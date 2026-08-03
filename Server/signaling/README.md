# Photon Arena signaling server

The one always-on backend piece: match signaling for online PvP, plus the
player accounts API. Browsers connect over WebSocket just long enough to pair
up (match codes + WebRTC offer/answer/ICE relay); the battle itself then runs
peer-to-peer over WebRTC DataChannels. See `Assets/Scripts/Net/` for the
Unity side.

## Accounts API

`accounts.js` serves register / login / logout / me under `/api/` (see its
header comment for the exact shapes). Design points: username-only identity,
email OPTIONAL (login-by-email + future recovery), scrypt password hashes,
opaque bearer tokens stored hashed, per-IP rate limits.

Storage (`store.js`) picks a backend from the environment:

- `TABLES_ENDPOINT=https://<account>.table.core.windows.net` — Azure Table
  Storage via managed identity (production; the app's identity needs the
  **Storage Table Data Contributor** role on the storage account, and no
  secret is stored anywhere).
- `TABLES_CONNECTION_STRING=...` — same table, key auth, for dev machines.
- neither — a JSON file in `data/` (local dev; EPHEMERAL inside a container).

The table (`PhotonAccounts` unless `TABLES_TABLE_NAME` says otherwise) is
created on boot if missing.

## Run locally

```bash
npm install
npm start        # listens on ws://localhost:8787
```

`test.html` is a self-contained harness: open it in a browser while the server
runs and it drives a full host+guest handshake (two peers in one page, through
the real server) and an echo test on both DataChannels.

## TURN relay fallback (Cloudflare)

Without configuration the server hands out STUN only — fine for development
and for most home networks. To enable the relay fallback for players whose
NATs block direct connections, create a TURN key in the Cloudflare dashboard
(Realtime → TURN) and set:

```
CF_TURN_KEY_ID=<key id>
CF_TURN_API_TOKEN=<api token>
```

Credentials are minted server-side and included in the `ice` list sent to
clients; the key itself never reaches a browser.

## Deploy (Azure Container Apps)

```bash
az containerapp up --name photon-arena-signaling \
  --resource-group <rg> --location <region> \
  --ingress external --target-port 8787 \
  --source .
```

Then set the two TURN env vars as secrets on the app. The game connects to
`wss://<app-url>` — pass it via the `?server=` query on the game URL or update
the default in `Assets/Scripts/Net/NetSession.cs`.
