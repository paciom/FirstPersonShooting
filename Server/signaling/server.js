// Photon Arena signaling server.
//
// The only always-on backend piece of the P2P architecture: browsers connect
// here over WebSocket just long enough to pair up (match codes + WebRTC
// SDP/ICE relay), then all battle traffic flows peer-to-peer over WebRTC.
// After the handshake this server is idle for the rest of the match.
//
// Protocol (JSON text frames):
//   client -> server:
//     {t:"host", arena, name}         create a room, get a match code
//     {t:"join", code:"ABCDE"}        join a hosted room
//     {t:"quick", arena, name}        auto-match: take the oldest open room, else host
//     {t:"list", page}                page of open rooms, oldest first
//     {t:"signal", data:{...}}        opaque payload relayed to the room's other peer
//     {t:"leave"}                     leave the current room
//   server -> client:
//     {t:"hosted", code, ice:[...]}   room created; ice = RTCIceServer list
//     {t:"joined", ice:[...]}         joined; sent to the guest
//     {t:"rooms", page, pages, total, rooms:[{code, arena, name, age}]}
//     {t:"peer-joined"}               sent to the host when the guest arrives
//     {t:"signal", data:{...}}        relayed from the other peer
//     {t:"peer-left"}                 the other peer disconnected
//     {t:"error", reason}             bad code, full room, malformed message
//
// The room list is the lobby: a player picks an arena off it and joins, so
// every waiting room carries the arena its host will load and the name to show
// beside it. Sorted OLDEST FIRST — the top of the list is whoever has been
// waiting longest, which is both the fair pick and the one "quick" takes, so
// the button and the list never disagree about who is next.
//
// TURN: with CF_TURN_KEY_ID + CF_TURN_API_TOKEN set, short-lived Cloudflare
// TURN credentials are minted server-side and included in the ice list (the
// key never ships to browsers). Without them, STUN-only — direct P2P still
// works for most home networks; only the relay fallback is missing.

const http = require("http");
const crypto = require("crypto");
const { WebSocketServer } = require("ws");
const accounts = require("./accounts");

const PORT = process.env.PORT || 8787;
const CF_TURN_KEY_ID = process.env.CF_TURN_KEY_ID || "";
const CF_TURN_API_TOKEN = process.env.CF_TURN_API_TOKEN || "";
const TURN_TTL_SECONDS = 4 * 60 * 60;

// No I/O/0/1: codes get read aloud between kids across a room.
const CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
const CODE_LENGTH = 5;
const ROOM_TTL_MS = 30 * 60 * 1000;
const HEARTBEAT_MS = 30 * 1000;
const MAX_ROOMS = 500;
const MAX_CONNECTIONS_PER_IP = 16;
const LIST_PAGE_SIZE = 5;
const MAX_NAME_LENGTH = 16;

// A stray rejection anywhere (Node >=15) would otherwise kill the process —
// one bad frame taking the server down for every player.
process.on("unhandledRejection", (err) =>
  console.error("unhandled rejection:", err)
);

const STUN_SERVERS = [
  { urls: "stun:stun.cloudflare.com:3478" },
  { urls: "stun:stun.l.google.com:19302" },
];

/**
 * code -> { host, guest, idleSince, createdAt, arena, name }. idleSince is set
 * whenever the room is waiting for a guest (creation, or the guest leaving) and
 * cleared while the room is full — the TTL sweep only reaps waiting rooms,
 * never live matches.
 *
 * createdAt is separate from idleSince on purpose: idleSince restarts when a
 * guest bails, and a room that jumped back to the top of the lobby every time
 * somebody peeked at it would never let the longest waiter through.
 */
const rooms = new Map();

/** Rooms still waiting for a guest, longest wait first. */
function openRooms() {
  const open = [];
  for (const [code, room] of rooms)
    if (room.host && !room.guest) open.push([code, room]);
  open.sort((a, b) => a[1].createdAt - b[1].createdAt);
  return open;
}

/**
 * Names are typed by one player and shown to every other one, so they are
 * clamped to printable characters and a length the lobby row can fit. Falls
 * back rather than rejecting: an empty name is a fine reason to be listed as
 * a challenger, not a reason to refuse the room.
 */
