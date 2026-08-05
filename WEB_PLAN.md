# WEB_PLAN — the Jet Armor Heroes home page at www.jah.cc

## Should it be a separate site? Yes.

The Unity WebGL player is not a web page you can dress up — it is a 257 MB
artifact Unity **regenerates wholesale** on every build. Three consequences,
each on its own enough to settle the question:

1. **Anything hand-written inside `Build/WebGL/index.html` is destroyed by the
   next build.** A marketing page cannot live in a directory the build owns.
2. **The player costs 240 MB before it shows a pixel.** A home page has to be
   readable in under a second, on a phone, from a link someone shared. Those two
   payloads must not share a URL.
3. **They do not even fit the same host.** Azure Static Web Apps' free tier caps
   an app at 250 MB — the marketing site fits a hundred times over, the game
   does not fit at all. The site wants managed TLS and a custom domain; the game
   wants a fat blob container.

So: two deployments, one brand.

| URL | What | Size | Where |
| --- | --- | --- | --- |
| `www.jah.cc` (+ `jah.cc` → www) | Marketing home page | ~4 MB | Azure Static Web Apps, free tier, managed TLS |
| `play.jah.cc` | The WebGL player | 257 MB | Existing `photonarenaweb` storage static website, fronted for TLS |

`play.jah.cc` needs a TLS front end because an Azure Storage static website
cannot serve HTTPS on a custom domain by itself (this is already recorded in
project memory). Two options, decided at deploy time, not now:
- **Cloudflare (free)** in front of `photonarenaweb.z13.web.core.windows.net`,
  with an Origin Rule rewriting the Host header — otherwise Azure 404s. Costs:
  the whole `jah.cc` zone moves to Cloudflare nameservers.
- **Azure Front Door** — keeps DNS at the registrar, but the standard tier is
  ~$35/mo. Only worth it if the game ever earns money.
Cloudflare is what shipped. The site's PLAY button points at
`https://play.jah.cc/` — one constant in one file (`Web/site.js`, `PLAY_URL`).

## Where it lives

```
Web/                      the site — plain HTML/CSS/JS, no build step, no CDN
  index.html
  styles.css
  site.js
  assets/                 generated, not hand-made (see below)
  robots.txt  sitemap.xml  404.html
Tools/build_site_assets.py   game art  ->  Web/assets  (webp + mp4 + posters)
Tools/deploy_site.ps1        Web/  ->  Azure Static Web Apps
```

No framework and no CDN, on purpose. Every asset is self-hosted, the page is
three files, and it matches how the rest of this repo is built: scripts that
regenerate their outputs from the game's own data rather than hand-tended blobs.

## The art already exists

Nothing here needs to be drawn or bought. The menu art pipeline
(`Tools/menuart.py` → plates, Unity `MenuArtForge` → robot renders,
`Tools/menucomposite.py` → the marriage of the two) has already produced
everything a home page needs:

- `Assets/Resources/Menu/keyart.png` — 1920×1080 hero: five heroes standing in
  the light beams over a city at sunset. This is the fold.
- `Assets/Resources/Menu/<mode>.png` ×15 — 1280×720 cards, each showing **real
  game robots in real poses** inside a painted room. These are the mode grid.
  STORY is the sixteenth mode and has no plate — it is staged, not illustrated —
  so its card is a frame lifted out of `Renders/pilot.mp4`, subtitle and all.
- `Assets/Resources/Menu/emblem.png` — 1024² RGBA crest. Logo mark and favicon.
- `PreviewCaptures/<HERO>_front.png` ×9 — 560² turntable renders on a flat
  (5,13,25) background, which un-mixes to true alpha exactly (colour minus
  background, divided by coverage). These are the hero roster cutouts.
- `Assets/Video/<hero>-transform.mp4` and `<hero>-jet-transform.mp4` ×9 each —
  5 s, robot ↔ vehicle and robot ↔ jet. Both play in the roster, on the two
  pills each card carries. Every one of these clips is stamped with a generator
  watermark in the top-left corner, which `build_site_assets.py` paints out with
  `delogo` — the game shows them small and moving, a home page shows them large
  and looped.
- `Assets/Models/Stages/<hero>-jet/stage8.glb` ×9 — the jet itself, the last
  frame of the transformation and the aircraft DOGFIGHT flies.
  `Tools/jetstills.py` renders it to a cutout with `previewglb`'s software
  rasteriser (no Unity, ~1 s each) and lays three of them out as a sky layer
  that BOTH the web hero and the game's title screen fly.

`build_site_assets.py` converts, keys, resizes and re-encodes all of it into
`Web/assets`, so a re-render of the game art is one command away from being live
art. Target budget: **≤ 4 MB for the first screen**, everything below the fold
lazy-loaded.

## The page, top to bottom

Classic AAA beats, in the order those sites use them:

1. **Sticky nav** — emblem, MODES / HEROES / TRANSFORM / PARENTS, and a PLAY
   FREE button that stays on screen the whole way down.
2. **Hero** — three layers deep: painted plate, jets, cast. JET ARMOR HEROES
   lockup over the game's own tagline "ROBOT · VEHICLE · JET · ONE BATTLE
   LEAGUE", two CTAs (PLAY FREE IN BROWSER / SEE THE 16 MODES), and the honesty
   line that converts: *no download, no account, plays in any browser*.
3. **Numbers strip** — 16 battle modes · 9 heroes · 9 jets · 0 downloads.
4. **The pitch** — three columns: transform mid-fight, the airdrop scramble, and
   nobody ever dies (shields de-rez and re-materialize) — the last is the line
   that sells a parent, and it is true of the fiction, not a euphemism.
