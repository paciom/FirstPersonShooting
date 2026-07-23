# Project Plan: "Photon Arena" — Kid-Friendly Sci-Fi FPS for Unity3D

A stylized sci-fi first-person shooter for ages 8–14 with **Player vs AI** and **AI vs AI** modes, built around high-tech gadgets (lasers, jetpacks, portals, shields, time gadgets, shrink bombs, invisibility) — and designed from day one to produce spectacular gameplay videos that can be auto-recorded and uploaded to YouTube.

---

## 1. Core Concept & Content Rating

The game must be appropriate for kids AND safe for YouTube monetization/recommendation:

- **Sci-fi energy combat**: laser blasters, plasma bolts, photon beams. Hits drain a visible **energy shield**, never flesh — no blood, no gore, no realistic firearms.
- **"De-rezzed" instead of killed**: when a shield hits zero the character dissolves into glowing voxels/light particles ("de-rezzed!") and re-materializes at a spawn portal with a teleport effect. Death never exists in the fiction.
- **Bright, colorful, stylized sci-fi art** — think Fortnite / Overwatch / TRON-for-kids: neon accents, glowing energy effects, holographic UI, clean futuristic arenas. This aesthetic is *also* what performs best on YouTube for this demographic.
- **Positive tone**: robot announcer, holographic emotes, victory dances, no taunting or dark themes.
- Target equivalent of **ESRB E10+ / PEGI 7**.

**Game modes for launch:**
1. **Player vs AI** — the player + AI teammates vs AI opponents (Team De-Rez Battle, e.g., 4v4).
2. **AI vs AI** — fully autonomous matches with broadcast-style cameras (the "content machine" for YouTube).

Both share one match system; the only difference is whether a human controls one character.

---

## 2. Weapons & Gadget Sandbox (the heart of the game AND the videos)

Gadgets are the differentiator: they create emergent, clip-worthy moments (portal plays, shrink escapes, invisible flanks). Every gadget is designed to be **readable on camera** — big visual telegraphs, distinct colors, satisfying payoffs.

### Primary weapons (shield-drain damage, all projectile/beam — visible on camera)
| Weapon | Behavior | Visual signature |
|---|---|---|
| **Laser Blaster** | Mid-range bolt rifle, the all-rounder | Bright colored bolts with trails |
| **Photon Beam** | Continuous short-range beam, melts shields up close | Solid glowing beam + sparks at contact |
| **Plasma Lobber** | Arcing plasma orbs, area burst | Slow glowing orb, big splash ring |
| **Rail Zapper** | Slow-charging long-range sniper | Charge glow → instant neon rail line |
| **X-Ray Scope** (attachment) | See enemy silhouettes through walls while scoped, at a cost (slow move, visible glow gives YOU away) | Wall-hack silhouette shader — looks amazing in videos |

