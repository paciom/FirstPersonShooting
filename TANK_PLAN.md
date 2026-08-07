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

One tank against an army, and the arithmetic says so. The hero out-shields the
heaviest raider six to one and out-guns it by more than ten: its cannon
one-shots a walker and two-shots a raider tank, while a raider that catches the
hero alone simply loses. Only a crossfire is a threat. Difficulty is therefore
NUMBERS — the pressure cap climbs with distance — and never a raider that is
individually a match for the player.

| | chassis | shield | gun | what it does |
|---|---|---|---|---|
| Hero | the player's picked robot's tank | 600 (+22/s) | 60 × 3.4/s | drives, turret tracks the stick |
| Recruit | roster tanks, cyan | 190 | 30 × 2.2/s | leashed to the hero, picks its own targets |
| Raider tank | roster tanks, magenta | 110 | 13 × 1.5/s | closes to 19 m, strafes, turret tracks |
| Raider robot | roster walkers, magenta | 50 | 7 × 2.2/s | faster, lighter, faces you and shoots |
| Outpost | a Commander building | 650–1400 | 42 × 0.85/s | bolted down, fires all round, holds a boon |

Bolts are coloured by TEAM rather than by weapon, unlike the arena's guns: with
a dozen tanks firing down a scrolling field, "whose shot is that" has to be
answerable at a glance.

Everything is one `TankPawn` at scene root — the shootable contract in this
project is `hit.transform.root.GetComponent<EnergyShield>()`, so a pawn parented
under a stage root is a pawn nothing can hit.

## Who drives

One home-screen card, two modes. **WHO DRIVES — YOU / AI v AI** is a pair of
chips on the select screen, remembered between runs (`TankRaidPick`). The home
screen is the one surface in this game where space is genuinely scarce, and a
second tile would spend a slot saying what two chips say on a screen that is
already asking questions about the run.

In AI v AI, `TankPilot` takes the controls. It is deliberately not the raider
brain: a raider's job is to hold a range against one enemy, the hero's is to get
UP THE FIELD, and a hero driven by the raider brain would circle the first tank
it met until the frontier pushed it off the bottom of the screen. So it drives
forward and lets four pulls argue — pickups, crowding, the emptier lane, and the
fence — while the turret stays locked on the nearest enemy. A hull going one way
with its gun the other is the clearest advertisement the two sticks could have.

## The outposts

Every few hundred metres a **structure** comes over the horizon: one of
Commander's six buildings, off to one side of the strip, with a heavy slow gun
and a great deal of shield. It is the only thing in the mode that waits to be
picked — everything else is coming at you and can be driven past.

Knocking one down is the run's PROGRESSION, and it is kept for the rest of the
run. Each is a different kind of stronger rather than a bigger number on one
axis, and each reads off its own model: the power plant gives you its reactor
(+200 shield), the turret its gun (+45% damage), the tech lab its uplink (+40%
fire rate), the refinery its coolant (faster recharge), the factory its robots
(two recruits and room for more). Past 1600 m a **command center** can appear,
which is all of it at once behind 1400 shield.

Boons are stored as factors and re-applied from the BUILD values on every
capture and every continue (`TankBoons`), never multiplied in place — a mode
with three lives would otherwise hand out three reactors for one outpost.

## The drops

A wreck leaves a **WEAPON POD**, a **REPAIR KIT** or a **RECRUIT BEACON** just
under half the time. A beacon turns one tank to your side: it drives with you,
fights on its own initiative and is leashed to the hero so it cannot be led off
the bottom of the screen by a raider that keeps retreating. Three at once, more
once a factory has been taken. Beacons stop dropping while the escort is full —
a pickup that does nothing is worse than no pickup, because the player drove
across the field for it.
The pod grants one gun from a curated twelve for 20 seconds through the existing
`WeaponLoadout` — the same component, timer and drop-on-de-rez the arena's
airdrops use. The kit's odds rise as the hero's shield falls, so a bad run gets
help and a good one does not.

The twelve are scaled up on pickup (`TankArsenal.PodDamageScale`). They come from
the arena, where they are balanced against hundred-shield robots and a chattering
blaster; dropped in beside a sixty-damage cannon every one of them would be a
downgrade, and a prize that makes you weaker is worse than no prize.

## Files

`Assets/Scripts/Tanks/`

* `TankRaid.cs` — the mode: builds the field, casts the hero, runs the waves, ends the run.
* `TankPawn.cs` — one tank, walker or structure at scene root: model, shield, guns, drive, aim.
* `TankBrain.cs` — close, hold range, strafe, shoot. Both sides use it; `anchor` is the difference.
* `TankPilot.cs` — the machine at the hero's controls, for AI v AI.
* `TankOutpost.cs` — the six structures and what each is worth.
* `TankBoons.cs` — what has been captured, applied from base on every capture and continue.
* `TankRaidPick.cs` — who drives, remembered between runs.
* `TankField.cs` — the battlefield that never ends (recycled bands, seeded scenery).
* `TankRaidCamera.cs` — high and behind, z hard and x soft.
* `TankRaidHud.cs` — shield, lives, distance, wrecks, the gun and its clock.
* `TankSticks.cs` — the two on-screen sticks, raw touches (uGUI can't do two thumbs).
* `TankArsenal.cs` — the twelve guns a pod can hand over.
* `TankPickup.cs` — a pod or a kit on the ground, collected by proximity.

Touched elsewhere: `GameMode.TankRaid` + launcher/teardown in `GameModeController`,
a 13th menu icon in `MainMenu` + `MenuIconRigs`, and one honest new field on
`EnergyShield` (`invulnerable`) for the respawn grace.
