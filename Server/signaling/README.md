# Photon Arena signaling server

The one always-on backend piece of online PvP. Browsers connect here just long
enough to pair up (match codes + WebRTC offer/answer/ICE relay); the battle
itself then runs peer-to-peer over WebRTC DataChannels. See
`Assets/Scripts/Net/` for the Unity side.

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
