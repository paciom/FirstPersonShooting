# MOVIE_PLAN.md — Photon Arena Story Studio

AI-written robot episodes, staged and rendered in Unity from a reusable animation
library. Two outputs from the same episode file: YouTube MP4s and interactive
WebGL episodes with choices.

Researched 2026-08-04/05 (Meshy docs, Mixamo live catalog API, Unity docs,
prior-art survey incl. Fable's Showrunner and FilmAgent). Verdict: **feasible,
with one reframe** — see below.

---

## 1. The reframe: build a studio, not a generator

The original idea: AI writes an episode → Meshy generates/rigs the movements the
story needs → Unity renders. Two corrections from research:

1. **Meshy's public API has no text-to-animation.** It applies clips from a
   ~600-preset library by `action_id` to a rigged model. (The webapp has a
   "Text to Motion" button at 3 credits/clip, but it is webapp-only — generated
   clips join the pipeline as exported GLB/FBX, not API calls.) So "Meshy rigs
   the movements per story" isn't a thing that exists — anywhere, at any vendor.
2. **That's good news, not bad.** The valuable asset was never each movie — it's
   the reusable **verb library** + **shot grammar** + **story compiler**. Once a
   story's vocabulary is covered by the library, the marginal cost of an episode
   is LLM tokens + TTS: **under ~$3, usually under $0.50**. Text-to-video
   (Veo/Kling/Runway) costs $2–45 per *minute*, forever, drifts identity across
   episodes, and can never be interactive. A fixed cast on a fixed asset library
   is how Red vs. Blue ran for 21 years on Halo assets — consistency is a
   *feature* of a series, not a limitation.

So the plan: **freeze the cast (the 9 robots), grow a verb library once, compile
unlimited screenplays against it.** The LLM is constrained to the library's
vocabulary — which prior art (FilmAgent, Hidden Door) shows is exactly what makes
LLM-staged content work instead of drift into nonsense.

### Why we can win where Showrunner is criticized

Fable's Showrunner (the "AI South Park" company, launched July 2025, Amazon-backed)
is publicly criticized as "AI-generated static images of characters talking in
robotic AI-generated voices." Its weaknesses are exactly our strengths:

| Failure mode (reported)       | Our answer                                              |
|-------------------------------|---------------------------------------------------------|
| Barely-animated slides        | Real 3D staging + 500+ real clips already in the repo   |
| Robotic voices                | Neural TTS with per-line emotion direction (§5)         |
| No camera craft               | Cinemachine shot grammar + the existing camera-director DNA (SpectatorCamera, Brawl director) |
| Not interactive               | Same episode file plays in WebGL with choice points     |
| Identity drift                | Fixed cast/sets by construction                         |

Bonus nobody else has: **transformation sequences already exist** —
robot→tank stop-motion stages for all 9 robots and the robot→jet pipeline.
Transformers-style episodes get their signature moves for free.

---

## 2. What the repo already provides (inventory 2026-08-04)

- **Cast:** 9 robots, one byte-identical 24-joint Mixamo-named skeleton
  (verified by parsing every `Assets/Models/Meshy/*-rig.glb`), Generic rig via
  glTFast, fighter prefabs + controllers in `Assets/Resources/Brawl/`.
- **Clips:** ~460 `.anim` (fight set ~27/robot, locomotion 9/robot, `_meshy`
  harvests), 171 Meshy fight GLBs, transformation stage GLBs
  (`Assets/Models/Stages/`), plus proven forges: `BrawlMoveForge` (pose-template
  bake), `MeshyWalkerForge`, `Tools/meshyfight.py` (rig→animate→download→analyze).
- **Sets:** 8 procedural themed arenas (`Assets/Scripts/Arenas/Definitions/`),
  Brawl stages (Frontline/Carrier/Quarry), Commander buildings, TD canyon,
  Dogfight sky — all runtime-built, swapped via `ArenaRuntime`.
- **Camera:** Cinemachine 3.1.7 **installed, never used**. SpectatorCamera's
  header calls itself "the seed of the full interest-based CameraDirector";
  the Brawl camera already scores azimuths for occlusion + side-on framing.
- **Dialogue plumbing:** `ChatterDirector` + `RadioLog` (speaker-attributed
  subtitle feed), `MatchAnnouncer` (headline overlay),
  `Tools/generate_chatter.py` (LLM-authored line bank with placeholder
  whitelist — the exact authoring pattern the story compiler extends).
- **TTS:** SAPI bake pipeline (`Tools/chinesevoice.ps1`, 939 clips) — the
  *pattern* is reusable; the *voice quality* is not (§5).
- **Recording:** Unity Recorder 5.1.6 installed unused; Timeline 1.8.12 present
  transitively; battle-tested headless capture entry points (MenuCapture,
  PreviewCaptureTool); WebGL build + Azure deploy + www.jah.cc.

Missing entirely: sequencer, screenplay schema, shot presets, English voices,
staging layer. That's the build.

---

## 3. Architecture

One episode = one JSON file. Everything downstream is deterministic.

```
story premise ─► LLM (generate_episode.py) ─► episode.json ─► validator
                                                                 │
                                              ┌──────────────────┴────────────┐
                                              ▼                               ▼
                                     ScreenplayPlayer (runtime         same player in
                                     sequencer in the editor)          WebGL build
                                              │                               │
                                     Unity Recorder ─► MP4            interactive episode
                                     (YouTube)                        with choices (www.jah.cc)
```

**Sequencer, not runtime Timeline.** Research is unambiguous: every shipped
data-driven cutscene system (Pixel Crushers, Naninovel, Ink) interprets commands
with a runtime sequencer; teams that generated TimelineAssets at runtime
abandoned it (ExposedReference binding pain, editor-only helpers). Our
`ScreenplayPlayer` is a coroutine sequencer in the `BrawlShow` idiom — which is
already a working captioned act-sequencer. Timeline is not needed at all.

**Episode schema (v1 sketch):**

```json
{
  "title": "The Rust Storm", "set": "Foundry", "music": "tension-01",
  "cast": ["ranger", "titan", "bolt"],
  "scenes": [{
    "location": "Foundry", "marks": {"ranger": "A", "titan": "B"},
    "beats": [
      {"line": {"who": "ranger", "text": "Something's wrong with the reactor.",
                 "emotion": "worried", "verb": "talk_point"},
       "shot": "closeup:ranger"},
      {"action": {"who": "titan", "verb": "walk_to", "target": "C"},
       "shot": "tracking:titan"},
      {"choice": {"prompt": "Follow Titan?", "options": [
         {"text": "Yes", "goto": "scene-3"}, {"text": "Stay", "goto": "scene-4"}]}}
    ]
  }]
}
```

Verbs, shots, sets, emotions, and cast come from **published vocabulary lists**
that are pasted into the LLM prompt and enforced by the validator. Unknown verb →
validator substitutes nearest verb and appends the miss to `verb_wishlist.txt`
(the library-growth loop). Beat timing is audio-duration-driven: a line beat
holds for its WAV length (the standard AudioWait pattern; text-length fallback).

---

## 4. The verb library (one-time cost, near zero dollars)

**Target: ~60–80 verbs, each verb = per-robot `.anim` on the shared skeleton.**

Sources, cheapest first — all verified against live catalogs 2026-08-04:

1. **Already owned (free):** ~460 clips — full fight set, locomotion, jumps,
   get-ups, victory, transformations. Combat episodes are ~fully covered today.
2. **Mixamo (free, 2,446 clips):** audited — deep conversational coverage:
   talking ×15, head nod ×5 / shake ×4, agree/dismiss/shrug/think/surprised/
   laugh/cry/bored/defeated, 20+ sit idles and sit↔stand transitions, 7 pointing
   variants, prop verbs (pick up, throw, carry box, push button, open door ×5,
   drink, type, write). ~60–80 solo acting verbs before spending anything.
   **Mixamo is unmaintained legacy (no updates since 2015, multi-day outage
   June 2025): bulk-download the grab list as a one-time insurance step.**
3. **Meshy presets (0 credits in webapp):** Talk family (308–314),
   Listening_Gesture 47, sit family, waves, Pick_Up 273–284, open_door 285–289 —
   native on the robots' own rigs. API route costs 3 cr/clip if automated via
   `meshyfight.py` and the fleet-pass rig ids.
4. **Meshy Text-to-Motion (3 cr/clip, webapp):** only for audited gaps —
   listening loop, prop handoff, hug/high-five, robot-specific beats.
   Budget **~15–30 clips ≈ 45–90 credits** (balance was 1156).

**The Mixamo→robot bridge (the one real technical build):** the robots are
Generic (glTFast has no Humanoid import), Mixamo clips are Humanoid. Verified
path — keep the entire existing Generic stack unchanged and add
`Assets/Editor/MixamoBakeForge.cs`:

- Editor-time: build a Humanoid Avatar per robot on the editor-imported rig GLB
  (24 joints satisfy Humanoid's 15 required; 22 map, incl. shoulders/chest/toes;
  no fingers — finger motion drops, irrelevant for robots). The rig rest pose is
  a bent A-pose (arms ~58° down), so **programmatic T-pose enforcement first**.
- Play each Mixamo clip through that robot's Avatar (muscle-space retarget
  handles proportions per robot), sample with `GameObjectRecorder`, save a
  Generic clip keyed to the shared bone paths — structurally identical to what
  `BrawlMoveForge` already does, so it inherits the known rules: pin Hips to
  skeleton rest (root-motion rule), author curves fully **before**
  `AssetDatabase.CreateAsset` (curve-edit trap), clips-in-place/motor-moves.
