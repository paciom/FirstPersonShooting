// Photon Arena leaderboards: one board per (mode, arena), plus an "all
// arenas" board per mode, each holding every signed-in player's PERSONAL
// BEST. Scores come from single-player modes the server never sees, so
// integrity is plausibility, not proof: a per-mode cap, a per-account rate
// limit, and the fact that only one row per player exists (a cheater can
// hold one spot, not fill the board).
//
// HTTP surface (JSON, CORS open, bearer auth like accounts.js):
//   POST /api/score        Authorization: Bearer <token>
//        {mode, arena, score}         -> {ok, improved, best, rank}
//   GET  /api/leaderboard?mode=..&arena=..&top=10   [Authorization optional]
//                                     -> {ok, mode, arena, higherIsBetter,
//                                         rows:[{rank, username, score, at}],
//                                         myRank, myScore}
//   GET  /api/boards                   -> {ok, modes:[{mode, higherIsBetter,
//                                         unit, max, cumulative}]}
//
// arena "" (or "*") is the mode's overall board. Every post lands on both
// the arena board and the overall board.
//
// Store layout (see store.js) -- the sort order lives IN the row key because
// Table Storage returns a partition in row-key order and has no ORDER BY:
//   ("board:<mode>|<arena>", "<sortKey>_<at>_<userId>")  -> {userId, username, score, at}
//   ("best", "<mode>|<arena>|<userId>")                  -> {score, rowKey}
// sortKey is the score zero-padded to 10 digits, inverted (MAX - score) for
// higher-is-better boards so the top of the partition is the top of the board.
// The timestamp tie-break means whoever got there first stays ahead.

const { createStore } = require("./store");

const SCORE_DIGITS = 10;
const SCORE_MAX = 10 ** SCORE_DIGITS - 1;
const RANK_SCAN = 200;        // how deep we look for "your rank"
const TOP_MAX = 50;
const ARENA_MAX = 32;

/**
 * Every board the client may post to. `max` is the plausibility cap; a
 * score above it is refused, not clamped, so a hacked client gains nothing
 * by lying big. `cumulative` boards ADD each post to the player's row
 * (career wins) instead of keeping the best single match, so their cap is
 * per post. What each number means is decided client-side, in
 * LeaderboardClient's mode table -- keep the two in step.
 */
const MODES = {
  gunfight:     { max: 20000,   unit: "points" },
  brawl:        { max: 1,       unit: "wins", cumulative: true },
  commander:    { max: 20000,   unit: "points" },
  towerdefense: { max: 2000,    unit: "points" },
  tankraid:     { max: 1000000, unit: "metres" },
  dogfight:     { max: 5000,    unit: "points" },
  chinesequest: { max: 100000,  unit: "points" },
  chineserun:   { max: 1000000, unit: "metres" },
  adventure:    { max: 100,     unit: "endings" },
};
for (const m of Object.values(MODES)) if (m.higherIsBetter === undefined) m.higherIsBetter = true;

let store = null;
let resolveToken = null;

/** @param deps.resolveToken accounts.js's token -> {userId, user} lookup */
async function init(deps) {
  resolveToken = deps.resolveToken;
  store = deps.store || (await createStore());
}

// ------------------------------------------------------------------ keys

function cleanMode(raw) {
  const mode = String(raw || "").trim().toLowerCase();
  return Object.prototype.hasOwnProperty.call(MODES, mode) ? mode : null;
}

/**
 * Arena names are display names from the client ("Neon Canyon"); the key is
 * a slug that Table Storage row keys accept. Unknown arenas are fine -- the
 * client owns the list, adding one needs no server change.
 */
function cleanArena(raw) {
  const slug = String(raw || "")
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, ARENA_MAX);
  return slug === "" || slug === "*" ? "*" : slug;
}

function boardKey(mode, arena) {
  return `board:${mode}|${arena}`;
}

function sortKey(mode, score) {
  const n = MODES[mode].higherIsBetter ? SCORE_MAX - score : score;
  return String(n).padStart(SCORE_DIGITS, "0");
}

function better(mode, a, b) {
  return MODES[mode].higherIsBetter ? a > b : a < b;
}

// -------------------------------------------------------------- rate limit

/** userId -> {count, resetAt}. Kids finish a match every minute or two; a
 *  script posts far faster than that. */
const postBuckets = new Map();
const POST_MAX = 30;
const POST_WINDOW_MS = 10 * 60 * 1000;

function postLimited(userId) {
  const now = Date.now();
  let entry = postBuckets.get(userId);
  if (!entry || now >= entry.resetAt) {
    entry = { count: 0, resetAt: now + POST_WINDOW_MS };
    postBuckets.set(userId, entry);
    if (postBuckets.size > 10000)
      for (const [k, v] of postBuckets) if (now >= v.resetAt) postBuckets.delete(k);
  }
  entry.count++;
  return entry.count > POST_MAX;
}

// ------------------------------------------------------------------ core

/**
 * Record a score on one board. Only a personal best changes anything: the
 * old ranked row is removed and the new one inserted, so each player owns
 * exactly one row per board.
 */
