// Photon Arena accounts: register, login, logout, me.
//
// The game is for kids, so the design leans anonymous-first: playing never
// requires an account, a username is the only required identity, and email is
// OPTIONAL (it exists so a player who wants recovery/login-by-email can have
// it — never demanded). Passwords are scrypt-hashed; sessions are opaque
// bearer tokens stored HASHED server-side, so a leaked database leaks no
// live session and no password.
//
// HTTP surface (JSON in, JSON out, CORS open — auth is header-based, no
// cookies, so cross-origin reads are harmless):
//   POST /api/register  {username, password, email?}      -> {ok, token, ...}
//   POST /api/login     {id, password}   id = name OR email -> {ok, token, ...}
//   POST /api/logout    Authorization: Bearer <token>       -> {ok}
//   GET  /api/me        Authorization: Bearer <token>       -> {ok, userId, ...}
// plus the leaderboard routes (/api/score, /api/leaderboard, /api/boards),
// which live in leaderboard.js and ride the same store and session tokens.
//
// Every reply is a FLAT object — the Unity client parses with JsonUtility,
// which cannot see nested objects, so keep it one level deep.
//
// Store layout (see store.js): one table, four partitions —
//   ("user",  <userId>)         the account row
//   ("name",  <usernameLower>)  unique-username index -> {userId}
//   ("email", <sha256(email)>)  unique-email index    -> {userId}
//   ("token", <sha256(token)>)  session               -> {userId, expiresAt}
//
// The email index key is HASHED because Table Storage row keys forbid
// characters ('/', '#', '?') that are legal in an email address; usernames
// are [a-z0-9_] so they key directly.

const crypto = require("crypto");
const { promisify } = require("util");
const { createStore } = require("./store");
const leaderboard = require("./leaderboard");

const scrypt = promisify(crypto.scrypt);

const USERNAME_RE = /^[A-Za-z0-9_]{3,16}$/;
// Deliberately loose — the only job is catching typos like a missing "@",
// not judging which mailboxes may exist.
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PASSWORD_MIN = 6;
const PASSWORD_MAX = 72;
const TOKEN_TTL_MS = 90 * 24 * 60 * 60 * 1000;
const MAX_BODY_BYTES = 16 * 1024;

// Not a moderation system — just keeps the worst words out of a kid-facing
// name field. Substring match on the lowercased name.
const NAME_BLOCKLIST = ["fuck", "shit", "cunt", "nigg", "bitch", "penis", "porn"];

const SCRYPT_N = 16384, SCRYPT_R = 8, SCRYPT_P = 1, KEY_LEN = 32;

let store = null;

async function init() {
  store = await createStore();
  // Leaderboards share the table and the session lookup.
  await leaderboard.init({ store, resolveToken });
}

// ---------------------------------------------------------------- passwords

async function hashPassword(password) {
  const salt = crypto.randomBytes(16);
  const key = await scrypt(password, salt, KEY_LEN, {
    N: SCRYPT_N, r: SCRYPT_R, p: SCRYPT_P,
  });
  // Self-describing so the cost can be raised later without a migration.
  return `s1$${SCRYPT_N}$${salt.toString("hex")}$${key.toString("hex")}`;
}

async function verifyPassword(password, stored) {
  const parts = String(stored || "").split("$");
  if (parts.length !== 4 || parts[0] !== "s1") return false;
  const n = parseInt(parts[1], 10);
  const salt = Buffer.from(parts[2], "hex");
  const expected = Buffer.from(parts[3], "hex");
  const key = await scrypt(password, salt, expected.length, {
    N: n, r: SCRYPT_R, p: SCRYPT_P,
    // Above-default N would otherwise throw instead of verifying.
    maxmem: 128 * n * SCRYPT_R * 2,
  });
  return crypto.timingSafeEqual(key, expected);
}

// ------------------------------------------------------------------ tokens

function hashToken(token) {
  return crypto.createHash("sha256").update(token).digest("hex");
}

/** Row key for the unique-email index (see the layout note up top). */
function emailKey(emailLower) {
  return crypto.createHash("sha256").update(emailLower).digest("hex");
}

