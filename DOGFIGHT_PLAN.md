# DOGFIGHT

Robots that fold into tanks can fold into the sky instead. Two jets circle a
navy void over a glowing ground grid, guns tied to their noses: get behind the
other one and hold the trigger. First to five wrecks takes the sortie. One card
launches it with the player in the cyan cockpit; the AI WAR card seats two
CPU pilots and hands the couch a broadcast camera instead of a stick.

Its ancestors are TANK RAID (a mode that owns its world, a pawn driven through
a `Drive`/`Firing` seam that makes the player and the AI the same code path)
and the TRANSFORM feature (the stop-motion stages, now generated a second time
against `*_Jet_Transformation.mp4` clips). Nothing about either had to change —
the jet stages are a sibling folder the same tooling fills.

## Decisions taken

| decision | choice | why |
|---|---|---|
| Flight model | kinematic arcade: always moving, steer pitch/yaw, bank is visual | it must be flyable by an eight-year-old with a mouse; stalls and rudders are simulator problems |
| Player steering | jet chases the cursor; WASD/arrows add on top | "point where you want to go" is the one flight control that needs no tutorial; sticks-add-don't-replace is the TANK RAID rule |
| Guns | one nose blaster per jet, team-coloured bolts, soft aim assist inside a 6° cone | whose shot is that must read at a glance (the TANK RAID rule); the assist is how kid-aim lands hits without an aimbot |
| Match | kill race to 5, respawn after a beat with spawn grace | rounds with intermissions kill the flow a dogfight is; the race keeps both jets airborne |
| The world | its own set: navy void, glowing ground far below, spire and cloud props | the transformation clips were shot on a navy void; the mode looks like where the jets were born |
| Jet stages | `Assets/Models/Stages/<robot>-jet/stage*.glb`, roster fields `jetStages`/`jetVideo` | the `<robot>-jet` key falls through meshyimageto3d.py, make_reverse_clips.py and the roster scan with no special cases |
| Robots without jet stages | fly the ranger airframe in their team paint | one robot has been through the jet pipeline so far; a mode that only ranger could enter would hide from the menu |
| Orientation | stages turned by a set-wide constant, not VehicleSkin's long-axis rule | a delta wing is wider than it is long, so "longest axis forward" faces it sideways by construction |

## What this rides on (already built)

| need | existing system |
|---|---|
| world swap in/out | TANK RAID's setup/teardown: hide cast, dark the environment, stage root, `ArenaRuntime.Load` on the way out |
| shootable jets | scene-root pawns + `EnergyShield` + capsule on Default; bolts resolve `hit.transform.root` |
| gun | `LaserBlaster` re-tuned, exactly as TankArsenal's cannon is |
| team read | `MatchAnnouncer.TeamColor`, `TeamPaint.Apply` with the robot's anchor hue |
| stop-motion intro | the stage GLBs themselves; swap timing vocabulary from `TransformMode` (burst at 42%) |
| HUD | runtime uGUI, `LegacyRuntime.ttf`, scaled-rect bars (fillAmount is a lie without a sprite), sortingOrder 17 |
| explosions, bursts, reticle ring | `VfxUtil` |

## The rules

- **The pawns are not under the stage root.** Bolts resolve through
  `transform.root`; a jet parented to the stage is a jet nothing can shoot.
- **Constants are expressed against each other.** The camera publishes its
  reach; spawn rings, the fly volume and the prop field derive from it and from
  each other, never hand-picked twice.
- **Whatever drives, the pawn cannot tell.** The player writes
  `Steer`/`Throttle`/`Firing`; `JetBrain` writes the same three and nothing else.
- **No coroutines in match flow.** A state enum and float timers, as
  `BrawlMatch` does — a mid-Play recompile must not eat the referee.
- **Soft walls.** The fly volume steers you back; it never stops you. A wall
  you can hit is a wall a kid will hit all day.

## The cast

|  | shield | regen | speed min/cruise/boost | turn | gun |
|---|---|---|---|---|---|
| both jets | 100 | 8/s after 4s | 16 / 26 / 40 m/s | 80°/s pitch, 70°/s yaw | 12 dmg · 6/s · 90 m/s bolts |

Symmetric on purpose: the player's edge is the aim assist and the AI's honest
reaction delay (0.25 s) and aim jitter (2.5°), not a bigger number. First to 5;
respawn 2.5 s later on the spawn ring with 2 s of grace, facing the fight.

## Files

`Assets/Scripts/Dogfight/`

- `Dogfight.cs` — the mode: begin/teardown, world + cast, match state machine
  (intro → transform → fight → over), player input, score.
- `JetPawn.cs` — one jet at the scene root: stage build + fit, shield, gun,
  flight integration, soft-volume assists, wreck and respawn. The registry.
- `JetBrain.cs` — the CPU pilot: pursue, lead, fire, break off when tailed.
- `DogfightSky.cs` — the set: ground glow, spires, clouds, suns, fly volume.
- `DogfightCamera.cs` — chase/cockpit rig for one subject; the AI WAR
  broadcast adds auto-cuts between jets.
- `DogfightHud.cs` — shields, score pips, speed, reticle, banners, end panel.

## Touched elsewhere

`GameMode.Dogfight`/`DogfightWar` + launcher/teardown in `GameModeController`;
two cards in `MainMenu` (the deck band re-pitched from seven card columns to
eight, 224 → 194 px, so WAR ROOM can deal six); subtitle arms in
`RobotSelectMenu`; `jetStages`/`jetVideo` on `RobotRoster.Entry` filled by
`ArenaBuilder.LoadJetStages`; jet texture prompt in `Tools/meshyimageto3d.py`.

## Content pipeline (per robot, ~240 credits)

```
python Tools/meshyimageto3d.py <robot>-jet submit TransformerTest/02_frames/<robot>-jet/*.png
python Tools/meshyimageto3d.py <robot>-jet status && python Tools/meshyimageto3d.py <robot>-jet download
python Tools/shrink_glb_textures.py --root Assets/Models/Stages/<robot>-jet
python Tools/previewglb.py TransformerTest/<ROBOT>JET Assets/Models/Stages/<robot>-jet/stage*.glb
cp ExternalData/<Robot>_Jet_Transformation.mp4 Assets/Video/<robot>-jet-transform.mp4
python Tools/make_reverse_clips.py <robot>-jet
```

Frames are picked where the silhouette changes (ranger-jet: 0/22/30/38/46/54/66/90
of 121 — the fold is done by ~72), cropped 640 → 560 to shed the Ai watermark,
and never taken from a frame with a flash in it. Ranger's jet run: 2026-08-03,
240 credits, all eight stages accepted first try.

After download, run `python Tools/stageorient.py` (point it at the new set):
Meshy flips its canonical front mid-set where the frames stop reading as a
creature, and JetPawn carries that split as a per-set robot-framed stage
count (ranger: jet 5, tank 4). If the new set's flip lands elsewhere, update
the constant — and settle any yaw SIGN doubt with one in-game screenshot,
never an offline probe alone.