async function postToBoard(mode, arena, userId, username, score, at) {
  const bestKey = `${mode}|${arena}|${userId}`;
  const best = await store.get("best", bestKey);
  if (MODES[mode].cumulative) {
    if (score <= 0) return { improved: false, best: best ? best.score : 0 };
    score += best ? best.score : 0;
  } else if (best && !better(mode, score, best.score)) {
    return { improved: false, best: best.score };
  }
  if (score > SCORE_MAX) score = SCORE_MAX;

  const rowKey = `${sortKey(mode, score)}_${String(at).padStart(13, "0")}_${userId}`;
  const pk = boardKey(mode, arena);
  await store.merge(pk, rowKey, { userId, username, score, at });
  if (best && best.rowKey) await store.remove(pk, best.rowKey);
  await store.merge("best", bestKey, { score, rowKey, username, at });
  return { improved: true, best: score };
}

/** Top rows of a board, ranked, plus where `userId` sits (0 = not in the
 *  first RANK_SCAN rows). */
async function readBoard(mode, arena, top, userId) {
  const rows = await store.list(boardKey(mode, arena), Math.max(top, userId ? RANK_SCAN : top));
  let myRank = 0;
  let myScore = null;
  if (userId) {
    const idx = rows.findIndex((r) => r.userId === userId);
    if (idx >= 0) {
      myRank = idx + 1;
      myScore = rows[idx].score;
    } else {
      const best = await store.get("best", `${mode}|${arena}|${userId}`);
      if (best) myScore = best.score;
    }
  }
  return {
    rows: rows.slice(0, top).map((r, i) => ({
      rank: i + 1,
      username: r.username || "?",
      score: r.score,
      at: r.at || 0,
      me: !!userId && r.userId === userId,
    })),
    myRank,
    myScore,
  };
}

// ---------------------------------------------------------------- routes

async function handleScore(req, res, helpers) {
  const session = await resolveToken(helpers.bearerToken(req));
  if (!session) return helpers.fail(res, 401, "bad-token", "sign in to post scores");
  if (postLimited(session.userId))
    return helpers.fail(res, 429, "rate-limited", "that is a lot of scores, take a breather");

  const body = await helpers.readJsonBody(req);
  if (!body) return helpers.fail(res, 400, "bad-json", "malformed request");

  const mode = cleanMode(body.mode);
  if (!mode) return helpers.fail(res, 400, "bad-mode", "unknown game mode");
  const arena = cleanArena(body.arena);
  const score = Number(body.score);
  if (!Number.isInteger(score) || score < 0 || score > MODES[mode].max)
    return helpers.fail(res, 400, "bad-score", "that score does not look right");
  // A time of zero is "did not play", not a record.
  if (!MODES[mode].higherIsBetter && score === 0)
    return helpers.fail(res, 400, "bad-score", "that score does not look right");

  const at = Date.now();
  const username = session.user.username;
  const onArena = arena === "*"
    ? null
    : await postToBoard(mode, arena, session.userId, username, score, at);
  const overall = await postToBoard(mode, "*", session.userId, username, score, at);
  const main = onArena || overall;

  // Rank on the board the player was actually playing.
  const view = await readBoard(mode, arena, 1, session.userId);
  console.log(`leaderboard: ${session.userId} ${mode}/${arena} ${score}` +
    (main.improved ? " (new best)" : ""));
  helpers.writeJson(res, 200, {
    ok: true,
    improved: main.improved,
    best: main.best,
    rank: view.myRank,
    overallImproved: overall.improved,
    overallBest: overall.best,
  });
}

async function handleBoard(req, res, helpers) {
  const url = new URL(req.url, "http://x");
  const mode = cleanMode(url.searchParams.get("mode"));
  if (!mode) return helpers.fail(res, 400, "bad-mode", "unknown game mode");
  const arena = cleanArena(url.searchParams.get("arena"));
  const top = Math.min(TOP_MAX, Math.max(1, Number.parseInt(url.searchParams.get("top"), 10) || 10));
  const token = helpers.bearerToken(req);
  const session = token ? await resolveToken(token) : null;

  const view = await readBoard(mode, arena, top, session ? session.userId : null);
  helpers.writeJson(res, 200, {
    ok: true,
    mode,
    arena,
    higherIsBetter: MODES[mode].higherIsBetter,
    unit: MODES[mode].unit,
    cumulative: !!MODES[mode].cumulative,
    rows: view.rows,
    myRank: view.myRank,
    myScore: view.myScore === null ? -1 : view.myScore,
    myName: session ? session.user.username : "",
  });
}

function handleBoards(req, res, helpers) {
  helpers.writeJson(res, 200, {
    ok: true,
    modes: Object.entries(MODES).map(([mode, m]) => ({
      mode, higherIsBetter: m.higherIsBetter, unit: m.unit, max: m.max,
      cumulative: !!m.cumulative,
    })),
  });
}

/**
 * Route one /api/* request if it belongs to the leaderboard. Returns false
 * when the URL is not ours so accounts.handle can keep going.
 */
async function handle(req, res, url, helpers) {
  if (url !== "/api/score" && url !== "/api/leaderboard" && url !== "/api/boards")
    return false;
  if (!store) {
    helpers.fail(res, 503, "starting", "the server is still waking up");
    return true;
  }
  if (url === "/api/score" && req.method === "POST") await handleScore(req, res, helpers);
  else if (url === "/api/leaderboard" && req.method === "GET") await handleBoard(req, res, helpers);
  else if (url === "/api/boards" && req.method === "GET") handleBoards(req, res, helpers);
  else helpers.fail(res, 405, "bad-method", "wrong method for that endpoint");
  return true;
}

module.exports = { init, handle, MODES, cleanArena };