- 60 verbs × 9 robots = 540 clips, overnight batch, $0.

**QA bench:** extend the BrawlShow pattern into a **VerbShow** — every robot
performs every verb, captioned with source (mixamo/meshy/template), judged on
screen. Same judging bench that already gated the fight clips.

---

## 5. Voices — the make-or-break (per prior art, literally)

Flat robotic voices are the #1 reported killer of every generate-an-episode
product. SAPI is exactly that tier. Both viable options are cheap:

| Option | Cost / 10-min episode | Direction | Notes |
|---|---|---|---|
| **Azure Neural TTS** (default) | **~$0.15** ($16/1M chars; HD $22/1M from Mar 2026) | ~30 SSML `express-as` styles (angry, excited, whispering, shouting) + styledegree | Already on Azure; 50+ en-US voices; paid tier grants royalty-free commercial output incl. monetized video + baked WebGL audio; word-boundary events give subtitle timing for free |
| ElevenLabs v3 (hero episodes) | ~$1.70–2.50 | Inline tags (`[excited]`, `[whispers]`, `[laughs]`), multi-character dialogue mode | Best performance; paid plans = perpetual commercial rights |
| Chatterbox / Orpheus (local, MIT/Apache) | $0 | emotion exaggeration / tags | Fallback; needs GPU |

