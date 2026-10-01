using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CommonLib
{
    /// <summary>
    /// Post-decrypt packet dump for protocol research.
    /// Writes one CSV-style line per packet to its own log4net repository/appender:
    ///   ts,process,dir,opcode,len,hex,note
    /// ts = ISO8601 UTC, dir = c2s|s2c, opcode = 4 hex digits, len = payload byte count,
    /// hex = full decrypted payload (after the 2-byte size + 2-byte opcode header).
    ///
    /// OFF by default. Settings (App.config appSettings, env var overrides):
    ///   PacketDump.Enabled          / ECO_PACKET_DUMP       (default false)
    ///   PacketDump.MaskCredentials  / ECO_PACKET_DUMP_MASK  (default true)
    ///   PacketDump.Directory        / ECO_PACKET_DUMP_DIR   (default "Logs")
    /// </summary>
    public static class PacketDump
    {
        public const ushort OP_USER_LOGIN = 0x001F;
        public const string NOTE_HAS_CREDENTIALS = "含帳密，勿上傳";
        public const string NOTE_MASKED = "帳密已遮蔽";

        private const string RepositoryName = "EcoPacketDump";
        private static readonly object initLock = new object();
        private static ILog dumpLog;
        private static string processName = "unknown";

        public static bool Enabled { get; private set; }
        public static bool MaskCredentials { get; private set; } = true;
        public static string OutputPath { get; private set; }

        /// <summary>
        /// Call once at process start, e.g. PacketDump.Initialize("login").
        /// </summary>
        public static void Initialize(string process)
        {
            lock (initLock)
            {
                processName = process;
                Enabled = ReadBool("PacketDump.Enabled", "ECO_PACKET_DUMP", false);
                MaskCredentials = ReadBool("PacketDump.MaskCredentials", "ECO_PACKET_DUMP_MASK", true);
                if (!Enabled)
                {
                    return;
                }

                string dir = ReadString("PacketDump.Directory", "ECO_PACKET_DUMP_DIR", "Logs");
                // relative paths resolve against the exe directory, same as log4net does for Logs\debug.log
                if (!Path.IsPathRooted(dir))
                {
                    dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir);
                }
                OutputPath = Path.GetFullPath(Path.Combine(dir, $"packets-{process}.log"));
                dumpLog = CreateLogger(OutputPath);
                Logger.Info($"PacketDump enabled: {OutputPath} (mask credentials: {MaskCredentials})");
            }
        }

        /// <summary>
        /// Log a decrypted client-to-server packet. Call only after Encryption.Decrypt.
        /// </summary>
        public static void LogC2S(Packets.BasePacket packet, string handler)
        {
            if (!Enabled || packet == null) return;
            Write("c2s", packet.ProtocolID, packet.Data, handler);
        }

        public static void Write(string dir, ushort opcode, byte[] payload, string handler)
        {
            if (!Enabled || dumpLog == null) return;
            try
            {
                dumpLog.Info(FormatLine(DateTime.UtcNow, processName, dir, opcode, payload, handler, MaskCredentials));
            }
            catch (Exception e)
            {
                // never let research logging break packet handling
                Logger.Error(e);
            }
        }

        /// <summary>
        /// Pure formatter (no I/O), kept public for testing.
        /// </summary>
        public static string FormatLine(DateTime utc, string process, string dir, ushort opcode, byte[] payload, string handler, bool mask)
        {
            byte[] data = payload ?? new byte[0];
            string note = handler ?? string.Empty;

            if (opcode == OP_USER_LOGIN)
            {
                if (mask)
                {
                    data = MaskLoginPayload(data);
                    note = JoinNote(note, NOTE_MASKED);
                }
                else
                {
                    note = JoinNote(note, NOTE_HAS_CREDENTIALS);
                }
            }

            return string.Join(",",
                utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                process,
                dir,
                opcode.ToString("X4"),
                data.Length.ToString(CultureInfo.InvariantCulture),
                data.ToHexString(),
                note);
        }

        /// <summary>
        /// 001F payload layout (see Packets/LoginData.cs):
        /// [len][username\0][len][password-hash\0][len][mac][uint32 sso].
        /// Returns a copy with username, password hash and MAC bytes replaced by '*' (0x2A).
        /// Length prefixes and the SSO field are kept so the frame layout stays readable.
        /// If the payload cannot be parsed, every byte is masked (fail closed).
        /// </summary>
        public static byte[] MaskLoginPayload(byte[] payload)
        {
            byte[] masked = (byte[])payload.Clone();
            int offset = 0;
            for (int field = 0; field < 3; field++)
            {
                if (offset >= masked.Length)
                {
                    return masked.Length == 0 ? masked : Enumerable.Repeat((byte)0x2A, masked.Length).ToArray();
                }
                int len = masked[offset];
                if (offset + 1 + len > masked.Length)
                {
                    return Enumerable.Repeat((byte)0x2A, masked.Length).ToArray();
                }
                for (int i = offset + 1; i < offset + 1 + len; i++)
                {
                    masked[i] = 0x2A;
                }
                offset += len + 1;
            }
            return masked;
        }

        private static string JoinNote(string a, string b)
        {
            // ';' not ',' so the note never adds CSV columns
            return string.IsNullOrEmpty(a) ? b : $"{a}; {b}";
        }

        private static ILog CreateLogger(string path)
        {
            var repo = LogManager.GetAllRepositories().FirstOrDefault(r => r.Name == RepositoryName)
                       ?? LogManager.CreateRepository(RepositoryName);
            var hierarchy = (Hierarchy)repo;

            var layout = new PatternLayout("%message%newline");
            layout.ActivateOptions();

            var appender = new RollingFileAppender
            {
                Name = "packetDumpAppender",
                File = path,
                AppendToFile = true,
                Encoding = new UTF8Encoding(false),
                RollingStyle = RollingFileAppender.RollingMode.Size,
                MaximumFileSize = "50MB",
                MaxSizeRollBackups = 10,
                StaticLogFileName = true,
                LockingModel = new FileAppender.MinimalLock(),
                Layout = layout
            };
            appender.ActivateOptions();

            hierarchy.Root.RemoveAllAppenders();
            hierarchy.Root.AddAppender(appender);
            hierarchy.Root.Level = log4net.Core.Level.All;
            hierarchy.Configured = true;

            return LogManager.GetLogger(RepositoryName, "PacketDump");
        }

        private static string ReadString(string appKey, string envKey, string fallback)
        {
            string env = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            try
            {
                string app = ConfigurationManager.AppSettings[appKey];
                if (!string.IsNullOrWhiteSpace(app)) return app.Trim();
            }
            catch (ConfigurationErrorsException) { }
            return fallback;
        }

        private static bool ReadBool(string appKey, string envKey, bool fallback)
        {
            string v = ReadString(appKey, envKey, null);
            if (v == null) return fallback;
            switch (v.ToLowerInvariant())
            {
                case "1": case "true": case "yes": case "on": return true;
                case "0": case "false": case "no": case "off": return false;
                default: return fallback;
            }
        }
    }
}