async function mintToken(userId) {
  const token = crypto.randomBytes(32).toString("base64url");
  await store.insert("token", hashToken(token), {
    userId,
    expiresAt: Date.now() + TOKEN_TTL_MS,
  });
  return token;
}

/** The user row for a live token, or null. */
async function resolveToken(token) {
  if (!token) return null;
  const key = hashToken(token);
  const session = await store.get("token", key);
  if (!session) return null;
  if (session.expiresAt < Date.now()) {
    await store.remove("token", key);
    return null;
  }
  const user = await store.get("user", session.userId);
  if (!user) return null;
  return { userId: session.userId, user };
}

// -------------------------------------------------------------- rate limits

/** bucket -> ip -> {count, resetAt}. In-memory, like the WS per-IP cap. */
const rateBuckets = new Map();

function rateLimited(bucket, ip, max, windowMs) {
  let perIp = rateBuckets.get(bucket);
  if (!perIp) rateBuckets.set(bucket, (perIp = new Map()));
  const now = Date.now();
  let entry = perIp.get(ip);
  if (!entry || now >= entry.resetAt) {
    entry = { count: 0, resetAt: now + windowMs };
    perIp.set(ip, entry);
    // Opportunistic sweep so dead IPs don't accumulate forever.
    if (perIp.size > 10000)
      for (const [k, v] of perIp) if (now >= v.resetAt) perIp.delete(k);
  }
  entry.count++;
  return entry.count > max;
}

// ------------------------------------------------------------------ helpers

function clientIp(req) {
  const forwarded = req.headers["x-forwarded-for"];
  if (forwarded) return String(forwarded).split(",")[0].trim();
  return req.socket.remoteAddress || "unknown";
}

function bearerToken(req) {
  const header = String(req.headers["authorization"] || "");
  return header.startsWith("Bearer ") ? header.slice(7).trim() : "";
}

function writeJson(res, status, body) {
  const raw = JSON.stringify(body);
  res.writeHead(status, {
    "Content-Type": "application/json",
    // Header-token auth, no cookies: open CORS is safe and keeps every
    // current and future game origin working without a server change.
    "Access-Control-Allow-Origin": "*",
    "Cache-Control": "no-store",
  });
  res.end(raw);
}

