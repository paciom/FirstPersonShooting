# Photon Arena

A kid-friendly (ages 8–14) sci-fi first-person shooter built in Unity 6, designed for
**Player vs AI** and **AI vs AI** modes with auto-recorded, YouTube-ready gameplay videos.
See [PLAN.md](PLAN.md) for the full project plan.

No one ever "dies" in Photon Arena: laser hits drain an **energy shield**, and characters
who lose their shield **de-rez** into light and re-materialize moments later.

## Game modes

The game opens on a main menu with three modes:

- **AI v AI** — cyan and magenta robot teams fight autonomously while an
  auto-directed spectator camera orbits the action and cuts between bots
  (the seed of the broadcast CameraDirector).
- **Player v AI** — you + 3 cyan ally bots vs 3 magenta hunters (4v4 feel).
  Only your own de-rezzes add to the HUD score.
- **Arena Builder** — free-fly camera; press **R** to generate a randomized
  cover layout (NavMesh rebakes live). The layout persists for the next match.

**Esc** returns to the menu from any mode; click to re-lock the cursor after alt-tab.

## Chinese Run — the learning mode at a sprint

The same question as Chinese Quest, asked under pressure. The hero sprints down
a road with no end; every so often four robots are standing across it, one per
lane, holding the four meanings. **The road is the clock** — answer before you
reach them, because running out of road counts as getting it wrong.

Right, and the hero blows through the robot holding the answer: a photon blast
taken on the approach, or a gear change into a kick at a dead sprint. Wrong (or
too slow), and the robot that *was* right fires down the road instead — the hero
is thrown backward far enough to cost real ground, which is the only currency
here. Distance and your best-ever distance sit in the corner where Quest keeps
its shields.

The road is a fixed ring of segments recycled from back to front, so it is
infinite at a constant object count, and it carries no colliders at all —
`BrawlGround` answers 0 where it finds none, which *is* the road's surface.

## Chinese Quest — the learning mode

A hero robot stands centre stage with a Chinese character at the bottom of the
screen and four robots ringing it, one per corner and all turned in on it, each
holding up a meaning and its pinyin. Pick one — tap the robot, tap its card, or
press **1**–**4**.

Committing to an answer is what earns the pronunciation: the character is
**spoken in Mandarin the instant you choose**, right or wrong. It is never
spoken before, because every card carries pinyin — hearing the word first would
turn "what does this character mean" into "which of these four sounds was that",
and the character need never be looked at.

Then the robot holding the **right** answer settles it, in one direction or the
other. Find it and the hero rockets across the stage or blasts down the diagonal
and puts it down. Miss it and the hero never swings at all — it holds its mark
while the correct robot crosses the stage and floors *it*, then takes the pose
as its card lights green beside the red one you picked. Five shields, then the
run ends.

The whole cast runs at double a bout's speed, and the charge to a target at four
times, through `BrawlFighter.Tempo` — one knob that scales a fighter's delta time
and its Animator together, so hit windows never drift out from under the fist.

Nothing here re-implements combat — all five robots are `BrawlFighter`s driven
through the same `Intent` struct a human controller fills, so the frame data,
hit reactions and knockdowns are Brawl's own. The mode adds only choreography.

### Decks, and the INFINITE one

Both modes open on the same deck picker. Eleven themed decks (ANIMALS, NUMBERS,
FAMILY, …) are hand-picked, plus EVERYTHING, which mixes them. A round's three
wrong answers always come from the same deck as the right one — same-theme
decoys are the difficulty, since four words from four themes can be answered off
the theme alone without ever reading the character.

**INFINITE** is different: the 2000 most frequent characters in written Chinese,
walked from the top, with spaced repetition (`ChineseProgress`). New characters
arrive in frequency order; a character may only come back once **50 questions**
have passed, so getting it right proves something; and it is **retired after
three correct in a row** and never asked again. The deck card shows how many of
the 2000 have been retired, and that state survives the session.

### Generated assets

All three are **generated, not authored**, and re-runnable after editing
`Assets/Scripts/Chinese/ChineseLexicon.cs`:

```bash
python Tools/buildfrequency.py
```

```bash
python Tools/subsetfont.py
```

```bash
powershell -ExecutionPolicy Bypass -File Tools/chinesevoice.ps1
```

The first builds the INFINITE deck from Jun Da's frequency list and CC-CEDICT;
the second packs the CJK glyphs (Unity's built-in font has none, and a WebGL
build has no OS font to fall back on); the third bakes a pronunciation clip per
distinct reading with Windows' zh-CN voice. See
[Assets/Resources/Chinese/README.md](Assets/Resources/Chinese/README.md).

## Current status — Phase 1 greybox + visual upgrade

- First-person movement (WASD + mouse, Shift sprint, Space jump, Esc frees the cursor)
- Laser blaster (hold left mouse) firing glowing bolts with trails and light halos
- Energy shields with regen, de-rez dissolve + re-materialize cycle
- **Hover-robot enemies** built by `RobotFactory` (glowing visor, chest core, antenna,
  hover ring + thruster, idle bob animation) — 4 orange dummies, 3 magenta hunters
  with human-like reaction delay and aim error
- **Force-field bubbles** (`ShieldBubble` + fresnel/hex `PA_ForceField` shader) that
  flash when characters take hits
- **Multi-stage explosions** (core flash, shockwave ring, stretched sparks, glow motes)
  from procedurally generated sprites (`SpriteForge` → `Assets/Resources/VFX`)
- Neon greybox arena: emissive trim, corner pillars, accent lights, bloom post-processing
- HUD: crosshair, shield bar (cyan → orange as it drains), de-rez score

## Opening the project

1. Open **Unity Hub** → Add project from disk → select this folder (Unity 6000.5.0f1).
2. Open `Assets/Scenes/GreyboxArena.unity`.
3. Press **Play**.

## Rebuilding the arena

The greybox scene is 100% generated by script. In the editor menu:
**Photon Arena → Build Greybox Arena** (or run `ArenaBuilder.BuildAllBatch` in batch mode).
It reconfigures URP, recreates materials, rebuilds the scene, and re-bakes the NavMesh.

## Project layout

- `Assets/Scripts/` — runtime gameplay code (brain/motor split: `PlayerBrain` and `AIBrain`
  both drive the same `CharacterMotor` + `LaserBlaster`, which is what makes AI vs AI mode
  nearly free later)
- `Assets/Editor/` — `ArenaBuilder` (scene generator) and `PackageInstaller` (batch bootstrap)
- `Assets/Scenes/GreyboxArena.unity` — generated greybox arena
- `Assets/Settings/` — generated URP pipeline + post-processing assets
