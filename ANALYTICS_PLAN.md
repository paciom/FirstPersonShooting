# ANALYTICS_PLAN.md — Player & Site Analytics

*Written 2026-08-03. External facts re-verified that day. Companion docs: `PLAN.md` (game roadmap), `WEB_PLAN.md` (marketing site).*

## TL;DR

"Something like Google Analytics" — except Google Analytics itself is off the table for a
child-directed game, and so is every other third-party analytics SDK, by our own verified
COPPA research (2026-07-29): relying on the *support-for-internal-operations* exception is
what lets us run with **no parental consent and no consent banners**, and putting a
third-party SDK in the client voids it. So we build the small first-party version on the
Azure stack we already run:

```
Unity client (WebGL / iOS / Android)          www.jah.cc (site beacon, ~15 lines JS)
        \                                        /
         POST batched anonymous events (text/plain JSON)
                          |
            Azure Function  "jah-metrics-fn"   (validate, allowlist, forward)
                          |
            Application Insights "jah-metrics" (customEvents + built-in
                          |                     Funnels / Retention / Cohorts)
            Workbook dashboard: DAU, D1/D7 retention, mode & robot
            popularity, session length, load time, errors
```

**$0/month** at our scale (5 GB/month ingestion free per billing account, Functions free
grant), one pipeline for the game on every platform *and* the marketing site, and a clean
story for Apple's Kids Category and Google Play Families when mobile ships.

**Status 2026-08-04: LIVE end to end.** Phases 1–5 done — client library
(`Assets/Scripts/Metrics/Metrics.cs` + `Assets/Plugins/WebGL/MetricsBeacon.jslib`),
Azure stack provisioned (`Tools/provision_metrics.ps1`), ingest Function deployed
(`Tools/metrics_fn/`, `Tools/deploy_metrics_fn.ps1`) at
`https://jah-metrics-fn.azurewebsites.net/api/e`, all 15 modes instrumented via the
`GameModeController.Mode` setter seam, account uid join wired off `AccountClient.Changed`,
site beacon + `?src=site` + `privacy.html` deployed to www.jah.cc, and the "JAH Metrics"
workbook published (`Tools/create_metrics_workbook.ps1`). Phase 6 (mobile checklist)
remains for when iOS/Android builds exist. Ops notes: POST with header `x-jah-debug: 1`
to get the Function's gate trace instead of the blind 204; the client accepts single-event
loss on cold starts by design (batching + reflush covers real sessions).

## Why not the obvious products (verified 2026-08-03)

| Option | Verdict | Reason |
|---|---|---|
| Google Analytics 4 (web) | ❌ | Cookie-based → consent UI; no child-directed mode for web; a third-party collecting on a kids' service voids our SFIO posture |
| Firebase Analytics | ❌ | Unity SDK still has **no WebGL support** (re-verified in the 2026-07-29 backend research) — fails half the requirement before compliance even comes up |
| Unity Analytics (UGS) | ❌ | UGS free-tier enforcement is a **lockout of all UGS services** until payment details are entered (verbatim in Unity docs); Multiplay shutdown Apr 2026 is the churn precedent |
| GameAnalytics | ❌ (fallback) | Best of the third-party bunch (free, Unity+WebGL, kids-app config) but still a third-party SDK → voids SFIO, adds Apple 1.3 "limited cases" review burden; owned by Mobvista (adtech). Documented as the bail-out if we ever abandon first-party |
| PostHog / Plausible / Umami | ❌ | Web-first; no Unity story; self-hosting costs more than our entire pipeline |
| **First-party on Azure** | ✅ | No third party at all → strongest COPPA/Kids-Category position; rides existing `photon-arena-rg`; App Insights ships funnels/retention/cohorts out of the box |

## Compliance constraints that shaped the design (amended COPPA rule, deadline passed 2026-04-22)

1. **No consent needed** when the only PI collected is a persistent identifier used solely
   for support for internal operations (§312.5(c)(7)) — analytics/"analyze the functioning"
   qualifies. Never use the id to contact, profile, or advertise.
2. **The privacy page must NAME the internal operations** the identifier serves and how we
   keep it limited to them (new requirement in the amended rule → Phase 5 has template text).
3. **Written data-retention policy is legally required**; indefinite retention prohibited.
   → workspace retention set to **90 days**, and the privacy page says so.
4. **Apple Kids Category (guideline 1.3)**: third-party analytics only in "limited cases"
   (no IDFA, no device info). First-party to our own server sidesteps the clause entirely.
   No tracking → no ATT prompt.
5. **Google Play Families**: never transmit AAID/IMEI/etc., no `AD_ID` permission (API 33+),
   disclose collection in the Data safety form. Our random UUID satisfies all three.
