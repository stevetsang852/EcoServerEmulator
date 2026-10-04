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

## Docker

Docker Compose starts MySQL, imports `sql/eco.sql`, builds the emulator with
Mono, and runs WorldServer, LoginServer, and MapServer in one application
container.

### Prerequisites

- Docker Desktop configured for **Linux containers**
- On Windows: WSL 2 and Virtual Machine Platform enabled, followed by a reboot
- Ports `17831`, `17832`, and `17833` available on `127.0.0.1`

From an elevated PowerShell window, missing Windows features can be enabled with:

```powershell
wsl --install --no-distribution
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
```

Reboot Windows, start Docker Desktop, and wait until its Linux engine reports
that it is running.

### Start and verify

On Windows, start Docker Desktop and double-click `start_in_docker.bat`.
It builds and starts the containers, waits for them to become healthy, and
prints the local connection details. Use `start_in_docker.bat /no-pause` when
running from a terminal or automation without an interactive pause.

```sh
cd EcoServerEmulator-master
docker compose up --build -d
docker compose ps
```

Both `db` and `eco` should become `healthy`. Follow the server logs when needed:

The first start can take several minutes while MySQL initializes and imports
the UTF-8 seed data. Game ports are bound to `127.0.0.1` for local testing only:
WorldServer `17831`, LoginServer `17832`, and MapServer `17833`.

```sh
docker compose logs -f db eco
```

Run the complete local login, character selection, map entry, and movement test
with the seeded `test` account (the default plaintext password is `111111`):

```sh
docker compose exec eco /app/run-research-client.sh test 111111
```

Success ends with:

```text
MAP_ENTERED slot=0
MOVE_SENT X=...
RESEARCH_CLIENT_SUCCESS
```

### Stop or reset

```sh
docker compose down
```

To erase the test database and re-import `sql/eco.sql` on the next start:

```sh
docker compose down -v
```

For non-default database credentials, set `MYSQL_PASSWORD` and
`MYSQL_ROOT_PASSWORD` in the shell before starting Compose.


## Client
  This emulator is compatible with the last version of japanese official eco client(2017/08/31), which can be downloaded [here](https://drive.google.com/file/d/18NU7MRoc79DAIjFVyVUb_q6cjzYEtdbz/view?usp=sharing).

### Steps for using the client
  1. Modify the address and port in server.lst file to make it the same as your server settings. 
  2. Start "eco.exe" with arguements `/launch -u:<username> -p:<password>`. (You can find an example in "start_eco.bat" batch file)
  3. Edit database and make sure the "account table" has your login account as added in step 2.
