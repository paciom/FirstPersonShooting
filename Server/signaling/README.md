# Photon Arena signaling server

The one always-on backend piece: match signaling for online PvP, plus the
player accounts API. Browsers connect over WebSocket just long enough to pair
up (match codes + WebRTC offer/answer/ICE relay); the battle itself then runs
peer-to-peer over WebRTC DataChannels. See `Assets/Scripts/Net/` for the
Unity side.

## The lobby

Waiting rooms are public: a host advertises the arena it will load and a name,
and anyone signed on to the server can page through the open ones and click in.
The exact frames are in the header of `server.js`; the parts that matter:

- `{t:"host", arena, name}` — open a room. `arena` is an index into the
  client's `ArenaLibrary`; the server clamps it and never interprets it, so
  adding arenas needs no server change. `name` is stripped to word characters
  and capped at 16.
- `{t:"list", page}` — one page of five, **oldest first**, so the top of the
  list is whoever has waited longest. The reply echoes the CLAMPED page, which
  is what walks a client back onto a real page as rooms drain.
- `{t:"quick", arena, name}` — auto-match: takes the longest-waiting room, or
  hosts one when the lobby is empty. Two players pressing it pair up. Which
  side you got is in the reply (`joined` vs `hosted`), not in the request.

Only rooms with a host and no guest are listed — a full room is a live match.

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

## Leaderboards

`leaderboard.js` keeps one board per (mode, arena) plus an overall board per
mode, each holding every signed-in player's personal best. Guests play fine;
only signed-in players can post, because a row needs a name to show.

- `POST /api/score` (bearer) `{mode, arena, score}` -> `{ok, improved, best,
  rank}`. `mode` must be one of the keys in `MODES`; `arena` is the client's
  display name, slugged server-side (unknown arenas are fine, adding one needs
  no server change); `score` is an integer within the mode's plausibility cap
  and is REFUSED above it, never clamped.
- `GET /api/leaderboard?mode=..&arena=..&top=10` (bearer optional) -> the top
  rows, plus `myRank`/`myScore` for the caller. Omit `arena` for the overall
  board.
- `GET /api/boards` -> the mode table (direction, unit, cap).

Sort order lives in the row key (`<score, inverted for higher-is-better>_<time>_<userId>`)
because Table Storage returns a partition in row-key order and has no ORDER BY.
Each player owns exactly one row per board: a new best deletes the old row.

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