6. **House rules**: props are enums/numbers from a fixed vocabulary — never free text (the
   typed squad-config box must never be logged raw); IPs stay masked (App Insights zeroes
   client IP at rest by default — keep it); never join events to infra logs.

## What gets measured (event taxonomy v1)

| Event | Props | Answers |
|---|---|---|
| `session_start` (auto) | platform, app_ver, unity, src | DAU/WAU/retention; site→game conversion via `?src=site` |
| `session_end` (auto, best-effort) | — | session length (also derived server-side from last-event time) |
| `boot_perf` | load_ms | is the 62 MB WebGL build loading fast enough |
| `menu_view` | — | funnel top |
| `mode_select` | mode | which of the 8 modes gets clicked |
| `match_start` | mode, variant, robot, difficulty | mode + roster popularity (9 robots) |
| `match_end` | mode, result, duration_s, score_bucket | completion rate, match length per mode |
| `match_quit` | mode, at_s | rage-quit points |
| `robot_select` | robot | roster popularity before matches even start |
| `error` (auto) | msg[120], where | top exceptions in the wild (deduped, ≤10/session) |

Rules: tag AI-vs-AI spectator/recording sessions (`variant:"aivai"`) so YouTube-pipeline
runs don't pollute player metrics; `env:"dev"` for editor/debug builds (dashboards filter
to `env=="prod"`); Chinese Quest learning events are a Phase-6+ taxonomy of their own.

## Architecture decisions

- **Ingest Function, not direct-to-App-Insights.** The track endpoint would work SDK-less,
  but the Function buys: server-side event allowlist (junk from a public endpoint dies
  there), key rotation without shipping a build, server-authoritative timestamps, and a
  seam to swap/add storage (Cosmos) or fold into the future Container Apps API server
  (`AccountClient`/`NetSession` exist but have no live host yet — when that server lands,
  ingest can move into it and the client only changes one URL).
- **IDs**: `iid` = random GUID in PlayerPrefs (IndexedDB on WebGL — requires explicit
  `PlayerPrefs.Save()`, already handled), `sid` = per-launch GUID. The Function maps
  iid→`user_Id`, sid→`session_Id` on the App Insights envelope — that mapping is what makes
  the built-in Funnels/Retention/Cohorts blades light up.