function cleanName(raw) {
  const name = String(raw || "")
    .replace(/[^\w \-']/g, "")
    .trim()
    .slice(0, MAX_NAME_LENGTH);
  return name || "CHALLENGER";
}

/** Arena index, clamped to something plausible — the client owns the names. */
function cleanArena(raw) {
  const arena = Number.parseInt(raw, 10);
  return Number.isFinite(arena) && arena >= 0 && arena < 64 ? arena : 0;
}

function sendRoomPage(ws, page) {
  const open = openRooms();
  const pages = Math.max(1, Math.ceil(open.length / LIST_PAGE_SIZE));
  const wanted = Math.min(Math.max(0, Number.parseInt(page, 10) || 0), pages - 1);
  const now = Date.now();
  const slice = open.slice(wanted * LIST_PAGE_SIZE, (wanted + 1) * LIST_PAGE_SIZE);
  send(ws, {
    t: "rooms",
    // The CLAMPED page goes back, not the requested one: a client paging past
    // the end while rooms drain would otherwise sit on an empty page forever,
    // asking for a page that no longer exists.
    page: wanted,
    pages,
    total: open.length,
    rooms: slice.map(([code, room]) => ({
      code,
      arena: room.arena,
      name: room.name,
      age: Math.max(0, Math.round((now - room.createdAt) / 1000)),
    })),
  });
}

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
        // A hung mint must not stall every host/join behind the await.
        signal: AbortSignal.timeout(5000),
      }
    );
    if (!res.ok) throw new Error(`Cloudflare TURN mint failed: ${res.status}`);
    const body = await res.json();
    // generate-ice-servers returns { iceServers: [...] }; older examples show
    // a single object — normalize either shape to a flat array.
    const minted = body.iceServers ?? body.ice_servers ?? [];
    const servers = Array.isArray(minted) ? minted : [minted];
    // Refresh at 80% of TTL so handed-out credentials outlive the match start.
    turnCache = {
      servers,
      expiresAt: now + TURN_TTL_SECONDS * 1000 * 0.8,
    };
    return [...STUN_SERVERS, ...servers];
  } catch (err) {
    console.error("TURN credential mint failed, serving STUN-only:", err.message);
    // Negative cache: without this, every host/join during a Cloudflare
    // outage would stall the full mint timeout before falling back.
    turnCache = { servers: [], expiresAt: now + 60 * 1000 };
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
  if (room.guest === ws) {
    room.guest = null;
    // Back to waiting — the idle clock restarts.
    room.idleSince = Date.now();
  }
  if (notifyPeer && peer) send(peer, { t: "peer-left" });
  // A room without its host is unjoinable — drop it rather than strand guests.
  if (!room.host) {
    if (room.guest) room.guest.roomCode = null;
    rooms.delete(code);
  }
}

async function hostRoom(ws, msg) {
  leaveRoom(ws, true);
  if (rooms.size >= MAX_ROOMS)
    return send(ws, { t: "error", reason: "server-full" });
  const code = makeCode();
  if (!code) return send(ws, { t: "error", reason: "server-full" });
  const now = Date.now();
  rooms.set(code, {
    host: ws,
    guest: null,
    idleSince: now,
    createdAt: now,
    arena: cleanArena(msg.arena),
    name: cleanName(msg.name),
  });
  ws.roomCode = code;
  // ice is stringified: the Unity client (JsonUtility) can't parse nested
  // arrays, so it relays this string to the browser's RTCPeerConnection
  // without ever parsing it.
  send(ws, { t: "hosted", code, ice: JSON.stringify(await getIceServers()) });
}

async function joinRoom(ws, code) {
  const room = rooms.get(code);
  if (!room || !room.host) return send(ws, { t: "error", reason: "no-such-room" });
  if (room.guest) return send(ws, { t: "error", reason: "room-full" });
  if (room.host === ws) return send(ws, { t: "error", reason: "own-room" });
  leaveRoom(ws, true);
  room.guest = ws;
  room.idleSince = null;
  ws.roomCode = code;
  send(ws, { t: "joined", ice: JSON.stringify(await getIceServers()) });
  send(room.host, { t: "peer-joined" });
}