Pipeline: same shape as `chinesevoice.ps1` — bake WAVs offline per line, name by
hash, robots get per-character voice + a light "radio" filter (pitch/ring-mod)
for the robot timbre. 9 distinct voice personas map to the battle-league
personalities. Robots need **no lip sync** (the droid/Transformers convention):
head look-at + eye-glow pulse on the active speaker + gesture verbs during lines.

---

## 6. Cinematography

Shot grammar as data, implemented once with Cinemachine 3:

- **Shot presets** (the proven Dialogue System pattern): `wide`, `establishing`,
  `medium:X`, `closeup:X`, `ots:X>Y`, `tracking:X`, `orbit`, `crash:X`, `dutch` —
  each a CinemachineCamera prefab positioned relative to its subject, LookAt
  composed by Rotation Composer.
- **Auto-coverage fallback:** CinemachineClearShot scores occlusion/distance and
  picks the best of several rigs when the screenplay doesn't specify — and the
  occlusion lessons are already institutional knowledge (Brawl camera: sphere-cast
  AND ray, embedded-end veto, gaze rescue).
- **Grammar defaults** so the LLM can't film badly: new location → establishing;
  dialogue → alternate OTS/closeup on speaker; action verb → tracking/wide;
  never two identical consecutive shots; cut on beat boundaries, blend inside
  beats (name-matched custom blends).

Subtitles ride the existing RadioLog idiom (team-colored, speaker-attributed);
MatchAnnouncer becomes the title-card/narrator overlay. VFX cues in beats map to
the owned packs (WarFX, Vefects fire) through the existing `BrawlFx` hardening.

---

## 7. Rendering (offline path)

Unity Recorder in `-batchmode` is **confirmed broken by Unity's own docs** (no
graphics pipeline → recording never starts). Supported route, which fits the
existing editor-closed batch discipline:

- Launch the editor **windowed** from CLI: `-projectPath -executeMethod
  EpisodeRender.Run -episode <file>` (no `-batchmode`); the method configures
  RecorderController (H.264 MP4 + game audio, stereo), enters Play mode,
  ScreenplayPlayer runs the episode with `Time.captureDeltaTime` locked to the
  frame rate, exits on completion.
- Fallback if audio capture misbehaves: PNG sequence + ffmpeg mux (the WAV
  timeline is already known from the episode JSON — deterministic).
- Thumbnails/packaging reuse the menu-art pipeline (robot renders + plates).