- **Transport is CORS-simple `text/plain`** (a typed JSON Blob turns `sendBeacon` into a
  preflighted request, which beacons can't do — it fails silently in Chromium). Final flush
  on WebGL goes through `navigator.sendBeacon` via `.jslib`; `UnityWebRequest` dies with
  the page.
- **Failure = accepted loss.** No retry storms, queue capped at 500 events, nothing in the
  metrics path may throw into gameplay.
- **One App Insights resource for everything** — game and site share the 5 GB pool,
  separated by an `app` dimension (`jah` / `site`).

## Cost (verified 2026-08-03)

| Piece | Free allowance | Our worst case |
|---|---|---|
| App Insights ingestion | 5 GB/month per billing account, then ~$2.30/GB | ~1 KB/event → ~5M events/mo free; hobby scale is <100k |
| Retention | 31 days free, ~$0.10/GB/mo after | 90-day setting ≈ pennies |
| Function (consumption) | 1M executions/mo | batching ≈ 1 exec per ~25 events |
| Circuit breaker | **daily cap 0.2 GB/day on the workspace** | guarantees $0 even if a bug event-storms |

## Phases

1. ✅ **Client library** (2026-08-03): `Metrics.Track("match_start", ("mode","dogfight"))`
   facade — batching, ids, lifecycle flushes, error hook, editor logging. Inert: `Endpoint`
   const is empty.
2. **Provision Azure** (`Tools/provision_metrics.ps1`, az CLI, into `photon-arena-rg`):
   Log Analytics workspace + App Insights `jah-metrics` (90-day retention, 0.2 GB daily cap)
   + storage + Function app `jah-metrics-fn` (Python or PowerShell, ~100 lines: size caps,
   `^[a-z_]{3,32}$` name allowlist, ≤50 events/envelope, forward to track API, always 204).
   CORS: `https://jah.cc`, `https://www.jah.cc`, `https://play.jah.cc`, `http://localhost:*`
   for local WebGL tests. Set `Metrics.Endpoint`, verify events land, then flip `Env` to
   prod for release builds. Optional later: `metrics.jah.cc` grey-cloud CNAME.
3. **Instrument the game**: session/menu events in `GameModeController` + `MainMenu`,
   match events at each mode's start/end/teardown (`ArenaRuntime` teardown rule), robot
   select in `RobotSelectMenu`, boot_perf in the WebGL boot path. Editor play-through shows
   `[Metrics]` log lines as the manual test.
4. **Site beacon**: ~15 lines inline in `Web/site.js` — `page_view` + `play_click`, no
   cookie/localStorage (below the PI threshold entirely); Play link gains `?src=site`.
5. **Dashboards + privacy page**: one Workbook (DAU, D1/D7 retention grid, mode/robot
   share, funnel session→mode→match→end, p50 load_ms, top errors); `jah.cc/privacy`
   paragraph naming the internal operations (draft below) + 90-day retention statement.
6. **Mobile later** (`applicationIdentifier` still empty — when iOS/Android builds exist):
   same client code, zero SDKs. Checklist: remove `com.unity.modules.unityanalytics` from
   `Packages/manifest.json` (present today, legacy module, belt-and-braces for a kids'
   title); confirm no engine HW-stats/telemetry flags; App Store privacy label = "Data Not
   Linked to You: Product Interaction, Diagnostics"; Play Data safety form equivalents;
   Kids Category declaration; no ATT prompt anywhere.

**Privacy-page draft (Phase 5):**
> Jet Armor Heroes stores a randomly generated player code on your device and
> receives anonymous play events (which game mode was played, how long a match lasted,
> whether an error happened). We use this only to run and improve the game — seeing which
> modes are fun, finding bugs, and keeping the service secure. The code can't be used to
> contact anyone, is never combined with names, emails, or other personal information,
> is never shared or used for advertising, and every event is automatically deleted
> after 90 days.

## Admin dashboard (www.jah.cc/admin, added 2026-08-04)

- **Page**: `Web/admin/index.html` — tiles + hand-rolled SVG/CSS charts (DAU, site
  views/clicks, mode & robot popularity, funnel, match length, boot p50/p90, top
  errors), Live/Dev toggle. Series colors are darkened brand steps that pass the
  dataviz six-checks on the panel surface. All interpolated strings escaped —
  error messages arrive from a public endpoint, so an unescaped admin page would
  be stored XSS.
- **API**: `WebApi/metrics` (SWA *managed* function, free tier) — named-KQL map
  only, client never sends KQL; queries App Insights REST API with a read-only
  API key held in SWA app settings (`Tools/setup_admin_api.ps1` creates/rotates,
  never prints it). Defense in depth: SWA route rule *and* an in-function
  `x-ms-client-principal` role check.
- **Auth**: SWA built-in auth, `staticwebapp.config.json` — `/admin*` and
  `/api/*` require role `admin`; anonymous hits 302 to the Microsoft (AAD) login;
  GitHub/Twitter login routes are 404'd. Free tier = built-in providers only
  (Google sign-in would need Standard, ~$9/mo). Grant access with
  `Tools/invite_admin.ps1 -Email <who>` (invitation link IS the grant, max 7-day
  expiry, ≤25 role users on free tier). Invitees sign in with a Microsoft
  account on that email. `/admin` + `/api` are robots-disallowed and noindexed.

## Gotchas already accounted for

- `session_end` is unreliable everywhere (tab close, app kill) — dashboards derive session
  length from last-event timestamps; the event is a bonus.
- Editor/dev noise: `env:"dev"` via `Debug.isDebugBuild`, filtered in every query.
- Clock skew: client `t` is advisory; the Function stamps receive time authoritatively.
- `OnApplicationPause(true)` flushes on mobile (backgrounded apps often never return).
- play.jah.cc is Cloudflare-proxied — CORS origin is the public name, and the beacon body
  stays `text/plain` end to end.
- App Insights sampling: leave off (volume is nowhere near needing it).

## Sources (2026-08-03)

- COPPA amended rule, SFIO notice + retention requirements: [Fenwick](https://www.fenwick.com/insights/publications/coppas-coming-of-age-key-compliance-changes-in-ftcs-final-rule), [NatLawReview](https://natlawreview.com/article/ftc-publishes-final-coppa-rule-amendments), [Hintze Law](https://hintzelaw.com/blog/2025/2/6/final-coppa-rule-amendments-definitional-changes)
- Azure Monitor pricing / 5 GB free / caps: [Azure pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/), [App Insights FAQ](https://learn.microsoft.com/en-us/azure/azure-monitor/app/application-insights-faq)
- Apple Kids Category analytics clause: [App Review Guidelines §1.3](https://developer.apple.com/app-store/review/guidelines/)
- Play Families identifier/SDK rules: [Families Policies](https://support.google.com/googleplay/android-developer/answer/9893335), [Self-certified ads SDK program](https://support.google.com/googleplay/android-developer/answer/9900633)
- In-repo: 2026-07-29 backend/COPPA research (Cosmos, UGS lockout, Firebase-WebGL, SFIO account design) — see memory `photon-arena-backend-data`.