5. **Modes** — all 16 cards in the game's own three decks: ARENA COMBAT (AI v AI,
   Player v AI, Online PvP, Brawl, Brawl: AI War, Martial Arts Show, Story),
   WAR ROOM (Commander, Commander: AI War, Tower Defense, Tank Raid, Dogfight,
   Dogfight: AI War), ACADEMY · WORKSHOP (Chinese Quest, Chinese Run, Arena
   Builder). Each card: art, name, one line.
6. **Heroes** — the nine cutouts in a row, each with name, class and a one-line
   personality, and two pills: VEHICLE and JET. Hovering runs the vehicle
   transformation; the pills are how the jet is asked for, and how any of it
   works on a touch screen.
7. **Transform** — the big one: a jet transformation at size, then the fleet
   strip — nine airframes, one per hero, rendered off the stage GLBs.
8. **For parents** — ages 8–14, no gore, no chat with strangers, no purchases,
   no account, runs on a school laptop. AAA sites put a rating block here; this
   is the equivalent and it is the block that gets forwarded.
9. **Footer** — play link, GitHub, contact, © line.

Motion is CSS-only (scroll reveals, parallax on the hero) and every animation is
wrapped in `prefers-reduced-motion`. Full keyboard nav, real `<h1>`/`<h2>`
structure, alt text on every plate, Open Graph + Twitter cards pointing at the
keyart so a shared link unfurls into that image.

## Build order

1. ✅ `Tools/build_site_assets.py` — the asset pipeline (webp, alpha un-mix,
   video re-encode with posters, favicon set). **3.0 MB** of `Web/assets`.
2. ✅ `Web/index.html` + `styles.css` + `site.js` — the page.
3. ✅ Local check: served from `Web/`, driven at 1440 and 375 wide, console
   clean, no horizontal overflow, the hover-to-transform clips play.
4. ✅ `robots.txt`, `sitemap.xml`, `404.html`, OG image, favicons.
5. ✅ `Tools/deploy_site.ps1` — creates the Static Web App, uploads `Web/`, and
   prints the DNS records:

   ```
   powershell -ExecutionPolicy Bypass -File Tools/deploy_site.ps1
   ```
6. ✅ DNS in the `jah.cc` zone at **Cloudflare** (owned, zone active, free plan).
   Live 2026-08-03: `https://jah.cc` and `https://www.jah.cc` both serve the
   page on Azure-issued certificates, `http://` 301s to `https://jah.cc`, the
   custom 404 works, and `https://play.jah.cc` serves the game.
   The site answers on **both** `jah.cc` and `www.jah.cc`; the game keeps
   `play.jah.cc`.

   | Type | Name | Value | Proxy |
   | --- | --- | --- | --- |
   | CNAME | `@` | `kind-sea-0d8a69e0f.7.azurestaticapps.net` | **DNS only** |
   | CNAME | `www` | `kind-sea-0d8a69e0f.7.azurestaticapps.net` | **DNS only** |
   | TXT | `@` | the apex validation token from `-BindDomain` | — |
   | CNAME | `play` | `photonarenaweb.z13.web.core.windows.net` | **Proxied** |

   Three things that bite here:
   - **Grey cloud on `@` and `www`.** A proxied record resolves to Cloudflare's
     IPs, so Azure's validation never sees its own hostname, never validates,
     and never issues the certificate. Azure serves TLS for these two directly.
   - **The apex has no CNAME to validate against**, so Azure issues a TXT token
     instead — published at `@`, not at `_dnsauth` (that is Front Door's
     convention, not Static Web Apps'). Cloudflare's CNAME flattening handles
     serving the apex; no ALIAS record type is needed.
   - **`play` is proxied**, and — measured 2026-08-03, contrary to the warning
     written above it — needed **no** Origin Rule and no Host-header override:
     the storage static website answers on whatever Host arrives, and with
     `Accept-Encoding: gzip` (which every browser sends) Cloudflare passes the
     origin's `Content-Encoding: gzip` straight through, same Content-Length and
     same Content-MD5. That pass-through is the thing that had to be true —
     Unity's build has the decompression fallback off, so the browser is the
     only thing that can decompress the 240 MB data file.

   The Cloudflare token in `.secrets/cloudflare_token.txt` can read the zone but
   **not** its DNS records — it was scoped for the TURN work. DNS edits need a
   token with Zone → DNS → Edit on `jah.cc`, or the dashboard.

   **The failure worth remembering:** `www` was created proxied and only flipped
   to grey a few minutes before the bind. Azure had already cached the proxied
   answer, sat in `Validating` for ~12 minutes, then went `Failed` with "An
   unknown error has occurred… please try again later". The cure is
   `az staticwebapp hostname delete` followed by `set` again — a second bind
   against the same DNS went `Validating → Adding → Ready` in about four
   minutes. The apex never had this problem, because TXT validation reads a
   record the proxy does not touch.

### Two facts worth keeping

- **The clips are not uniform.** Eight transform videos were captured on the
  game's near-black; Titan's came back on a mid grey. `build_site_assets.py`
  reads each clip's corner colour and keys only the ones that are too light —
  tight tolerance, because Titan's hands and visor are grey too.
- **`Web/assets` is derived and LFS-tracked** (the repo sends `*.webp`, `*.mp4`
  and `*.png` to LFS). Committed anyway, since deploying is a plain upload from
  disk. If this ever moves to a GitHub Actions deploy, the checkout step needs
  `lfs: true` or the site will publish with pointer files where the art was.

## Not in v1, deliberately

Newsletter capture (needs a backend and, for under-13s, a COPPA-shaped consent
flow this project has explicitly avoided), a news/blog section (nothing to put in
it yet), a trailer (there is no recorded gameplay video in the repo — the
transform clips stand in until there is), and localisation (the Chinese modes
teach characters; they do not imply a Chinese-language site yet).
