# Brawl — a Street Fighter–style versus mode for Photon Arena

A new main-menu item, `BRAWL`: two robots on a raised holographic stage, side-on
camera, health bars, a round timer, best-of-3 rounds. Punches, kicks, flying
kicks, blocks and a chargeable PHOTON BLAST special. Player picks both fighters
on the existing robot-select screen and plays P1 (cyan) against an AI P2
(magenta). Kid-friendly throughout: hits spark, nobody bleeds, a KO de-rezzes
the loser into light and the winner strikes a victory pose.

Decisions taken 2026-07-30: ships in two animation tracks — script-forged
placeholder moves first (zero credits, playable immediately), then a Meshy
animation pass per robot behind the same forge (probe one robot now, fleet
after eyeballing); lane-locked 2.5D combat with seconds-based frame data; no
new scene serialization — the mode is 100% runtime-generated like Commander.

> **Status 2026-07-30: plan written, build starting on `feature/commander`.**

---

## 1. What this rides on (already built)

Like Commander, Brawl is less new code than it looks.

| Need | Existing system | Notes |
| --- | --- | --- |
| Fighters | 9 rigged walker prefabs in `Assets/Models/Generated/` | Animator + Speed blend tree + `RobotLocomotion` (drives walk from position delta — works unchanged for fighter footsies) |
| Fighter picking | `RobotSelectMenu` | Already picks BOTH sides' robots with team paint previews; Brawl routes through it exactly like Player v AI |
| Spawning & tint | `RobotFactory.InstantiateNormalized` | Height-normalized model under a `Body` node, team tint applied |
| Team colours | `MatchAnnouncer.TeamColor`, `TeamPaint` | Cyan v magenta, same as everywhere |
| World swap | `CommanderController`'s Environment swap | Deactivate `Environment` children, build our set, teardown restores via `ArenaRuntime.Load(CurrentIndex)` — one authority for world state |
| Stage visuals | `ArenaKit` / `ArenaMaterials` | The holographic look without new materials |
| KO fiction | De-rez (dissolve into light) + `VfxUtil` sparks | Nothing dies; kid-friendly stays intact |
| Special move | `LaserBolt` / bolt VFX family | PHOTON BLAST is the robots' own weapon fiction |
| Block visual | `ShieldBubble` / `EnergyShield` flash | Blocking reads instantly |
| Clip pipeline | `MeshyWalkerForge` | Proven GLB → cloned `.anim` → controller path; Brawl's forge follows it |
| Rig knowledge | `Tools/rigpaths.py`, `rigrest.py` | Joint paths and rest poses per robot — the delta-template bake these were written for |
| Meshy tooling | `Tools/meshyvehicles.py` pattern | Key in `.secrets/`, task ids in `Tools/*_tasks.json`, preview-then-approve spend discipline |
| Touch input | `TouchControls` | Pattern for the Brawl touch pad |

**Held invariants:** nothing serializes into `GreyboxArena.unity` (code edits
take effect on Play alone); mode fields follow the recompile-during-Play rules
(plain private fields, no readonly collections); projectiles spawn at scene
root, not under the stage root.

---

## 2. What is genuinely new

A fighting-game combat core (state machine + frame data + hit resolution), a
side-on tracking camera, a versus HUD with round flow, a fighting AI, and an
animation forge for fight moves. Roughly 8 new files under
`Assets/Scripts/Brawl/` plus one editor forge and one Python tool — one class
per file per project convention.

```
Assets/Scripts/Brawl/
  BrawlController.cs   mode lifecycle: Begin/Teardown, owns everything below
  BrawlStage.cs        runtime set: platform, edge glow, backdrop, stage lights
  BrawlCamera.cs       side-on rig: tracks midpoint, zooms with separation, shake
  BrawlFighter.cs      per-fighter state machine, lane motor, animator driving
  BrawlMoveSet.cs      frame data table (damage/range/startup/active/recover)
  BrawlInput.cs        P1 keys + touch pad adapter feeding a fighter
  BrawlBrain.cs        the AI opponent
  BrawlMatch.cs        rounds, timer, KO, best-of-3, rematch
  BrawlHud.cs          health bars, pips, timer, announcements
Assets/Editor/
  BrawlMoveForge.cs    bakes fight clips + controllers into Resources/Brawl/
Tools/
  meshyfight.py        rig + animate + download against the Meshy API
```

