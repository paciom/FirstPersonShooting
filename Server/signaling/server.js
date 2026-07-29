// Photon Arena signaling server.
//
// The only always-on backend piece of the P2P architecture: browsers connect
// here over WebSocket just long enough to pair up (match codes + WebRTC
// SDP/ICE relay), then all battle traffic flows peer-to-peer over WebRTC.
// After the handshake this server is idle for the rest of the match.
//
// Protocol (JSON text frames):
//   client -> server:
//     {t:"host"}                      create a room, get a match code
//     {t:"join", code:"ABCDE"}        join a hosted room
//     {t:"signal", data:{...}}        opaque payload relayed to the room's other peer
//     {t:"leave"}                     leave the current room
//   server -> client:
//     {t:"hosted", code, ice:[...]}   room created; ice = RTCIceServer list
//     {t:"joined", ice:[...]}         joined; sent to the guest
//     {t:"peer-joined"}               sent to the host when the guest arrives
//     {t:"signal", data:{...}}        relayed from the other peer
//     {t:"peer-left"}                 the other peer disconnected
//     {t:"error", reason}             bad code, full room, malformed message
//
// TURN: with CF_TURN_KEY_ID + CF_TURN_API_TOKEN set, short-lived Cloudflare
// TURN credentials are minted server-side and included in the ice list (the
// key never ships to browsers). Without them, STUN-only — direct P2P still
// works for most home networks; only the relay fallback is missing.

const http = require("http");
const crypto = require("crypto");
const { WebSocketServer } = require("ws");

const PORT = process.env.PORT || 8787;
const CF_TURN_KEY_ID = process.env.CF_TURN_KEY_ID || "";
const CF_TURN_API_TOKEN = process.env.CF_TURN_API_TOKEN || "";
const TURN_TTL_SECONDS = 4 * 60 * 60;

// No I/O/0/1: codes get read aloud between kids across a room.
const CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
const CODE_LENGTH = 5;
const ROOM_TTL_MS = 30 * 60 * 1000;
const HEARTBEAT_MS = 30 * 1000;

const STUN_SERVERS = [
  { urls: "stun:stun.cloudflare.com:3478" },
  { urls: "stun:stun.l.google.com:19302" },
];

/** code -> { host: ws|null, guest: ws|null, createdAt: number } */
const rooms = new Map();

function makeCode() {
  for (let attempt = 0; attempt < 50; attempt++) {
    let code = "";
    const bytes = crypto.randomBytes(CODE_LENGTH);
    for (let i = 0; i < CODE_LENGTH; i++)
      code += CODE_ALPHABET[bytes[i] % CODE_ALPHABET.length];
    if (!rooms.has(code)) return code;
  }
  return null;
}

// Cached Cloudflare TURN credentials: one mint covers many matches within the
// TTL, and a mint failure degrades to STUN-only instead of failing the room.
let turnCache = { servers: null, expiresAt: 0 };

