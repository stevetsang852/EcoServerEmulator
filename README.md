# [EcoServerEmulator](https://github.com/cm-MMK-2/EcoServerEmulator)  
<br />

# <span style="color: red; ">NOT a full server! ONLY for testing! </span>


## <span style="color: green; ">For the Memories of Emil Chronicle Online </span>
<br />

## Current Available: Login, Create Character, Move

  ![Pre](https://github.com/cm-MMK-2/EcoServerEmulator/blob/master/preview/progress.png)
<br />


## How to use
  1. Setup a mysql database and import /sql/eco.sql
  2. Setup connection string in /CommonLib/settings.json
  3. Build the project and launch WorldServer, LoginServer and MapServer.
<br />


## Client
  This emulator is compatible with the last version of japanese official eco client(2017/08/31), which can be downloaded [here](https://drive.google.com/file/d/18NU7MRoc79DAIjFVyVUb_q6cjzYEtdbz/view?usp=sharing).

### Steps for using the client
  1. Modify the address and port in server.lst file to make it the same as your server settings. 
  2. Start "eco.exe" with arguements `/launch -u:<username> -p:<password>`. (You can find an example in "start_eco.bat" batch file)
  3. Edit database and make sure the "account table" has your login account as added in step 2.


## Post-decrypt packet dump (protocol research)
  Off by default. When enabled, LoginServer and MapServer write one line per decrypted client packet for `001F` (UserLogin), `11FE` (RequestMove) and `11F8` (CharaMove) to a separate log file:

  - `Logs/packets-login.log` (LoginServer) and `Logs/packets-map.log` (MapServer), next to the server exe (same folder as the existing `Logs\debug.log`, e.g. `MapServer/bin/Debug/Logs/`). An absolute `PacketDump.Directory` is used as-is; the full path is printed at startup.
  - Line format: `ts,process,dir,opcode,len,hex,note`, e.g. `2026-10-01T15:00:00.000Z,map,c2s,11FE,7,00010203040506,RequestMove`. `ts` is ISO8601 UTC, `len` is the payload byte count, `hex` is the full decrypted payload (after the 2-byte size and 2-byte opcode header). No header line.
  - Logging happens in the handlers, after `Encryption.Decrypt` in `CommonLib/Socket/EcoServerApp.cs`.

  Switches (in `LoginServer/App.config` / `MapServer/App.config` → `<appSettings>`, or env vars, which take priority):

  | appSettings key | env var | default |
  | --- | --- | --- |
  | `PacketDump.Enabled` | `ECO_PACKET_DUMP` | `false` |
  | `PacketDump.MaskCredentials` | `ECO_PACKET_DUMP_MASK` | `true` |
  | `PacketDump.Directory` | `ECO_PACKET_DUMP_DIR` | `Logs` |

  `001F` carries the username, salted password hash and MAC address. With masking on (default) those bytes are replaced by `2a` and the note says `帳密已遮蔽`; with masking off the note says `含帳密，勿上傳`. `Logs/` and `packets-*.log` are in `.gitignore` — never commit or upload these files. Note: the existing debug log (`Logs/debug.log`) already prints the 001F username and password hash unmasked; that is unchanged by this feature.

### Recording a session for eco-lab (step by step)
  1. Build the solution (Windows, .NET Framework 4.5.2) and make sure MySQL has `sql/eco.sql` imported and your login in the `account` table.
  2. Turn the dump on before starting the servers: set env var `ECO_PACKET_DUMP=1` (or `PacketDump.Enabled=true` in `LoginServer/App.config` and `MapServer/App.config`). Leave `ECO_PACKET_DUMP_MASK` unset so credentials stay masked.
  3. Start the servers in order: WorldServer → LoginServer → MapServer. Each prints the full path of its `packets-*.log` at startup.
  4. Point the client's `server.lst` at your own server (127.0.0.1 and the LoginServer port, 17832 by default). Only ever connect to your own server.
  5. Start `eco.exe /launch -u:<username> -p:<password>`, log in, enter the map, and walk a few steps. Use one client connection at a time.
  6. Close the client, then stop the servers.
  7. Collect `Logs/packets-login.log` and `Logs/packets-map.log` from next to each server exe. Check that the `001F` lines say `帳密已遮蔽`.

  > ⚠️ **Do not upload `Logs/debug.log`.** It prints the `001F` username and password hash **unmasked**. Only share the `packets-*.log` files, and only if the `001F` note says `帳密已遮蔽`.
