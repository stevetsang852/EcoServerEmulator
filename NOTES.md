# Local protocol research notes

## Implemented path

| Client action | Packet(s) | Result |
| --- | --- | --- |
| Establish transport encryption | `INIT_PACKET`, DH public key, AES-128-ECB | Per-connection key exchange |
| Authenticate with WorldServer | `0001`, `000A`, `001F`, `002F`, `0031` | LoginServer endpoint from `0033` |
| Authenticate with LoginServer | `0001`, `000A`, `001F` | Character list `0028` / equipment `0029` |
| Create a character when needed | `00A0` | Creation result `00A1` |
| Request MapServer | `00A7`, `0032` | Map ID `00A8`, endpoint `0033` |
| Enter the map and move | `000A`, `0010`, `01FD`, `11FE`, `11F8` | Map entry `1B67`, movement persisted |

The research client only connects to `127.0.0.1`. Its movement packet follows SagaECO's `11F8` layout: signed 16-bit X and Y, 16-bit direction, then 16-bit movement type.

## Local setup status

For Docker testing, `docker compose up --build -d` supplies MySQL, restores NuGet
packages, builds with Mono, and starts all three servers. The database connection
is supplied through `ECO_DB_CONNECTION_STRING`, so no credential file is baked
into the image.

Native Windows builds still require a local MySQL instance, a valid
`CommonLib/settings.json` connection string, restored NuGet packages, and the
.NET Framework 4.5.2 developer targeting pack.
