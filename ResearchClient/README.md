# ResearchClient

`ResearchClient` is a local-only protocol test client for the three-server setup. It performs the DH/AES exchange, authenticates through World and Login, creates a character if the account has none, enters the returned MapServer, unlocks movement, and sends one small move.

Build `EcoServerEmulator.sln`, start WorldServer, LoginServer, and MapServer, then run `ResearchClient.exe`. It prompts for an account and password (input is masked); a 32-character hexadecimal password is treated as the MD5 value stored in `Account.password`, otherwise it hashes the entered plaintext password with MD5. All server connections are restricted to `127.0.0.1`.

The client sends movement to a `CharaData` position, and MapServer persists the coordinates. It does not implement combat, NPCs, or external server access.
