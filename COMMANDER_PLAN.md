# Commander — a Red Alert–style RTS mode for Photon Arena

A fourth main-menu item, `COMMANDER`, opening a base-building real-time strategy
game played with the robots, weapons and fiction the arena modes already use.
Both **Player v AI** and **AI v AI** (spectator, for the auto-recorded videos).

Decisions taken 2026-07-29: full Red Alert economy loop; structures are built
from primitives first and given Meshy models once footprints are final; both
player-commanded and AI-v-AI matches.

---

## 1. What this rides on (already built)

Commander is less new code than it looks, because most of an RTS is already in
this project under a different name.

| Need | Existing system | Notes |
| --- | --- | --- |
| Unit models | 9 rigged walker prefabs in `Assets/Models/Generated/` | Animator + Speed blend tree + `RobotLocomotion` already drive walk/run from position delta |
| Vehicle units | `Assets/Models/Meshy/*-vehicle.glb` (9 of them) | titan = "heavy six-wheeled assault tank", scout = recon buggy, racer = fast attack. The RTS vehicle roster is **already paid for** |
| Pathing | `NavMeshSurface` on `Environment`, `CollectObjects.Children` | Unbounded by children, so a 180 m map bakes with no reconfiguration |
| Combat | `Weapon` base + 54 subclasses, `EnergyShield`, `WeaponUtil.DamageProp` | `DamageProp` is already the single hit path for non-robot targets — buildings plug straight in |
| Death fiction | `DeRezEffect` | Units and structures fold into light. Nothing dies; kid-friendly stays intact |
| Runtime world building | `ArenaKit`, `ArenaRuntime`, `ArenaMaterials` | The map is generated at Play, exactly like arenas |
| Teams & colour | `teamId` 0/1, `MatchAnnouncer.TeamColor`, `TeamPaint` | Cyan v magenta reads on camera |
| Robot picking | `RobotSelectMenu` | Reused verbatim to pick each side's army chassis |
| Map picking | `ArenaSelectMenu` | Same pattern, commander map list |
| Unit barks | `ChatterDirector` / `RadioLog` (this branch) | "Acknowledged", "Moving out", "Base under attack" — free voice for orders |
| Spectator camera | `SpectatorCamera` | Starting point for the AI-v-AI director |

**Consequence worth stating up front:** Commander is 100% runtime-generated, so
it needs **no `ArenaBuilder` rerun and no batch Unity build**. Code edits take
effect on Play alone. This is deliberate and should stay true — nothing in
Commander should require serializing into `GreyboxArena.unity`.

---

## 2. What is genuinely new

Base building, a resource economy, unit selection and orders, production queues,
a power grid, an AI commander, and an RTS HUD. Roughly 18 new files under
`Assets/Scripts/Commander/`, one class per file per project convention.

---

## 3. Design

### Scale and camera

- Battlefield **180 × 180 m**, flat ground at y = 0, with impassable rock ridges
  and a few chokepoints. Arenas are 32 m across; this is a different order of
  size, which is why Commander gets its own map builder rather than an
  `ArenaDefinition`.
- Camera pitched 55°, height 25 m (zoomed in) to 80 m (whole-base view). A 2 m
  robot reads clearly at 40 m — chunky, Red Alert–ish.
- Pan: WASD, screen-edge scroll, middle-drag. Zoom: wheel. Clamped to map
  bounds. Tagged `MainCamera` like the other rigs, and only one active at a time.

### Economy — "photon crystals"

Straight Red Alert, renamed to fit the fiction.

- **Crystal fields**: 8 clusters of ~12 glowing crystals, 25 credits each,
  placed to make map control matter (2 safe per base, 4 contested).
- **Collector** (harvester): drives to the nearest crystal in its assigned
  field, fills to 300 credits over ~8 s, returns to a Refinery dock, unloads.
  Auto-retargets when a field runs dry.
- **Power grid**: each structure produces or draws power. When draw exceeds
  supply, production speed scales by `supply / draw` and turrets fire slower —
  the classic reason to defend your power plants.

| Structure | Cost | Power | Footprint | Prereq |
| --- | --- | --- | --- | --- |
| Command Center | 2500 | +50 | 6×6 | — |
| Power Plant | 300 | +100 | 4×4 | Command Center |
| Refinery (ships 1 Collector) | 1400 | −30 | 6×4 + dock | Power Plant |
| Robot Factory | 1500 | −50 | 6×6 + exit lane | Refinery |
| Photon Turret | 600 | −40 | 2×2 | Power Plant |
| Tech Lab | 1200 | −60 | 4×4 | Factory |

| Unit | Cost | Chassis | Role |
| --- | --- | --- | --- |
| Ranger | 300 | ranger robot | basic infantry, Laser Blaster |
| Scout | 400 | scout buggy (vehicle) | fast recon, reveals map |
| Collector | 1000 | knight battle wagon (vehicle) | harvesting, unarmed |
| Panther | 500 | panther pursuit car | fast raider |
| Titan | 700 | titan tank (vehicle) | heavy armour, needs Tech Lab |

