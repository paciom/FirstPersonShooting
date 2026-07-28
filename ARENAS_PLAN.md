# Plan: Ten Arenas

Add 10 new arenas, drastically different from each other and from the current
one — different colour schemes, different layouts, several genuinely
multi-level, and content that goes well past "neon boxes on a ship deck"
(alien plants, lava catwalks, a giant's desk, a wireframe void).

The existing ship-deck arena stays as **HANGAR**, so the game ships with **11
selectable arenas**.

## Decisions taken

| Decision | Choice | Why |
|---|---|---|
| Storage | **Runtime rebuild, one scene** | `ArenaRandomizer` already demolishes cover and re-bakes the NavMesh live, so the pattern is proven here. Avoids duplicating the player/bot/GameController rig across 10 scenes. |
| Art | **Procedural only** | Everything from Unity primitives + shaders. No new GLBs, no LFS growth, no Meshy credits, and the ~350 MB web build does not move. |

The runtime choice buys one thing that matters more than either: **arena layout
code iterates on Play alone.** No `Build Greybox Arena` re-run, so the project's
worst workflow trap — editing code and playing a stale saved scene — cannot
bite while building arenas.

---

## 1. What is hardcoded today

The arena is not data. It is spread across six files as literals:

| File | Hardcoded |
|---|---|
| [ArenaBuilder.cs](Assets/Editor/ArenaBuilder.cs) | 40×40 floor, walls at ±20, 4 pillars, 40 cover blocks, lighting rig, player at `(0, 0.1, -15)`, 6 bot spawns |
| [ArenaBlockManager.cs:23](Assets/Scripts/ArenaBlockManager.cs:23) | `halfExtent = 14`, 9 `SpawnZones` |
| [ArenaRandomizer.cs:19](Assets/Scripts/ArenaRandomizer.cs:19) | 17 `KeepOut` points, `Range(-14, 14)` |
| [TreasureSpawner.cs:135](Assets/Scripts/Treasures/TreasureSpawner.cs:135) | `arenaExtent` |
| [RobotReinforcements.cs:97](Assets/Scripts/Treasures/RobotReinforcements.cs:97) | reinforcement line at `z = ±16` |
| [GameModeController.cs:324](Assets/Scripts/GameModeController.cs:324) | spectator rig at `(0, 8, -14)`, fly cam at `(0, 14, -22)` |

Every one of these has to become a read off the **active** arena.

### What already works, unchanged

Verified while reading, so the plan does not budget for it:

- `AIBrain` is pure `NavMeshAgent` + 3D `Physics.Raycast` line-of-sight. It has
  no flat-ground assumption; the `flat.y = 0f` lines are horizontal steering
  only. **Multi-level needs no AI changes.**
- `DeRezEffect` captures `_spawnPosition` at `Awake` and teleports there on
  re-materialize — so respawn follows wherever the builder puts a character,
  *provided we re-seed it on arena change* (see §3 gotchas).
- `CharacterMotor` already has a "gravity replaced by gentle upward drift" flag
  (Moonboots). REEF's bubble columns get low-gravity for free.
- `EffectZone` does timed spherical damage/slow zones. FOUNDRY's lava and
  BIODOME's spore mist are configured zones, not new systems.
- `WeaponUtil.DamageProp` is the single route for making any non-robot
  shootable. Destructible crystals/pods route through it.

---

## 2. The seam

```
Assets/Scripts/Arenas/
  ArenaDefinition.cs      abstract: name, palette, bounds, spawns, Build()
  ArenaKit.cs             runtime geometry helpers (Box, Ramp, Stair, Platform,
                          Cylinder, Arch, Tube, Stalk, Spire, Trim, Light, Link)
  ArenaMaterials.cs       runtime material factory — Shader.Find, no AssetDatabase
  ArenaPalette.cs         base/accentA/accentB/emissive/fog/ambient/bloom
  ArenaContext.cs         the ACTIVE arena's data; single source of truth
  ArenaRuntime.cs         demolish → build → atmosphere → bake → cover → respawn
  ArenaLibrary.cs         the 11 definitions, index → definition
  Definitions/            one file per arena
```

`ArenaDefinition` shape:

```csharp
public abstract class ArenaDefinition
{
    public abstract string DisplayName { get; }   // "BIODOME"
    public abstract string Tagline     { get; }   // one line for the select card
    public abstract ArenaPalette Palette { get; }

    public virtual Vector2 HalfExtent      => new Vector2(20f, 20f);  // interior
    public virtual float   CoverHalfExtent => 14f;   // where blocks may churn
    public virtual float   GroundY         => 0f;    // dynamic cover's plane

    public abstract Vector3[] TeamSpawns(int teamId);      // 4 per team
    public virtual  Vector3   PlayerSpawn => TeamSpawns(0)[0];
    public virtual  Vector3   ReinforcementLine(int teamId) => ...;
    public virtual  Vector3[] DropPlanes => new[]{ Vector3.zero }; // treasure y-levels
    public virtual  Vector3   SpectatorPerch => new Vector3(0, 8, -14);

    public abstract void Build(Transform root, ArenaKit kit);   // the geometry
    public virtual  CoverSpec[]  Cover(System.Random rng)  => ...;
    public virtual  void  ApplyAtmosphere();   // fog, ambient, lights, bloom
    public virtual  LinkSpec[]   NavLinks => none;   // multi-level connections
}
```

`ArenaBuilder` stops generating arena geometry. It calls
`ArenaRuntime.Load(0)` once at edit time so the saved scene still opens on
HANGAR, and keeps everything else it does (URP, shaders, model repair, robot
forges, characters, roster).

**Materials become runtime.** `MakeSciFiMaterial` / `MakeLitMaterial` for
*arena surfaces* move into `ArenaMaterials` as `new Material(Shader.Find(...))`
— the pattern `VfxUtil` already uses. Character materials stay as `.mat`
assets. Every shader an arena touches must be in `EnsureShadersIncluded`, or a
player build strips it and `new Material(null)` throws mid-load — the exact
failure mode already documented in `ArenaBuilder`.

---

## 3. Runtime rebuild — and the five things that will break

`ArenaRuntime.Load(index)`, called from the arena-select screen before a match:

1. Guard: only from Menu / ArenaSelect / ArenaPreview, never mid-match.
2. Destroy all children of `Environment`.
3. `def.Build(root, kit)` — geometry, with colliders.
4. `def.ApplyAtmosphere()` — `RenderSettings` fog + ambient + skybox, rebuild
   the light rig, override bloom intensity on the volume profile.
5. `surface.BuildNavMesh()`.
6. Spawn `ArenaBlock` cover **after** the bake, so obstacles carve (same order
   as today).
7. Add `NavMeshLink`s for level connections.
8. Reposition player + 6 bots onto the new spawns.

Known breakages, each needing a small explicit fix:

- **`ArenaBlockManager._blocks` is cached in `Start()`** and subscribes to
  `OnShattered` there. After a rebuild it holds destroyed blocks and the new
  ones are unmanaged. → add `Rescan()`, unsubscribing first.
- **`DeRezEffect._spawnPosition` is captured in `Awake`.** Without a re-seed,
  every character de-rezzes and re-materializes at the *previous* arena's
  coordinates — likely inside a wall. → add `SetSpawn(Vector3)`, called during
  reposition.
- **Dynamic cover assumes an open plane.** `ArenaBlockManager.RandomSpot`
  picks any `x/z` in `halfExtent` at `y = 0` and would slide a block under a
  platform or off a floating island. → churn is confined to a per-arena
  `CoverHalfExtent` + `GroundY`, and arenas where it makes no sense (GRIDSPACE)
  set cover count to zero and use static geometry.
- **`TreasureSpawner.TryPickLandingSpot` samples the NavMesh from `y = 0`.** On
  a multi-level arena that only ever finds the ground floor, so upper floors
  never get loot and the vertical space goes dead. → sample from `DropPlanes`.
- **`GameModeController._deRezEffects` is cached in `Start()`.** Fine as long
  as rebuilds do not create characters — and they do not. Worth an assert.

---

## 4. Multi-level support

Five arenas are genuinely multi-level, so this is a work item, not a side
effect. The constraints that decide whether bots can use a level:

| Constraint | Value | Consequence |
|---|---|---|
| `NavMeshAgent.radius` | 0.4 | ramps ≥ **1.2 m** wide or the bake drops them |
| `NavMeshAgent.height` | 2.0 | ≥ **2.2 m** headroom under any platform |
| Bake max slope | 45° (default) | ramps ≤ **40°** for margin |
| `CharacterController.stepOffset` | 0.3 (default) | stairs ≤ 0.3 rise, **or** raise to 0.45 |

`ArenaKit` provides `Ramp()` and `Stair()` that take rise/run and *refuse to
emit geometry outside those limits* — the cheapest way to stop building a
staircase bots cannot climb. Drops between levels get `NavMeshLink` so bots
will jump down rather than treat a rooftop as a dead end.

Verification per multi-level arena: enter Arena Builder, cycle to it, watch
whether bots reach the top floor. A rooftop no bot ever visits is a bake
failure, not a design choice.

---

## 5. The eleven arenas

**Hard constraint (2026-07-28): exactly ONE arena is neon.** That is HANGAR.
Every other arena must be built from a different material family — stone, brick,
iron, wood, moss, crystal — and none of them may use `PA_SciFiPanel` or a
glowing-seam trim. Where an arena emits light it must be a lit *object* (lava,
a seed pod, a crystal) rather than strip lighting, or the set slides back into
looking like one arena in different colours.

GRIDSPACE (black void, neon wireframe, floating platforms) was **cut** for this
reason — its entire identity was neon, so it could not be restyled, only
replaced. BIODOME and CRYSTAL HOLLOW took its slot.

Palettes respect the project's bloom rule: emissive intensity stays ≲ 2.5 or
the colour washes to white. Each arena overrides bloom intensity to suit.

| # | Name | Palette | Layout | Distinct thing |
|---|---|---|---|---|
| 0 | **HANGAR** | gunmetal / cyan+magenta | flat, churning cover | *existing arena, unchanged* |
| 1 | **BIODOME** | acid lime + deep violet, warm haze | mounded ground, raised root-bridge ring at 3 m | Giant swaying alien plants. Bulb-pods are the destructible cover and burst into spore `EffectZone`s. |
| 2 | **SKYLINE** | sunset orange + teal glass | **3 levels** — 5 buildings with interior floors, ramps, rooftop plank bridges over a street canyon | Real verticality; the street below is playable, so a fall costs position, not the round. |
| 3 | **CRYOVAULT** | pale cyan + white + navy | glacier floor split by a chasm ring, mid ledges via ice ramps | Translucent ice you can see enemies through but not shoot through. Thick ground fog. |
| 4 | **FOUNDRY** | black basalt + orange/red | **2 catwalk levels** over a lava lake, ground islands between pours | Stepping off is a lava `EffectZone` — shield drain + knockback, never an instant loss. |
| ~~5~~ | ~~**GRIDSPACE**~~ | — | — | **Cut.** Neon was its whole identity, and only one arena may be neon. Replaced by BIODOME + CRYSTAL HOLLOW. |
| 6 | **REEF** | aqua + coral pink, caustics | half-flooded station, glass tunnels, coral cover | Bubble columns flip `CharacterMotor` to the existing low-gravity drift — a free vertical mechanic. |
| 7 | **ZIGGURAT** | sand tan + turquoise glyphs, hard sun | **3 tiers** with real interior chambers and exterior stairs | Interiors: enclosed rooms make the sightline game completely unlike the open arenas. |
| 8 | **CRYSTAL HOLLOW** | magenta/violet crystal on dark rock | low-ceilinged cavern, huge spires | Crystals light up when shot and shatter into shards; the cave gets brighter as the round wears on. |
| 9 | **TOYBOX** | flat primary colours, bright | a giant's desk — **2 levels** via a ruler ramp onto a book stack | Robots are tiny. Cover is a mug, pencils, dice, eraser blocks. Tone break, pure kid appeal, all primitives. |
| 10 | **ORBITAL** | white/steel + hard sun, black starfield | ring corridor with glass floor panels, docking clamps, **low-gravity centre** | Procedural starfield skybox and a planet below the floor. |

Split: 5 clearly multi-level (2, 4, 5, 7, 9), 2 with ledges (1, 3), 3 mostly
ground (6, 8, 10).

Names and concepts are the proposal — cheap to swap before §7 starts, since
each is one self-contained file.

## 6. New shaders

Procedural-only means the shaders carry the visual difference.

**Correction (2026-07-28):** extending `PA_SciFiPanel` with more pattern modes
was the wrong call and has been abandoned. That shader draws panel grooves *and*
applies its glowing seam unconditionally in every mode, so anything built from
it reads as "neon lines" whatever colours and mode you feed it. Stone and brick
are not reachable from it at all.

Replaced by **`PA_Surface`** — a triplanar shader carrying genuine material
archetypes: `Hull` (spaceship plating, the only one with lit seams), `Stone`,
`Brick`, `Tread`, `Organic`, `Crystal`, `Strata`, `Plank`. Three things do the
differentiating: a per-style procedural pattern, cavity shading so recesses
genuinely darken, and derivative-based bump so the height field perturbs the
normal without UVs or tangents. Roughness drives the specular lobe, which is
most of why dry sandstone and moulded plastic do not read the same in identical
light. `PA_SciFiPanel` stays for HANGAR and GRIDSPACE, where neon *is* the
intended style.
- **`PA_Foliage`** — translucent gradient + emissive veins + vertex wind sway.
  Serves BIODOME plants and REEF coral.
- **`PA_Crystal`** — fresnel rim, inner glow, fake refraction. Serves CRYOVAULT
  ice and CRYSTAL HOLLOW spires.
- **`PA_Grid`** — wireframe/holo grid with a scan line. Serves GRIDSPACE and
  every light bridge.
- **`PA_Starfield`** — procedural skybox. Serves ORBITAL, and GRIDSPACE's void.

All five go into `EnsureShadersIncluded`.

## 7. Arena selection

**Flow: menu → robot select → arena select → match.** Both AI modes get the
arena step; it is not optional and not skippable per mode.

```
MainMenu "AI v AI"      ─┐
MainMenu "PLAYER v AI"  ─┴─> OpenRobotSelect(mode)
                              └─> RobotSelectMenu     [Esc → main menu]
                                    └─ LaunchSelectedMatch(cyan, magenta)
                                         ├─ ApplyRobotSelection()
                                         └─> ArenaSelectMenu   [Esc → robot select]
                                               └─ LaunchArena(index)
                                                    ├─ ArenaRuntime.Load(index)
                                                    └─> StartAIvAI / StartPlayerVsAI
```

### The change in `GameModeController`

Both modes already funnel through one method, so this is a single split.
[LaunchSelectedMatch:224](Assets/Scripts/GameModeController.cs:224) today does
*close robot select → apply robots → start match*. It becomes:

```csharp
public void LaunchSelectedMatch(int cyanIndex, int magentaIndex)
{
    _cyanRobot = cyanIndex;
    _magentaRobot = magentaIndex;
    CloseRobotSelect();
    ApplyRobotSelection();
    OpenArenaSelect();              // was: Start{AIvAI,PlayerVsAI}()
}

public void LaunchArena(int arenaIndex)
{
    CloseArenaSelect();
    ArenaRuntime.Load(arenaIndex);  // geometry + bake + respawns, before the match
    if (_pendingMode == GameMode.AIvAI) StartAIvAI(); else StartPlayerVsAI();
}

public void CancelArenaSelect()     // Esc goes BACK one step, not to the main menu
{
    CloseArenaSelect();
    _robotSelect = RobotSelectMenu.Build(this, _roster, _pendingMode,
                                         _cyanRobot, _magentaRobot);
}
```

`_pendingMode` already survives across the robot screen, so it carries the
arena screen too with no new state. `EnterMenu` must also `CloseArenaSelect()`
alongside its existing `CloseRobotSelect()`, or Esc from the arena screen
leaves an orphaned canvas over the main menu.

Ordering that matters: **`ArenaRuntime.Load` runs before `Start*`**, never
after. `StartPlayerVsAI` positions and enables characters; loading an arena
after it would rebake the NavMesh under live agents and strand them.

### The screen

- Modelled on `RobotSelectMenu`'s card layout, which already auto-shrinks card
  spacing (`min(210, 1800/n)`) to fit any roster size — the same trick fits 11
  arenas without a scroll view.
- Each card: name, one-line tagline, palette swatch strip, and a "3 LEVELS"
  badge where the arena is multi-level.
- Overhead preview PNG per arena, from an extended `PreviewCaptureTool`.
- A **RANDOM** card, and an AI-v-AI **rotation** toggle so auto-recorded videos
  are not 40 minutes of the same room.
- Esc goes back to robot select. Touch input works — `TouchControls.Active`
  already drives the hint text.

### Fallbacks

`OpenRobotSelect` already falls straight through to the match when no roster
exists. `OpenArenaSelect` mirrors that: with fewer than two arenas registered
it loads index 0 and starts immediately, so the flow is never a one-card
screen. This is what makes the step safe to land before all 11 arenas exist.

### Live cycling

Arena Builder mode gains `[` / `]` to cycle arenas without going through the
menus. This is the iteration loop for the whole feature — build, Play, cycle,
look — and it is how the arenas get verified in §8.

---

## 8. Sequencing

One commit per step — a big uncommitted diff in this repo reads as
unrecoverable damage even when nothing is lost.

| Step | Work | Done when |
|---|---|---|
| **P0** | `ArenaKit` + `ArenaMaterials` + `ArenaPalette`; port the current arena to `HangarArena : ArenaDefinition`, built at runtime; `ArenaBuilder` calls it | Play looks identical to today; `SceneAudit.Run` object counts match |
| **P1** | `ArenaContext`; de-hardcode all six consumer files; `DeRezEffect.SetSpawn`, `ArenaBlockManager.Rescan` | Still one arena, still identical |
| **P2** | `ArenaRuntime.Load` — demolish/build/atmosphere/rebake/respawn; `[`/`]` cycling; one throwaway second arena to prove it | Cycling between two arenas mid-session works, bots path, respawns land correctly |
| **P3** | **Arena-select screen** + the `GameModeController` flow split (§7), with the under-two-arenas fallback | Menu → robot → arena → match works end to end, in **both** AI modes; Esc steps back correctly |
| **P4** | Multi-level kit: `Ramp`/`Stair`/`Platform` with enforced limits, `NavMeshLink`, `DropPlanes`, `stepOffset` bump. Build **SKYLINE** as the proof | Bots reliably reach and fight on the top floor of SKYLINE |
| **P5** | Shaders: `PA_Foliage`, `PA_Crystal`, `PA_Grid`, `PA_Starfield`, SciFiPanel modes 4–6 | Each renders on a test quad without pink |
| **P6–P14** | The remaining 9 arenas, **one commit each**, in order: GRIDSPACE, BIODOME, FOUNDRY, CRYSTAL HOLLOW, CRYOVAULT, ZIGGURAT, TOYBOX, REEF, ORBITAL | Each: bots path everywhere, treasures reachable, no bloom whiteout |
| **P15** | Select-screen polish: preview captures, RANDOM card, AI-v-AI rotation | Cards show real overhead art; rotation varies recorded matches |
| **P16** | Balance + perf pass: rebake cost, WebGL frame time, cover density per arena | Rebake under budget on WebGL; every arena playable |

**The select screen moved from last to P3.** It is a required step in the match
flow, not a finishing touch — so it lands as soon as `ArenaRuntime.Load` exists
and there is a second arena to pick. Its list then grows by one card per arena
commit, which also means every arena from P6 on is exercised through the real
flow the moment it lands, rather than only through the `[`/`]` debug path.
Preview art and the RANDOM/rotation extras stay late (P15) — they are polish on
a screen that already works.

GRIDSPACE goes first among the remainder deliberately: it is the furthest from
the current arena, so it flushes out any lingering assumption that an arena is
a walled box with a floor at `y = 0`.

## 9. Risks

- **Runtime NavMesh bake cost on WebGL.** Single-threaded; a multi-level 40×40
  arena could take 100–500 ms. Mitigation: bake once per *arena load*, behind
  the select screen, never per round. **Measure this in P2** — if it is bad,
  that is the moment to reconsider baked scenes, not after nine arenas exist.
- **Bots refusing a level.** Most likely failure mode. The kit's enforced
  ramp/headroom limits are the guard; per-arena verification is the check.
- **Cover churn on exotic layouts.** Confined per arena, and off where it makes
  no sense.
- **Bloom whiteout** on the brighter palettes (TOYBOX, ORBITAL). Per-arena
  bloom override plus the ≲2.5 emissive rule.
- **Verification throughput.** 11 arenas × "does it play" is the real cost.
  Mitigations: `[`/`]` cycling means no rebuild per look; the standalone Roslyn
  compile-check catches C# errors without closing your editor; batch rebuilds
  and preview captures still need you to run the menu item.
- **Build size**: procedural means effectively zero growth. Not a risk.

## 10. Not in scope

- Per-arena music or ambience.
- Arena-specific game rules beyond the hazard zones described.
- Retiring `ArenaRandomizer` — it keeps working, now scoped to the active
  arena's bounds.