---

## 3. Design

### Stage and camera

The fight happens on a **raised strip, 18 m × 6 m**, floating in the arena
void: flat top, glowing team-neutral edge strips, corner pylons, a distant
backdrop panel so the camera never looks into raw skybox. Built at runtime by
`BrawlStage` in the Commander manner: deactivate `Environment`'s children,
parent the set there, teardown destroys it and calls `ArenaRuntime.Load`.
No NavMesh — fighters move on a line, not a mesh.

The stage adds its own key + fill lights aimed along the camera axis: the
arena key light points away from side-on framing (same lesson as the preview
rigs), and it may be deactivated with the environment anyway.

Camera: perspective, FOV ~50, on the lane's -Z side. X tracks the midpoint of
the two fighters (clamped to stage), distance eases between 8 m and 14 m with
fighter separation, height ~2 m looking slightly down. Post-processing on;
stage glow stays ≤ 2.5 emission per the bloom whiteout rule.

### The lane

Fighters live on the world X axis at z = 0, x ∈ [-8, +8] (stage corners are
real: you can be cornered). They always face each other; walking away is a
retreat. Minimum separation 0.9 m standing — jumps arc over. Jump is a simple
ballistic arc driven by the fighter motor (no physics bodies, no
CharacterController): deterministic and cheap, like everything else here.

### Moves and frame data (seconds, not frames)

Move timing lives in one table, `BrawlMoveSet`, so tuning is one file:

| Move | Damage | Range | Startup | Active | Recover | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| PUNCH (J) | 8 | 1.6 m | 0.12 | 0.10 | 0.20 | fast poke, hits airborne (anti-air) |
| KICK (K) | 12 | 2.0 m | 0.20 | 0.12 | 0.28 | slower, longer reach |
| FLY KICK (K in air) | 16 | 1.8 m | 0.10 | until land | 0.25 | jump-in; causes knockdown |
| BLOCK (hold S) | — | — | 0.05 | held | 0.10 | negates damage + tiny pushback, no chip (kid-friendly) |
| PHOTON BLAST (L) | 20 | full lane | 0.30 | projectile | 0.40 | needs full CHARGE meter (fills by landing hits); blockable |

Hit resolution is **range + window**, not colliders: an attack connects if the
defender is inside `range` along the lane (and in the right height band for
air/ground) during the active window. Deterministic, tunable, and immune to
skinned-bounds lies. Landing a hit: 0.25 s hit-stun + 0.8 m knockback; fly
kick and PHOTON BLAST knock down (1.0 s get-up, invulnerable while rising).
Both fighters attacking = both can trade (classic SF flavour).

### Rounds and match

HP 100. Round ends on KO, or at 60 s the higher HP takes it ("TIME!"); equal
HP is a draw and both take a pip. First to 2 pips wins the match. Flow:
`ROUND 1 … FIGHT!` → play → `K.O.!` / `TIME!` → loser de-rezzes, winner plays
victory → next round resets positions/HP → match end shows `P1 WINS` /
`CPU WINS` with REMATCH / ROBOTS / MENU buttons. Escape exits to menu at any
point (the GameModeController contract).

### Controls

Desktop P1: **A/D** move · **Space/W** jump · **J** punch · **K** kick
(air = fly kick) · **S** hold to block · **L** PHOTON BLAST when charged.
Touch (later phase): left/right/jump pad on the left, P/K/B buttons on the
right, via the TouchControls pattern.

### AI

`BrawlBrain` ticks a decision every 0.15–0.30 s: outside range → advance
(occasionally jump-in fly kick); in range → punch/kick mix; sees the player's
startup → blocks with reaction delay 0.2–0.4 s (difficulty knob); just got
knocked down → backs off a beat. Personality knobs (aggression / caution /
flair) are plain floats, ready to be tied to the battle-league personalities
later.

### Animator contract

Each robot gets `Resources/Brawl/{robot}.controller` (loaded by name at
runtime — no scene serialization): base layer = the existing Speed blend tree
(idle/walk — `RobotLocomotion` keeps feeding it, fighters never run), plus
one state per move behind triggers: `Punch`, `Kick`, `FlyKick`, `Block`
(bool), `Hit`, `Knockdown`, `KO`, `Victory`. Gameplay timing comes from
`BrawlMoveSet`, never from clip length — clips are stretched to fit the move
windows via animator state speed, so swapping placeholder clips for Meshy
clips changes nothing about game feel.