Placement rule: new structures must sit within 12 m of an existing friendly
structure (RA adjacency), on flat unoccupied ground.

### Units and orders

- Left-click select, drag a box for many, double-click selects all of that type
  on screen, `Ctrl+1..9` control groups.
- Right-click: move; on an enemy, attack; on a crystal, harvest (Collectors);
  `A`-click = attack-move. Idle units auto-engage anything in range.
- States: Idle / Move / AttackMove / Attack / Guard / Harvest / Return.

**Performance decision:** arena robots carry all 54 weapon components
(`WeaponCatalog.AttachAll`). Commander units get exactly **one** weapon
component each. 80 units × 54 components is not survivable; 80 × 1 is fine.
`CommanderUnit` builds its own lean rig from the roster model rather than
cloning an arena bot.

### AI commander

`CommanderAI` runs the same public API the player's HUD does — no cheating, no
private hooks. Build order → expand to a second refinery → tech up → attack
waves whose size scales with elapsed time and a difficulty setting. This is the
same class that drives **both** sides in AI v AI.

### Win condition

Destroy the enemy Command Center, or reduce them to zero structures. The loser's
base de-rezzes in sequence — a good closing shot for a video.

### Menu flow

```
Main menu → COMMANDER → [Player v AI | AI v AI]
          → robot select (army chassis per side, existing screen)
          → map select (existing screen pattern, commander maps)
          → match
ESC steps back one screen, as it already does elsewhere.
```

`MainMenu` currently lays out three buttons at y = 40 / −80 / −200; four means
re-spacing to 100 / −20 / −140 / −260.

---

## 4. Phases

Each phase ends playable and gets its own commit.

**Phase 0 — Get there.** `GameMode.Commander`, menu button, `CommanderController`
lifecycle (hide the FPS player, disable arena bots, build the commander root,
tear it all down on ESC). `CommanderMap` generates ground, ridges, crystal
fields and two base sites; NavMesh rebakes. `CommanderCamera`. *Playable result:
pick Commander, fly over a real battlefield, ESC back out cleanly.*

**Phase 1 — Command something.** `CommanderUnit`, `CommanderSelection`, selection
rings, orders, control groups. A dozen pre-placed robots per side that fight when
they meet, using the existing weapons and shields. *Playable result: a skirmish
you actually direct.*

**Phase 2 — Economy.** `CrystalField`, `Collector` harvest loop, `CommanderEconomy`
(credits + power per team), credits readout. *Playable result: numbers go up when
you protect your collectors.*

**Phase 3 — Build a base.** `BuildingDefinition` / `BuildingCatalog` / `Building`,
`BuildPlacer` with ghost preview, validity tint and the adjacency rule.
Structures materialize on a rise-and-scanline (reusing the de-rez shader idea),
take damage through `WeaponUtil.DamageProp`, and de-rez when destroyed. Power
grid live. *Playable result: a base you laid out yourself.*

**Phase 4 — Production and tech.** `ProductionQueue` with rally points, unit
buttons gated by prereqs, low-power slowdown. *Playable result: the full RTS
loop — harvest, build, produce, attack.*

**Phase 5 — Opponents and endings.** `CommanderAI`, win/lose, AI v AI with
`CommanderDirector` (interest-based camera that follows the biggest fight and
cuts to base attacks). *Playable result: a complete match, and a watchable one.*

**Phase 6 — Polish.** Minimap with blips and click-to-jump, hotkeys, touch
controls (tap-select / tap-move / pinch-zoom — the tablet is a target), unit
barks through `ChatterDirector`, then the Meshy art pass for the six structures
(~270 credits of 760, previews for approval before any refine).

---

## 5. Risks and known traps

1. **Weapon-component blowup** — the single biggest perf risk; addressed by lean
   units in Phase 1, not retrofitted later.
2. **Agent count** — 80 `NavMeshAgent`s is comfortable; avoidance quality set to
   medium and repath rate throttled from the start.
3. **Hot reload during Play wipes plain-C# state** — `CommanderEconomy` and the
   AI's build order must re-derive rather than assume, the way `AIBrain._weights`
   now does.
4. **Bloom whiteout** — structure glow strips stay ≤ 1.8; anything hotter turns
   white ([[vfx-bloom-whiteout-rule]]).
5. **Camera tag collisions** — exactly one `MainCamera`-tagged rig active, or
   `FlashQuad` billboarding breaks.
6. **WebGL size** — blocks cost nothing; Meshy structure textures get run through
   `Tools/shrink_glb_textures.py` before they land.
7. **Don't reserialize the scene** — if a change starts wanting scene data, find
   another way. The moment Commander needs an `ArenaBuilder` rerun, every edit
   costs an editor close and a batch build.