### Gadgets (each character carries 1–2; cooldown-based)
| Gadget | Effect | Counterplay / balance | Camera moment |
|---|---|---|---|
| **Jetpack** | Burst flight / hover, limited fuel | Fuel gauge, visible thruster flare while flying | Aerial dogfights, drone-cam chases |
| **Energy Shield** | Deployable bubble or forward barrier, breaks after absorbing damage | Enemies can walk through; shatters dramatically | Slow-mo shield-shatter |
| **Portal Gun** | Place linked entry/exit portals for the whole team | Portals glow loudly, enemies can use them too | Portal flank reveals, shots through portals |
| **Time Rewinder** | Rewind YOUR position/shield 3 seconds (escape tool) | Long cooldown; ghost trail shows where you'll snap back | Ghost-trail rewind effect |
| **Chrono Bubble** | Dome that slows everything inside (projectiles crawl) | Affects allies too; caster commits to it | Bullet-time dome — instant highlight material |
| **Shrink Bomb** | Shrinks enemies caught in the blast for ~6 s: tiny, fast, squeaky voices, weak lasers | Shrunken players are hard to hit; it's an escape as much as a punishment | Comedy gold; kids will love this |
| **Invisibility Cape** | Near-invisible for ~8 s; shimmers when moving fast, fully breaks on firing | Footstep ripples + shimmer keep it fair and watchable | Predator-shimmer reveals |
| **Phase Pills** | Walk through walls for ~3 s (can't shoot while phased, ghostly glow) | Visible glow through the wall warns defenders | Through-the-wall ambush reveals |
| **Grav Grenade** | Pulls loose objects & players toward a point, gentle toss | Repositioning tool, not damage | Chaotic clustered scrambles |

**Rules for every gadget** (keeps the game fair and the videos legible):
1. **Loud telegraph** — every effect has a distinct color, sound, and buildup.
2. **A counter exists** — invisibility shimmers, portals are two-way, phase-walkers glow through walls.
3. **Time-boxed** — everything expires in seconds; no permanent advantages.
4. **Reads at a glance** — a first-time viewer must understand what happened without explanation.

Launch with **Jetpack + Shield + Portal + Shrink Bomb + Invisibility Cape** (5 gadgets, one per AI archetype); add Time Rewinder, Chrono Bubble, Phase Pills, X-Ray Scope, Grav Grenade in content updates — each new gadget is also a fresh YouTube video topic ("NEW: SHRINK BOMB vs INVISIBLE CAPE?!").

---

## 3. Technology Stack

| Area | Choice | Why |
|---|---|---|
| Engine | **Unity 6 (LTS)** | Latest LTS, best Recorder/Cinemachine support |
| Render pipeline | **URP** | Fast stylized rendering; bloom makes all the neon/laser VFX glow |
| VFX | **VFX Graph + Shader Graph** | Lasers, portals, de-rez dissolves, shield shaders, invisibility shimmer |
| Cameras | **Cinemachine 3** | Cinematic/spectator cameras, free |
| Video capture | **Unity Recorder** (in-Editor) + **FFmpeg pipeline** (in builds) | Easiest path now; batch automation later |
| AI navigation | **NavMesh + AI Navigation package** (+ Off-Mesh Links for jetpack routes) | Built-in, reliable |
| AI decisions | **Behavior trees / FSM** (Unity Behavior package or hand-rolled) | Debuggable, tunable difficulty |
| Input | **Input System package** | Gamepad + mouse/keyboard |
| UI | **UI Toolkit or uGUI + TextMeshPro** | Holographic HUD, scoreboards, de-rez feed |
| Assets | Synty sci-fi low-poly packs / Kenney.nl sci-fi (free) / Asset Store VFX | A consistent stylized look without an art team |

---

## 4. Architecture Overview

```
GameManager (match flow: lobby → countdown → match → victory → replay)
 ├── MatchConfig (mode: PvAI / AIvAI, team sizes, arena, loadouts, time/score limits)
 ├── TeamManager (spawn portals, scoring, re-materialization)
 ├── CharacterController layer
 │     ├── PlayerBrain   (reads Input System)
 │     └── AIBrain       (behavior tree → same character interface)
 │     └── CharacterMotor + EnergyShield + WeaponSystem + GadgetSystem (shared)
 ├── GadgetSystem (modular: each gadget = self-contained module w/ cooldown, VFX, AI usage hints)
 ├── CameraDirector (Cinemachine)
 │     ├── FirstPersonCam (PvAI)
 │     └── BroadcastCams  (AIvAI + replays: follow cams, drone cams, de-rez cam)
 ├── RecordingManager (start/stop capture, filenames, thumbnails, match-stats JSON)
 └── UIManager (holo-HUD, scoreboard, announcer, spectator overlay)
```

**Key principles:**
- **Brains are swappable.** Player and AI drive the same `CharacterMotor`/`WeaponSystem`/`GadgetSystem` interface. AI vs AI is nearly free once PvAI works, fairness is guaranteed, and you can possess any bot to debug.
- **Gadgets are plug-in modules.** Each implements one interface (`Activate`, `Tick`, `Expire`, plus `AIUsageHint` so bots know when it's smart to use it). Adding gadget #6 never touches core code.

---

## 5. AI Design (a gameplay feature AND a content feature)

AI must be fun to play against *and* fun to watch. Watchable AI ≠ optimal AI.

- **Behavior tree per bot**: Patrol → Seek objective → Engage → Use gadget → Take cover → Retreat/regroup → Celebrate.
- **Personality archetypes tied to signature gadgets** (viewers pick favorites):
  - *Rocketeer* — jetpack, aerial rushdown
  - *Bastion* — energy shield, holds ground, protects teammates
  - *Trickster* — portal gun, flanks and escapes
  - *Prankster* — shrink bombs, chaotic mid-range
  - *Phantom* — invisibility cape, sneaky picks
- **Human-like imperfection**: 200–400 ms reaction delays, aim-error cones, turn-speed limits, occasional gadget misuse (a Prankster shrinking itself occasionally is a feature). No aimbot behavior.
- **Gadget intelligence via hints, not omniscience**: each gadget module publishes simple usage conditions (e.g., Shield: "ally shield < 30% and under fire"); personalities weight them differently.
- **Difficulty tiers** for PvAI (Cadet/Ranger/Legend) scaling reaction time and accuracy — never wall-hacks (except the X-Ray Scope, which everyone can equip).
- **Drama hooks for AI vs AI**: subtle comeback rubber-banding, last-bot-standing slow-mo, rivalry tracking ("Phantom de-rezzes Bastion for the 3rd time!") in the de-rez feed and announcer lines.

---

## 6. Visuals & "Watchability" System

A first-class system, not end-of-project polish:

1. **Stylized sci-fi look**: URP toon/flat shading with emissive neon accents, strong team colors (cyan vs orange), bloom-heavy post-processing so lasers/portals/shields glow, juicy feedback (hit sparks, shield ripple shaders, hit-stop frames, screen shake).
2. **Signature VFX moments** (built in Shader Graph/VFX Graph):
   - **De-rez dissolve** — body breaks into glowing voxels that get sucked toward the sky.
   - **Shield shatter** — glass-like energy fragments in slow-mo.
   - **Portal surfaces** — swirling view-through discs.
   - **Invisibility shimmer** — refraction outline that intensifies with movement.
   - **Shrink pop** — squash-and-stretch with a puff of stars.
3. **Readable arenas**: 2–3 compact sci-fi maps (Space Station Ring, Neon Rooftops, Portal Labs) with clear landmarks, jetpack verticality, camera-friendly sight lines, and skybox spectacle (planets, nebulae).
4. **CameraDirector for AI vs AI** (the heart of good videos):
   - Pool of Cinemachine cameras: over-shoulder follow per bot, elevated stadium cams, fly-through drone cam, portal-cam (looking out of a portal), objective cam.
   - A **director brain** scores per-bot "interest" every second (in combat? shield critical? on a streak? just activated a gadget? mid-jetpack-chase?) and cuts to the best camera with minimum shot lengths (2–4 s). **Gadget activations spike interest** — the director naturally follows portal plays and shrink chaos.
   - **De-rez cam / highlight replays**: ring-buffer of the last ~10 s of transforms → replay big moments in slow motion from a cinematic angle.
5. **Broadcast UI overlay** for AI vs AI: holographic scoreboard bug, de-rez feed with gadget icons, bot nameplates with shield bars, robot-announcer captions — like an esports stream from the future.
6. **Match intro/outro cinematics**: drone fly-in of the arena + team lineup with gadget poses at start; podium celebration + stats (Most De-Rezzes, Best Gadget Play) at the end. Every video gets a natural thumbnail and a clean start/end.

---

## 7. Auto-Recording Pipeline

Two tiers — build Tier 1 now, Tier 2 when you want hands-off batch production:

**Tier 1 — Unity Recorder (Editor play mode), fastest to working videos**
- Drive Unity Recorder **from script** via `RecorderControllerSettings`: `RecordingManager` starts capture at the intro cinematic and stops after the outro.
- Output: MP4 (H.264), 1080p or 4K, 60 fps, timestamped names like `AIvAI_PortalLabs_2026-07-23_1.mp4`.
- Auto-capture a **thumbnail PNG** at the victory pose, plus a **match-stats JSON** (winner, scores, best plays, gadgets used) for titles/descriptions later.
- Result: run match → video + thumbnail appear in a folder → upload.

**Tier 2 — Batch production (standalone build + FFmpeg)**
- Unity Recorder doesn't run in player builds; for overnight batches capture frames via `AsyncGPUReadback` from a render texture and pipe raw frames + audio into an **FFmpeg** child process (FFmpegOut-style).
- **Match Scheduler**: JSON playlist (`arena`, `teams`, `loadouts`, `seed`, `duration`) → run N AI vs AI matches back-to-back, one video each.
- Deterministic seeds so a great match can be re-run and re-filmed from new angles.

**YouTube upload automation (optional final step)**
- Python + YouTube Data API v3 script watching the output folder: uploads video + thumbnail, builds title/description from the match-stats JSON (e.g., "PHANTOM's invisible ambush wins it! | Photon Arena AI Battle #12").
- **Important**: kid-targeted content must be marked **"Made for Kids"** (COPPA) — bake it into the upload defaults.

---

## 8. Development Phases

**Phase 0 — Project setup (few days)**
Unity 6 LTS + URP template; install Input System, Cinemachine, AI Navigation, Recorder, TextMeshPro; folder structure; import a sci-fi art pack (Kenney sci-fi free to start, Synty later).

**Phase 1 — Core FPS loop (1–2 weeks)**
First-person `CharacterMotor` (move/jump/look), Laser Blaster with visible bolts, `EnergyShield` damage model → de-rez dissolve → respawn at spawn portal, one greybox arena with NavMesh, basic holo-HUD (crosshair, shield bar, score).

**Phase 2 — AI opponents → Player vs AI mode (2–3 weeks)**
`AIBrain` (FSM/behavior tree), NavMesh movement, target selection, aim-error model, difficulty tiers, 4v4 Team De-Rez Battle scoring, match flow. *Milestone: a fun 4v4 PvAI laser match.*

**Phase 3 — Gadget sandbox v1 (2–3 weeks)**
`GadgetSystem` plug-in framework + the 5 launch gadgets (Jetpack, Shield, Portal, Shrink Bomb, Invisibility), AI usage hints per gadget, personality archetypes wired to signature gadgets, off-mesh jetpack links. *Milestone: bots using gadgets intelligently against you.*

**Phase 4 — AI vs AI mode + CameraDirector (2 weeks)**
All-bot matches, CameraDirector interest system + Cinemachine camera pool (gadget activations spike interest), broadcast overlay, announcer stubs, intro/outro cinematics. *Milestone: press Play, watch a full hands-off match that's genuinely fun to watch.*

**Phase 5 — Auto-recording Tier 1 (1 week)**
RecorderController integration, auto start/stop, filenames/thumbnails/match-stats JSON, "Record Match" button + simple in-Editor batch mode. *Milestone: one click → finished MP4 + thumbnail on disk.*

**Phase 6 — Visual polish & juice (2–3 weeks)**
Real art pass (toon + emissive neon, team colors, space skyboxes), signature VFX (de-rez dissolve, shield shatter, portal surfaces, shimmer, shrink pop), animations & emotes, robot announcer VO + synth music, slow-mo highlight replays, 2nd arena.

**Phase 7 — Batch production & upload automation (1–2 weeks, optional)**
FFmpeg capture for builds, Match Scheduler playlist, YouTube upload script with Made-for-Kids defaults, seed library of great matches.

**Phase 8 — Playtest, tune & content cadence (ongoing)**
Playtests with kids 8–14 (difficulty, readability, fun); watch-tests of AI vs AI videos (retention past 30 s?); then a content rhythm: each new gadget/arena/archetype = new videos ("NEW GADGET: PHASE PILLS — can Phantom counter it?").

---

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Gadget sandbox balance spirals | Every gadget time-boxed with a built-in counter; launch with only 5; plug-in architecture isolates each one |
| AI vs AI matches are boring to watch | Interest-based CameraDirector keyed to gadget activations, personalities, announcer + de-rez feed, slow-mo highlights, 3–5 min matches |
| VFX-heavy scenes tank frame rate (ruins 60 fps recording) | URP + pooled VFX Graph effects, particle budgets per gadget, profile on target hardware early |
| Recorder doesn't work in builds | Tier 1 (Editor) first; FFmpeg pipeline only when batch scale is needed |
| Scope creep on art | Asset packs + one consistent neon-toon style; custom work only for signature VFX |
| YouTube kid-content rules | Non-violent "de-rez" fiction from day one; Made-for-Kids flag; no chat/UGC |
| AI too hard/easy for 8-year-olds | Cadet/Ranger/Legend tiers via reaction-time/accuracy knobs; playtests with real kids |

---

## 10. First Concrete Steps

1. Create Unity 6 LTS project (URP template) in this folder.
2. Install packages: Input System, Cinemachine, AI Navigation, Unity Recorder.
3. Build Phase 1 greybox: capsule player, laser blaster with glowing bolts, dummy targets that de-rez into voxels.
4. Add 3 NavMesh bots that chase and shoot back — the moment this works, both game modes and the whole video pipeline have their foundation.