async function handleMessage(ws, raw) {
  let msg;
  try {
    msg = JSON.parse(raw);
  } catch {
    return send(ws, { t: "error", reason: "bad-json" });
  }
  // JSON.parse("null") and friends succeed — only objects are messages.
  if (!msg || typeof msg !== "object" || Array.isArray(msg)) {
    return send(ws, { t: "error", reason: "bad-json" });
  }

  switch (msg.t) {
    case "host":
      await hostRoom(ws, msg);
      break;

    case "join":
      await joinRoom(ws, String(msg.code || "").trim().toUpperCase());
      break;

    case "quick": {
      // Auto-match. Take the longest waiting room that isn't ours; with none
      // to take, become that room — so two players pressing the same button
      // pair up instead of both sitting in empty lobbies.
      const open = openRooms().filter(([, room]) => room.host !== ws);
      if (open.length > 0) await joinRoom(ws, open[0][0]);
      else await hostRoom(ws, msg);
      break;
    }

    case "list":
      sendRoomPage(ws, msg.page);
      break;
    case "signal": {
      // Signals racing a departure (peer left while ours was on the wire)
      // are dropped silently — the sender learns the real news from
      // peer-left, and an error here would kill its healthy session.
      const room = rooms.get(ws.roomCode);
      if (!room) return;
      const peer = otherPeer(room, ws);
      if (!peer) return;
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
  if (req.url.startsWith("/api/")) {
    // accounts.handle answers every request itself; this catch is the seat
    // belt that keeps one broken request from crashing the whole server.
    accounts.handle(req, res).catch((err) => {
      console.error("accounts handler failed:", err);
      if (!res.headersSent) res.writeHead(500);
      res.end();
    });
    return;
  }
  res.writeHead(404);
  res.end();
});

// maxPayload makes ws reject oversized frames before buffering them —
// the in-handler length check alone would run only after the buffering.
const wss = new WebSocketServer({ server, maxPayload: 64 * 1024 });

/** remoteAddress -> live connection count, for a crude per-IP cap. */
const connectionsPerIp = new Map();

function clientIp(ws, req) {
  // Behind Container Apps ingress the real address is in X-Forwarded-For.
  const forwarded = req.headers["x-forwarded-for"];
  if (forwarded) return String(forwarded).split(",")[0].trim();
  return req.socket.remoteAddress || "unknown";
}

wss.on("connection", (ws, req) => {
  const ip = clientIp(ws, req);
  const ipCount = (connectionsPerIp.get(ip) || 0) + 1;
  if (ipCount > MAX_CONNECTIONS_PER_IP) {
    ws.close(1013, "too many connections");
    return;
  }
  connectionsPerIp.set(ip, ipCount);

  ws.roomCode = null;
  ws.isAlive = true;
  ws.on("pong", () => (ws.isAlive = true));
  ws.on("message", (raw) => {
    if (raw.length > 64 * 1024) return ws.terminate();
    handleMessage(ws, raw.toString()).catch((err) =>
      console.error("handleMessage failed:", err)
    );
  });
  ws.on("close", () => {
    const count = (connectionsPerIp.get(ip) || 1) - 1;
    if (count <= 0) connectionsPerIp.delete(ip);
    else connectionsPerIp.set(ip, count);
    leaveRoom(ws, true);
  });
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
    // Only rooms still WAITING for a guest expire — a full room is a live
    // match, and reaping it would tear the game down under both players.
    if (room.idleSince != null && now - room.idleSince > ROOM_TTL_MS) {
      if (room.host) {
        send(room.host, { t: "error", reason: "room-expired" });
        room.host.roomCode = null;
      }
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

// Accounts warm up after the socket is listening: signaling must come up
// even if the store is misconfigured — /api/* answers 503 until init lands.
accounts.init().catch((err) => {
  console.error("accounts init failed (auth endpoints stay 503):", err.message);
});
