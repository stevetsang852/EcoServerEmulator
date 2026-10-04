using CommonLib;
using CommonLib.Packets;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace ResearchClient
{
    internal static class Program
    {
        private const string LocalHost = "127.0.0.1";
        private const int WorldPort = 17831;

        private static int Main(string[] args)
        {
            try
            {
                string username = args.Length > 0 ? args[0] : ReadRequiredLine("Account");
                string password = args.Length > 1 ? args[1] : ReadPassword();
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("Account and password are required.");
                }

                Console.WriteLine("Connecting to local WorldServer...");
                ServerEndpoint loginEndpoint = AuthenticateWorld(username, password);
                Console.WriteLine("Connecting to local LoginServer...");
                ServerEndpoint mapEndpoint = AuthenticateLogin(username, password, loginEndpoint);
                Console.WriteLine("Connecting to local MapServer...");
                EnterMapAndMove(username, password, mapEndpoint, _selectedSlot);
                Console.WriteLine("RESEARCH_CLIENT_SUCCESS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Research client failed: {exception.Message}");
                return 1;
            }
        }

        private static ServerEndpoint AuthenticateWorld(string username, string password)
        {
            using (var connection = new GameConnection(LocalHost, WorldPort))
            {
                PasswordChallenge challenge = InitializeVersionedServer(connection);
                RequireSuccess(connection.SendAndWait(
                    new BasePacket(0x001f, BuildLoginData(username, HashPassword(password, challenge))),
                    0x0020), 0x0020);

                connection.SendAndWait(new BasePacket(0x002f), 0x0030);
                connection.Send(new BasePacket(0x0031));
                BasePacket serverInfo = connection.ReadUntil(0x0033);
                ServerEndpoint endpoint = ParseServerInfo(serverInfo);
                Console.WriteLine($"LOGIN_SERVER {LocalHost}:{endpoint.Port}");
                return endpoint;
            }
        }

        private static ServerEndpoint AuthenticateLogin(string username, string password, ServerEndpoint loginEndpoint)
        {
            using (var connection = new GameConnection(loginEndpoint.Address, loginEndpoint.Port))
            {
                PasswordChallenge challenge = InitializeVersionedServer(connection);
                RequireSuccess(connection.SendAndWait(
                    new BasePacket(0x001f, BuildLoginData(username, HashPassword(password, challenge))),
                    0x0020), 0x0020);

                BasePacket characterList = connection.ReadUntil(0x0028);
                byte? selectedSlot = FindFirstCharacterSlot(characterList.Data);
                if (!selectedSlot.HasValue)
                {
                    selectedSlot = FindFirstEmptySlot(characterList.Data);
                    string name = CreateCharacterName();
                    Console.WriteLine($"No character found; creating {name} in slot {selectedSlot.Value}.");
                    byte[] createData = new byte[] { selectedSlot.Value }
                        .Concat(name.ToTBytes())
                        .Concat(new byte[] { 0, 0 })
                        .Concat(((ushort)1).ToBytes())
                        .Concat(new byte[] { 0 })
                        .Concat(((ushort)0).ToBytes())
                        .ToArray();

                    BasePacket creationResult = connection.SendAndWait(new BasePacket(0x00a0, createData), 0x00a1);
                    if (creationResult.Data == null || creationResult.Data.Length < 4 || creationResult.Data.ToUInt32() != 0)
                    {
                        throw new InvalidOperationException("Character creation was rejected by LoginServer.");
                    }
                }

                connection.Send(new BasePacket(0x00a7, new byte[] { selectedSlot.Value }));
                BasePacket mapIdPacket = connection.ReadUntil(0x00a8);
                connection.Send(new BasePacket(0x0032, mapIdPacket.Data));
                return ParseMapServer(connection.ReadUntil(0x0033));
            }
        }

        private static void EnterMapAndMove(string username, string password, ServerEndpoint endpoint, byte slot)
        {
            using (var connection = new GameConnection(endpoint.Address, endpoint.Port))
            {
                connection.Send(new BasePacket(0x000a));
                BasePacket challengePacket = connection.ReadUntil(0x000f);
                PasswordChallenge challenge = ParseChallenge(challengePacket);

                RequireSuccess(connection.SendAndWait(
                    new BasePacket(0x0010, BuildLoginData(username, HashPassword(password, challenge))),
                    0x0011), 0x0011);

                byte[] selection = new byte[6];
                selection[4] = slot;
                connection.Send(new BasePacket(0x01fd, selection));

                byte currentX = 100;
                byte currentY = 100;
                bool enteredMap = false;
                while (!enteredMap)
                {
                    BasePacket packet = connection.ReadPacket();
                    if (packet.ProtocolID == 0x01ff && packet.Data != null)
                    {
                        ParseCharacterPosition(packet.Data, out currentX, out currentY);
                    }

                    enteredMap = packet.ProtocolID == 0x1b67;
                }
                Console.WriteLine($"MAP_ENTERED slot={slot}");

                connection.Send(new BasePacket(0x11fe));
                connection.ReadUntil(0x1239);

                byte nextX = currentX == byte.MaxValue ? (byte)(currentX - 1) : (byte)(currentX + 1);
                byte nextY = currentY;
                byte[] movement = new byte[8];
                Buffer.BlockCopy(((ushort)nextX).ToBytes(), 0, movement, 0, 2);
                Buffer.BlockCopy(((ushort)nextY).ToBytes(), 0, movement, 2, 2);
                Buffer.BlockCopy(((ushort)2).ToBytes(), 0, movement, 4, 2);
                Buffer.BlockCopy(((ushort)6).ToBytes(), 0, movement, 6, 2);
                connection.Send(new BasePacket(0x11f8, movement));
                Console.WriteLine($"MOVE_SENT X={nextX} Y={nextY}");
            }
        }

        private static byte _selectedSlot;

        private static PasswordChallenge InitializeVersionedServer(GameConnection connection)
        {
            connection.Send(new BasePacket(0x0001, new byte[4]));
            PasswordChallenge challenge = ParseChallenge(connection.ReadUntil(0x001e));
            connection.SendAndWait(new BasePacket(0x000a), 0x000b);
            return challenge;
        }

        private static byte[] BuildLoginData(string username, string passwordHash)
        {
            byte[] macAddress = new byte[6];
            return username.ToTBytes()
                .Concat(passwordHash.ToTBytes())
                .Concat(new byte[] { (byte)macAddress.Length })
                .Concat(macAddress)
                .Concat(new byte[4])
                .ToArray();
        }

        private static string HashPassword(string password, PasswordChallenge challenge)
        {
            string databasePassword;
            if (password.Length == 32 && password.All(Uri.IsHexDigit))
            {
                databasePassword = password.ToLowerInvariant();
            }
            else
            {
                using (MD5 md5 = MD5.Create())
                {
                    databasePassword = BitConverter.ToString(md5.ComputeHash(Encoding.ASCII.GetBytes(password)))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                }
            }

            return Utilities.PasswordHash(databasePassword,
                challenge.FrontWord.ToString(CultureInfo.InvariantCulture),
                challenge.BackWord.ToString(CultureInfo.InvariantCulture)).ToHexString();
        }

        private static PasswordChallenge ParseChallenge(BasePacket packet)
        {
            if (packet.Data == null || packet.Data.Length < 8)
            {
                throw new InvalidDataException($"Password challenge 0x{packet.ProtocolID:X4} is too short.");
            }

            return new PasswordChallenge
            {
                FrontWord = packet.Data.ToUInt32(0),
                BackWord = packet.Data.ToUInt32(4)
            };
        }

        private static byte? FindFirstCharacterSlot(byte[] data)
        {
            int offset = 0;
            RequireArrayCount(data, ref offset, 4);
            bool[] hasName = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                int length = ReadByte(data, ref offset);
                EnsureAvailable(data, offset, length);
                hasName[i] = length > 0;
                offset += length;
            }

            SkipColumn(data, ref offset, 1, 4); // Race
            SkipColumn(data, ref offset, 1, 4); // Form
            SkipColumn(data, ref offset, 1, 4); // Sex
            SkipColumn(data, ref offset, 2, 4); // HairStyle
            SkipColumn(data, ref offset, 1, 4); // HairColor
            SkipColumn(data, ref offset, 2, 4); // Wig
            int count = ReadByte(data, ref offset);
            if (count != 4)
            {
                throw new InvalidDataException("Unexpected character slot count.");
            }

            for (byte slot = 0; slot < 4; slot++)
            {
                bool occupied = ReadByte(data, ref offset) != 0 || hasName[slot];
                if (occupied)
                {
                    _selectedSlot = slot;
                    return slot;
                }
            }

            return null;
        }

        private static byte FindFirstEmptySlot(byte[] data)
        {
            // The server's empty slots are zero-initialized; after the existing-slot scan
            // reports none, slot zero is the first valid slot.
            if (data == null || data.Length == 0)
            {
                throw new InvalidDataException("LoginServer returned an empty character list.");
            }

            _selectedSlot = 0;
            return 0;
        }

        private static string CreateCharacterName()
        {
            byte[] randomBytes = new byte[4];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            uint suffix = randomBytes.ToUInt32() % 1000000;
            return "RC" + suffix.ToString("D6", CultureInfo.InvariantCulture);
        }

        private static ServerEndpoint ParseServerInfo(BasePacket packet)
        {
            int offset = 0;
            string name = ReadTString(packet.Data, ref offset);
            string address = ReadTString(packet.Data, ref offset);
            int separator = address.LastIndexOf(':');
            if (separator <= 0 || !ushort.TryParse(address.Substring(separator + 1), out ushort port))
            {
                throw new InvalidDataException($"Invalid LoginServer address in '{name}' entry.");
            }

            return new ServerEndpoint(address.Substring(0, separator), port);
        }

        private static ServerEndpoint ParseMapServer(BasePacket packet)
        {
            if (packet.Data == null || packet.Data.Length < 6 || packet.Data[0] == 0)
            {
                throw new InvalidDataException("LoginServer did not provide a MapServer endpoint.");
            }

            int offset = 1;
            string address = ReadTString(packet.Data, ref offset);
            if (packet.Data.Length < offset + 4)
            {
                throw new InvalidDataException("MapServer response is missing its port.");
            }

            uint port = packet.Data.ToUInt32(offset);
            return new ServerEndpoint(RequireLoopback(address), port);
        }

        private static string RequireLoopback(string address)
        {
            IPAddress parsed;
            if (!IPAddress.TryParse(address, out parsed) || !IPAddress.IsLoopback(parsed) ||
                !parsed.Equals(IPAddress.Loopback))
            {
                throw new InvalidOperationException($"Refusing server address outside 127.0.0.1: '{address}'.");
            }

            return LocalHost;
        }

        private static string ReadTString(byte[] data, ref int offset)
        {
            int length = ReadByte(data, ref offset);
            EnsureAvailable(data, offset, length);
            string value = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            if (value.EndsWith("\0", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
            }
            return value;
        }

        private static void ParseCharacterPosition(byte[] data, out byte x, out byte y)
        {
            int offset = 12;
            EnsureAvailable(data, offset, 1);
            offset++;
            ReadTString(data, ref offset);
            offset += 3 + 2 + 1 + 2 + 1 + 2 + 4 + 4;
            EnsureAvailable(data, offset, 3);
            x = data[offset];
            y = data[offset + 1];
        }

        private static void SkipColumn(byte[] data, ref int offset, int elementSize, int expectedCount)
        {
            RequireArrayCount(data, ref offset, expectedCount);
            int length = elementSize * expectedCount;
            EnsureAvailable(data, offset, length);
            offset += length;
        }

        private static void RequireArrayCount(byte[] data, ref int offset, int expectedCount)
        {
            int count = ReadByte(data, ref offset);
            if (count != expectedCount)
            {
                throw new InvalidDataException($"Unexpected character array size {count}; expected {expectedCount}.");
            }
        }

        private static int ReadByte(byte[] data, ref int offset)
        {
            EnsureAvailable(data, offset, 1);
            return data[offset++];
        }

        private static void EnsureAvailable(byte[] data, int offset, int count)
        {
            if (data == null || offset < 0 || count < 0 || offset > data.Length - count)
            {
                throw new InvalidDataException("Server returned a truncated packet.");
            }
        }

        private static void RequireSuccess(BasePacket packet, ushort resultPacketId)
        {
            if (packet.Data == null || packet.Data.Length < 4 || packet.Data.ToUInt32() != 0)
            {
                throw new InvalidOperationException($"Authentication failed (response 0x{resultPacketId:X4}).");
            }

            Console.WriteLine("LOGIN_SUCCESS");
        }

        private static string ReadRequiredLine(string prompt)
        {
            Console.Write($"{prompt}: ");
            return Console.ReadLine();
        }

        private static string ReadPassword()
        {
            Console.Write("Password: ");
            StringBuilder value = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return value.ToString();
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (value.Length > 0)
                    {
                        value.Length--;
                    }
                    continue;
                }
                if (!char.IsControl(key.KeyChar))
                {
                    value.Append(key.KeyChar);
                }
            }
        }

        private sealed class PasswordChallenge
        {
            public uint FrontWord { get; set; }
            public uint BackWord { get; set; }
        }

        private sealed class ServerEndpoint
        {
            public ServerEndpoint(string address, uint port)
            {
                if (port == 0 || port > ushort.MaxValue)
                {
                    throw new InvalidDataException($"Invalid server port {port}.");
                }

                Address = RequireLoopback(address);
                Port = (ushort)port;
            }

            public string Address { get; private set; }
            public ushort Port { get; private set; }
        }

        private sealed class GameConnection : IDisposable
        {
            private readonly TcpClient client;
            private readonly NetworkStream stream;
            private readonly Encryption encryption;

            public GameConnection(string address, uint port)
            {
                client = new TcpClient();
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 10000;
                client.NoDelay = true;
                client.Connect(RequireLoopback(address), checked((int)port));
                stream = client.GetStream();
                encryption = new Encryption();
                try
                {
                    EstablishEncryption();
                }
                catch
                {
                    stream.Dispose();
                    client.Close();
                    throw;
                }
            }

            public void Send(BasePacket packet)
            {
                byte[] encrypted = encryption.Encrypt(packet.ToBytes());
                stream.Write(encrypted, 0, encrypted.Length);
                Console.WriteLine($"TX 0x{packet.ProtocolID:X4} ({packet.DataLength} bytes)");
            }

            public BasePacket SendAndWait(BasePacket packet, ushort expectedId)
            {
                Send(packet);
                return ReadUntil(expectedId);
            }

            public BasePacket ReadUntil(ushort expectedId)
            {
                while (true)
                {
                    BasePacket packet = ReadPacket();
                    if (packet.ProtocolID == expectedId)
                    {
                        return packet;
                    }
                }
            }

            public BasePacket ReadPacket()
            {
                byte[] header = ReadExact(8);
                uint encryptedLength = header.ToUInt32(0);
                uint plainLength = header.ToUInt32(4);
                if (encryptedLength == 0 || encryptedLength > 1024 * 1024 || encryptedLength % 16 != 0 ||
                    plainLength < 4 || plainLength > encryptedLength)
                {
                    throw new InvalidDataException("Invalid encrypted packet length.");
                }

                byte[] ciphertext = ReadExact((int)encryptedLength);
                byte[] envelope = header.Concat(ciphertext).ToArray();
                byte[] plain = encryption.Decrypt(envelope, 8);
                if (plainLength > plain.Length)
                {
                    throw new InvalidDataException("Encrypted packet has an invalid plaintext length.");
                }

                byte[] data = plain.Take((int)plainLength).ToArray();
                BasePacket packet = new BasePacket(data);
                Console.WriteLine($"RX 0x{packet.ProtocolID:X4} ({packet.DataLength} bytes)");
                return packet;
            }

            public void Dispose()
            {
                stream.Dispose();
                client.Close();
            }

            private void EstablishEncryption()
            {
                byte[] initPacket = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x10 };
                stream.Write(initPacket, 0, initPacket.Length);

                ReadUInt32(); // Reserved
                string generator = ReadSizedAscii();
                string primeText = ReadSizedAscii();
                string serverPublicText = ReadSizedAscii();
                if (generator != "3")
                {
                    throw new InvalidDataException($"Unexpected Diffie-Hellman generator '{generator}'.");
                }

                BigInteger prime = ParseHexInteger(primeText);
                BigInteger serverPublic = ParseHexInteger(serverPublicText);
                byte[] secret = new byte[64];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(secret);
                }
                secret[secret.Length - 1] = 0;
                BigInteger privateKey = new BigInteger(secret);
                BigInteger publicKey = BigInteger.ModPow(new BigInteger(3), privateKey, prime);
                string publicKeyText = publicKey.ToString("x").TrimStart('0').PadLeft(256, '0');
                if (publicKeyText.Length != 256)
                {
                    throw new InvalidDataException("Could not encode a 256-byte Diffie-Hellman public key.");
                }

                byte[] clientKey = new byte[260];
                Buffer.BlockCopy(((uint)256).ToBytes(), 0, clientKey, 0, 4);
                Buffer.BlockCopy(Encoding.ASCII.GetBytes(publicKeyText), 0, clientKey, 4, 256);
                stream.Write(clientKey, 0, clientKey.Length);

                BigInteger shared = BigInteger.ModPow(serverPublic, privateKey, prime);
                string sharedText = shared.ToString("x").TrimStart('0').PadLeft(256, '0');
                byte[] keyText = Encoding.ASCII.GetBytes(sharedText.Substring(0, 32));
                for (int i = 0; i < keyText.Length; i++)
                {
                    if (keyText[i] >= 'a')
                    {
                        keyText[i] -= 48;
                    }
                }

                encryption.SetAesKey(ParseHexBytes(Encoding.ASCII.GetString(keyText)));
            }

            private string ReadSizedAscii()
            {
                uint length = ReadUInt32();
                if (length == 0 || length > 4096)
                {
                    throw new InvalidDataException($"Invalid Diffie-Hellman field length {length}.");
                }

                return Encoding.ASCII.GetString(ReadExact((int)length));
            }

            private uint ReadUInt32()
            {
                return ReadExact(4).ToUInt32();
            }

            private byte[] ReadExact(int length)
            {
                byte[] data = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int count = stream.Read(data, read, length - read);
                    if (count == 0)
                    {
                        throw new EndOfStreamException("Server closed the connection.");
                    }
                    read += count;
                }
                return data;
            }

            private static BigInteger ParseHexInteger(string value)
            {
                return BigInteger.Parse("0" + value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            }

            private static byte[] ParseHexBytes(string value)
            {
                if (value.Length % 2 != 0)
                {
                    throw new InvalidDataException("Invalid AES key encoding.");
                }

                byte[] result = new byte[value.Length / 2];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = byte.Parse(value.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                return result;
            }
        }
    }
}