Interactive path notes (WebGL): re-simulate the episode in-engine (never ship
video — VideoClip assets don't work in WebGL); audio starts on first user
gesture (the choice UI provides one); voice WAVs fetched per-episode via
UnityWebRequest rather than Resources to keep the 400 MB build from growing.

---

## 8. Phases

**Phase 0 — Proof (1–2 days).** Hand-write one 60–90 s episode JSON (2 robots,
one arena, ~8 beats, 1 choice point ignored in render). Build minimal
`ScreenplayPlayer` (marks, walk_to, 4 verbs from existing clips, line beats),
3 Cinemachine shot presets, Azure TTS bake for 2 voices, EpisodeRender to MP4.
**Exit test: does 90 seconds feel like a show?** This de-risks the entire idea
before any library investment.

**Phase 1 — Verb library.** Mixamo bulk grab + `MixamoBakeForge` (T-pose
enforcement, per-robot Humanoid bake to Generic) + verb manifest + VerbShow QA
bench + Meshy preset/Text-to-Motion gap pass (≤90 credits). Exit: ~60 verbs ×
9 robots green on the bench.

**Phase 2 — Story compiler.** `Tools/generate_episode.py` (extends the
generate_chatter.py pattern): series bible + personalities + vocabulary lists in
prompt → episode JSON; validator with nearest-verb substitution +
`verb_wishlist.txt`; staging solver (marks on arena floor plans, spacing,
look-at); continuity memory across episodes (standings, running gags).

**Phase 3 — Presentation.** Full shot grammar + grammar defaults; per-robot
voice casting + robot filter; music/SFX beds; VFX cues; title cards + credits;
subtitle polish (word-boundary karaoke from Azure events).

**Phase 4 — The batch studio.** One command: premise in → MP4 out. Episode
queue, seeded randomness for retakes, YouTube packaging. First mini-season
(e.g., 5 × 3–5 min episodes) to calibrate quality and pace.

**Phase 5 — Interactive episodes.** ScreenplayPlayer in the WebGL build + choice
UI + branch graphs (LLM writes branching episodes natively — it's just more
scenes); publish on www.jah.cc; later, on-demand generation (server authors
JSON, client plays it — COPPA posture: kids pick from curated premises, no
free-text into the LLM).

Optional Phase 6 — bilingual episodes: the Chinese Quest voice/subset-font
pipelines make English/Chinese dual-subtitle learning episodes a small delta.

---

## 9. Budget

| Item | Cost |
|---|---|
| Verb library (Mixamo + bake forge) | $0 + machine time |
| Meshy gap clips (~15–30 × 3 cr) | 45–90 credits (~$1–2; balance 1156) |
| Azure TTS | ~$0.15 / episode (free tier: 500K chars/mo likely covers a season) |
| LLM screenplay | ~$0.05–0.50 / episode |
| Text-to-video comparison | $2–45 / minute, every time, no reuse, no interactivity |

**Marginal cost per additional episode: well under $1.** That's the whole thesis
working.

## 10. Risks & mitigations

- **Stock-clip stiffness** ("everyone moves like a template"): camera pace and
  editing carry it (machinima's proven trick), layered head look-at, VFX
  punctuation, and the robot fiction itself — audiences accept stiff robots.
- **Two-character contact beats** (handoffs, hugs): don't exist in any library.
  Shoot around them — cuts, OTS framing, impact VFX (the Brawl solution), or a
  Text-to-Motion pair. Never block a story on them.
- **LLM staging errors**: the vocabulary constraint + validator is the
  research-backed fix (FilmAgent's 65 positions / 9 shot types scored 3.98/5).
  Everything invalid degrades to a legal nearest verb, never a crash.
- **Voices sound samey**: 9 fixed personas, styledegree per line; upgrade hero
  episodes to ElevenLabs.
- **Mixamo disappears**: bulk-download at Phase 1 start; clips already baked to
  our own `.anim` files are ours regardless (royalty-free, embedded-in-project).
- **Recorder/audio flakiness**: PNG+ffmpeg fallback is deterministic.

## 11. Open decisions (not blockers)

1. TTS default: Azure ($0.15/ep, on our stack) vs ElevenLabs (~$2/ep, best
   direction). Plan assumes Azure default, ElevenLabs for flagship episodes.
2. Episode length target: 3–5 min (matches the league-episode direction) vs
   longer arcs.
3. First format: standalone stories vs episodes woven around the battle league
   (league matches as the B-plot is a natural hybrid).