function fail(res, status, error, message) {
  writeJson(res, status, { ok: false, error, message });
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on("data", (chunk) => {
      size += chunk.length;
      if (size > MAX_BODY_BYTES) {
        reject(new Error("body-too-large"));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

async function readJsonBody(req) {
  const raw = await readBody(req);
  try {
    const body = JSON.parse(raw || "{}");
    return body && typeof body === "object" && !Array.isArray(body) ? body : null;
  } catch {
    return null;
  }
}

/** {ok:true, token?, userId, username, email} — flat for JsonUtility. */
function profileReply(userId, user, token) {
  const reply = {
    ok: true,
    userId,
    username: user.username,
    email: user.email || "",
  };
  if (token) reply.token = token;
  return reply;
}

// ------------------------------------------------------------------- routes

async function handleRegister(req, res, ip) {
  if (rateLimited("register", ip, 10, 60 * 60 * 1000))
    return fail(res, 429, "rate-limited", "too many new accounts, try again later");

  const body = await readJsonBody(req);
  if (!body) return fail(res, 400, "bad-json", "malformed request");

  const username = String(body.username || "").trim();
  const password = String(body.password || "");
  const email = String(body.email || "").trim();

  if (!USERNAME_RE.test(username))
    return fail(res, 400, "bad-username",
      "names are 3-16 letters, numbers or _");
  const usernameLower = username.toLowerCase();
  if (NAME_BLOCKLIST.some((word) => usernameLower.includes(word)))
    return fail(res, 400, "bad-username", "pick a friendlier name");
  if (password.length < PASSWORD_MIN)
    return fail(res, 400, "bad-password",
      `passwords need at least ${PASSWORD_MIN} characters`);
  if (password.length > PASSWORD_MAX)
    return fail(res, 400, "bad-password", "that password is too long");
  const emailLower = email.toLowerCase();
  if (email && (email.length > 254 || !EMAIL_RE.test(email)))
    return fail(res, 400, "bad-email", "that email doesn't look right");

  const userId = crypto.randomUUID();

  // The name index is the uniqueness gate: whoever inserts it owns the name.
  if (!(await store.insert("name", usernameLower, { userId })))
    return fail(res, 409, "name-taken", "that name is taken, try another");

  if (email && !(await store.insert("email", emailKey(emailLower), { userId }))) {
    await store.remove("name", usernameLower); // roll back the claimed name
    return fail(res, 409, "email-taken", "that email already has an account");
  }

  await store.merge("user", userId, {
    username,
    usernameLower,
    email,
    emailLower: email ? emailLower : "",
    passHash: await hashPassword(password),
    createdAt: Date.now(),
  });

  const token = await mintToken(userId);
  console.log(`accounts: registered ${userId} (${usernameLower})`);
  writeJson(res, 200, profileReply(userId, { username, email }, token));
}

async function handleLogin(req, res, ip) {
  if (rateLimited("login", ip, 15, 5 * 60 * 1000))
    return fail(res, 429, "rate-limited", "too many tries, wait a few minutes");

  const body = await readJsonBody(req);
  if (!body) return fail(res, 400, "bad-json", "malformed request");

  const id = String(body.id || "").trim().toLowerCase();
  const password = String(body.password || "");
  if (!id || !password)
    return fail(res, 400, "bad-login", "type your name (or email) and password");

  // Usernames can't contain "@", so the two lookups can never collide.
  const index = id.includes("@")
    ? await store.get("email", emailKey(id))
    : await store.get("name", id);
  const user = index ? await store.get("user", index.userId) : null;
  // One generic answer for every miss — a login form must not reveal which
  // names exist.
  if (!user || !(await verifyPassword(password, user.passHash)))
    return fail(res, 401, "bad-login", "wrong name or password");

  await store.merge("user", index.userId, { lastLoginAt: Date.now() });
  const token = await mintToken(index.userId);
  console.log(`accounts: login ${index.userId}`);
  writeJson(res, 200, profileReply(index.userId, user, token));
}

async function handleLogout(req, res) {
  const token = bearerToken(req);
  if (token) await store.remove("token", hashToken(token));
  writeJson(res, 200, { ok: true });
}

async function handleMe(req, res) {
  const session = await resolveToken(bearerToken(req));
  if (!session) return fail(res, 401, "bad-token", "signed out");
  writeJson(res, 200, profileReply(session.userId, session.user));
}

/**
 * Route an /api/* request. The server's request handler calls this for any
 * URL under /api/ and this function always answers, one way or another.
 */
async function handle(req, res) {
  if (req.method === "OPTIONS") {
    res.writeHead(204, {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization",
      "Access-Control-Max-Age": "86400",
    });
    res.end();
    return;
  }

  if (!store)
    return fail(res, 503, "starting", "the server is still waking up");

  const ip = clientIp(req);
  if (rateLimited("api", ip, 120, 60 * 1000))
    return fail(res, 429, "rate-limited", "slow down a little");

  const url = req.url.split("?")[0];
  try {
    if (await leaderboard.handle(req, res, url,
        { bearerToken, readJsonBody, writeJson, fail }))
      return;
    if (url === "/api/register" && req.method === "POST")
      return await handleRegister(req, res, ip);
    if (url === "/api/login" && req.method === "POST")
      return await handleLogin(req, res, ip);
    if (url === "/api/logout" && req.method === "POST")
      return await handleLogout(req, res);
    if (url === "/api/me" && req.method === "GET")
      return await handleMe(req, res);
    return fail(res, 404, "not-found", "no such endpoint");
  } catch (err) {
    // Never a stack trace to the client; never a password in the log.
    console.error(`accounts: ${url} failed:`, err.message);
    if (!res.headersSent)
      return fail(res, 500, "server-error", "something went wrong, try again");
    res.end();
  }
}

module.exports = { init, handle };