async function getIceServers() {
  if (!CF_TURN_KEY_ID || !CF_TURN_API_TOKEN) return STUN_SERVERS;
  const now = Date.now();
  if (turnCache.servers && now < turnCache.expiresAt) {
    return [...STUN_SERVERS, ...turnCache.servers];
  }
  try {
    const res = await fetch(
      `https://rtc.live.cloudflare.com/v1/turn/keys/${CF_TURN_KEY_ID}/credentials/generate-ice-servers`,
      {
        method: "POST",
        headers: {
          Authorization: `Bearer ${CF_TURN_API_TOKEN}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ ttl: TURN_TTL_SECONDS }),
      }
    );
    if (!res.ok) throw new Error(`Cloudflare TURN mint failed: ${res.status}`);
    const body = await res.json();
    const servers = body.iceServers ? [body.iceServers] : body.ice_servers || [];
    // Refresh at 80% of TTL so handed-out credentials outlive the match start.
    turnCache = {
      servers,
      expiresAt: now + TURN_TTL_SECONDS * 1000 * 0.8,
    };
    return [...STUN_SERVERS, ...servers];
  } catch (err) {
    console.error("TURN credential mint failed, serving STUN-only:", err.message);
    return STUN_SERVERS;
  }
}

function send(ws, msg) {
  if (ws && ws.readyState === ws.OPEN) ws.send(JSON.stringify(msg));
}

function otherPeer(room, ws) {
  return room.host === ws ? room.guest : room.host;
}

function leaveRoom(ws, notifyPeer) {
  const code = ws.roomCode;
  if (!code) return;
  const room = rooms.get(code);
  ws.roomCode = null;
  if (!room) return;
  const peer = otherPeer(room, ws);
  if (room.host === ws) room.host = null;
  if (room.guest === ws) room.guest = null;
  if (notifyPeer && peer) send(peer, { t: "peer-left" });
  // A room without its host is unjoinable — drop it rather than strand guests.
  if (!room.host || (!room.host && !room.guest)) {
    if (room.guest) {
      room.guest.roomCode = null;
    }
    rooms.delete(code);
  }
}

async function handleMessage(ws, raw) {
  let msg;
  try {
    msg = JSON.parse(raw);
  } catch {
    return send(ws, { t: "error", reason: "bad-json" });
  }

  switch (msg.t) {
    case "host": {
      leaveRoom(ws, true);
      const code = makeCode();
      if (!code) return send(ws, { t: "error", reason: "server-full" });
      rooms.set(code, { host: ws, guest: null, createdAt: Date.now() });
      ws.roomCode = code;
      // ice is stringified: the Unity client (JsonUtility) can't parse nested
      // arrays, so it relays this string to the browser's RTCPeerConnection
      // without ever parsing it.
      send(ws, { t: "hosted", code, ice: JSON.stringify(await getIceServers()) });
      break;
    }
    case "join": {
      const code = String(msg.code || "").trim().toUpperCase();
      const room = rooms.get(code);
      if (!room || !room.host) return send(ws, { t: "error", reason: "no-such-room" });
      if (room.guest) return send(ws, { t: "error", reason: "room-full" });
      leaveRoom(ws, true);
      room.guest = ws;
      ws.roomCode = code;
      send(ws, { t: "joined", ice: JSON.stringify(await getIceServers()) });
      send(room.host, { t: "peer-joined" });
      break;
    }
    case "signal": {
      const room = rooms.get(ws.roomCode);
      if (!room) return send(ws, { t: "error", reason: "not-in-room" });
      const peer = otherPeer(room, ws);
      if (!peer) return send(ws, { t: "error", reason: "no-peer" });
      send(peer, { t: "signal", data: msg.data });
      break;
    }
    case "leave":
      leaveRoom(ws, true);
      break;
    default:
      send(ws, { t: "error", reason: "unknown-type" });
  }
}

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ ok: true, rooms: rooms.size }));
    return;
  }
  res.writeHead(404);
  res.end();
});

const wss = new WebSocketServer({ server });

wss.on("connection", (ws) => {
  ws.roomCode = null;
  ws.isAlive = true;
  ws.on("pong", () => (ws.isAlive = true));
  ws.on("message", (raw) => {
    // Oversized frames are not part of any legitimate handshake.
    if (raw.length > 64 * 1024) return ws.terminate();
    handleMessage(ws, raw.toString());
  });
  ws.on("close", () => leaveRoom(ws, true));
  ws.on("error", () => leaveRoom(ws, true));
});

// Dead-socket sweep + stale-room cleanup.
setInterval(() => {
  for (const ws of wss.clients) {
    if (!ws.isAlive) {
      leaveRoom(ws, true);
      ws.terminate();
      continue;
    }
    ws.isAlive = false;
    ws.ping();
  }
  const now = Date.now();
  for (const [code, room] of rooms) {
    if (now - room.createdAt > ROOM_TTL_MS) {
      if (room.host) send(room.host, { t: "error", reason: "room-expired" });
      if (room.guest) send(room.guest, { t: "error", reason: "room-expired" });
      if (room.host) room.host.roomCode = null;
      if (room.guest) room.guest.roomCode = null;
      rooms.delete(code);
    }
  }
}, HEARTBEAT_MS);

server.listen(PORT, () => {
  console.log(`Photon Arena signaling listening on :${PORT}`);
  console.log(
    CF_TURN_KEY_ID
      ? "TURN: Cloudflare credentials will be minted server-side"
      : "TURN: not configured (STUN-only; set CF_TURN_KEY_ID + CF_TURN_API_TOKEN)"
  );
});