---

## 4. Animation: two tracks behind one forge

### Track A — script-forged placeholders (ships first, zero credits)

`BrawlMoveForge` bakes per-robot fight clips the way `rigrest.py` prescribed:
one hand-authored **delta template** per move (punch = shoulder+elbow jab,
kick = hip+knee swing, block = forearms crossed, hit = torso recoil, KO =
fall back, victory = arms up), applied over each robot's **own rest pose**,
writing absolute local-rotation curves. Bone paths and rest values are read
from the robot's existing walk clip bindings + prefab hierarchy, so the bake
is per-robot automatically and never guesses names. Snappy 0.3–0.5 s robot
karate suits the toy aesthetic and proves the whole mode.

### Track B — the Meshy pass (probe now, fleet on approval)

`Tools/meshyfight.py`, in the meshyvehicles mould (`fight_tasks.json` state):

1. `rig <robot>` — POST /openapi/v1/rigging with the robot's geometry
   (rebuilt small, the meshyretexture trick) — ~5 credits. **No rig task ids
   survive from the original rigging, so this re-rig is unavoidable.**
2. `animate <robot> <move…>` — POST /openapi/v1/animations per action id,
   ~3 credits each.
3. `download <robot>` — GLBs into `Assets/Models/Meshy/Fight/{robot}-{move}.glb`
   (a subfolder, so the roster scan never sees them), then the material fix.

Move shortlist (action ids verified 2026-07-30): Combat Stance 89,
Kung Fu Punch 96, Roundhouse Kick 207, Flying Fist Kick 94, High Kick 215,
Block 138, Face Punch Reaction 174, Knock Down 187, Charged Spell Cast 125
(the PHOTON BLAST cast), Victory Cheer 59.

**Skeleton consistency rule:** fight clips must drive the *existing* forged
prefabs' skeletons. The re-rig may not reproduce them exactly — so the probe
(ranger: rig + punch + roundhouse + fly kick, ~14 credits) is downloaded and
its joint paths diffed against the current rig before any fleet spend. If the
new skeleton disagrees, the fallback is wholesale: refresh that robot's
`-rig/-walk/-run` from the new rig output too, re-run the material fix and
the walker forge, and everything shares one skeleton again.

`BrawlMoveForge` prefers `Fight/{robot}-{move}.glb` when present and falls
back to the template bake — Meshy clips slot in per robot as they land, with
no code change.

**Spend:** probe ~14 credits now (balance 1660). Fleet ≈ 9 robots × (5 rig +
10 moves × 3) ≈ **315 credits — awaits approval after the probe is eyeballed.**

---

## 5. Phases (one commit each)

0. **This plan.**
1. **meshyfight.py + ranger probe kicked off** — tasks cook server-side while
   the mode is built.
2. **Mode plumbing** — `GameMode.Brawl`; BRAWL button (menu tightens to
   100 px spacing to fit seven buttons); robot select routes Brawl straight
   to the fight (no arena select — the stage is its own set, like
   Commander's map); `BrawlController` Begin/Teardown with the Environment
   swap + hidden FPS cast; `BrawlStage`; `BrawlCamera`. Playable as: two
   idle robots face off on a lit stage.
3. **BrawlMoveForge Track A** — clips + controllers for all nine, batch
   `-executeMethod` run, robots now throw shapes.
4. **Combat core** — `BrawlFighter`, `BrawlMoveSet`, `BrawlInput`; P1 can
   walk, jump, punch, kick, fly-kick, block a dummy P2.
5. **Round flow + HUD** — `BrawlMatch`, `BrawlHud`; full best-of-3 with KO
   de-rez, victory pose, rematch screen.
6. **BrawlBrain** — the CPU fights back.
7. **Juice + special + touch** — hit-stop, sparks, camera shake, block
   flash, PHOTON BLAST, touch pad.
8. **Probe verification** — download, skeleton diff, re-forge ranger with
   real clips, report with fleet cost for approval.

Compile checks via the shell Roslyn loop between phases; Play-mode testing in
the editor at phase boundaries.
