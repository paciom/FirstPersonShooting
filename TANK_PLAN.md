# TANK RAID — the vertical scroller

A twin-stick, top-down, endlessly scrolling tank battle. The hero drives up a
battlefield that never ends; enemy tanks and robots come down it. Left stick
drives the hull, right stick turns the turret, and the guns never stop.

Its ancestors in this project are CHINESE RUN (an endless recycled field, a
camera that follows z hard and x softly, a mode that owns its own world and
hands the arena back on the way out) and the TRANSFORM feature (`TankTurret`,
and the `stage8` tank meshes that `Tools/tankturret.py` cut into
Hull + TurretPivot + TurretMuzzle). Nothing about either had to change.

## The rules

* **Two thumbs, no trigger.** The left stick is a world-space heading: the hull
  turns onto it and drives, so a tank that is pointing the wrong way has to come
  about before it goes anywhere. The right stick is where the gun points, and it
  is independent of where the tank is going — that separation IS the mode. The
  trigger is automatic, because a five-year-old should not have to hold three
  things down at once.
* **The screen scrolls whether or not you do.** A frontier line creeps up the
  field at 2.5 m/s and never retreats; the hero is clamped to the strip behind
  it. Dawdle and the screen pushes you forward. It is the one piece of pressure
  the mode has, and it is what makes it a *scrolling* game rather than a field
  with tanks on it.
* **Distance is the score.** Wrecks are the multiplier.
* **Three lives.** De-rez, a beat, and back on the field with two seconds of
  grace — an arcade continue, not a defeat screen.

## The cast

| | chassis | shield | what it does |
|---|---|---|---|
| Hero | the player's picked robot's tank | 220 | drives, turret tracks the stick |
| Raider tank | roster tanks, magenta | 95 | closes to 18 m, strafes, turret tracks |
| Raider robot | roster walkers, magenta | 55 | faster, lighter, faces you and shoots |

Everything is one `TankPawn` at scene root — the shootable contract in this
project is `hit.transform.root.GetComponent<EnergyShield>()`, so a pawn parented
under a stage root is a pawn nothing can hit.

## The drops

A wreck leaves a **WEAPON POD** or a **REPAIR KIT** just under half the time.
The pod grants one gun from a curated twelve for 20 seconds through the existing
`WeaponLoadout` — the same component, timer and drop-on-de-rez the arena's
airdrops use. The kit's odds rise as the hero's shield falls, so a bad run gets
help and a good one does not.

## Files

`Assets/Scripts/Tanks/`

* `TankRaid.cs` — the mode: builds the field, casts the hero, runs the waves, ends the run.
* `TankPawn.cs` — one tank or one walker at scene root: model, shield, guns, drive, aim.
* `TankBrain.cs` — what a raider does: close, hold range, strafe, shoot.
* `TankField.cs` — the battlefield that never ends (recycled bands, seeded scenery).
* `TankRaidCamera.cs` — high and behind, z hard and x soft.
* `TankRaidHud.cs` — shield, lives, distance, wrecks, the gun and its clock.
* `TankSticks.cs` — the two on-screen sticks, raw touches (uGUI can't do two thumbs).
* `TankArsenal.cs` — the twelve guns a pod can hand over.
* `TankPickup.cs` — a pod or a kit on the ground, collected by proximity.

Touched elsewhere: `GameMode.TankRaid` + launcher/teardown in `GameModeController`,
a 13th menu icon in `MainMenu` + `MenuIconRigs`, and one honest new field on
`EnergyShield` (`invulnerable`) for the respawn grace.
