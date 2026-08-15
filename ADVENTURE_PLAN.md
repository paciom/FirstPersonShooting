# ADVENTURE — mini branching side stories

A shelf of short, replayable side files from the season being written in
`Stories/`. Each one is a **branching story graph**: you read (later: watch) a
30-second beat, then pick one of two things to do. Some picks end it on the
spot. The good and bad endings are both meant to be *surprising* — the bad one
is never "you lost", it is "oh no, that is what that meant".

Text mode ships first, because a text adventure is free to make and can be read
by anyone who prefers reading. Video mode reuses the same files.

---

## The rules a side file obeys

These are enforced by `Tools/adventure_doc.py`, which fails loudly rather than
letting a broken graph reach a player:

| Rule | Why |
|---|---|
| Every branching beat has **exactly two** choices | One decision per beat, no menus |
| **10 beats maximum** on the longest route (`maxSteps`) | A run is 5 minutes of video, tops |
| The graph is a **DAG, not a tree** — paths rejoin | 36 beats cover a hundred routes; a 10-deep tree would need 1,023 |
| Every beat carries a **hook, a surprise, and a cliffhanger** | The three fields are required and checked non-empty |
| A beat's prose runs **150–200 words** | Shorter than that and a beat reads as a summary, not a scene |
| A beat is a **4–6 shot storyboard summing to exactly 30s** | A beat that claims thirty seconds has to actually be thirty seconds |
| Voice-over is capped at **60 words a beat**, and each line must fit its own shot | ~22s of speech inside 30s of picture; the images carry the rest |
| Exactly one shot per beat is marked `key` | That is the frame that becomes the beat's still image |
| Endings have no choices; branching beats always do | The reader switches screens off this |
| No unreachable beats, no cycles, no dead links | A dead link would strand a player mid-story |

Each beat carries a **`shots`** array — the storyboard — and this is the entire
hand-off to video mode:

```json
{"t": 8, "cam": "VERTICAL TRACKING",
 "action": "Panther goes over the rail and falls eighty meters down the outside
            of the foundry wall, cyan strips flaring, kicking off the brick twice",
 "vo": "So he goes over the wall on foot.",
 "sfx": "wind, plating scrape, impact", "key": true}
```

`cam` comes from a fixed vocabulary (WIDE, LOW ANGLE, MACRO INSERT, TRACKING,
CRANE UP, TOP-DOWN, HOLD …) so a shot list is a thing a renderer can read and
not just prose. The first shot lands the hook, the last lands the cliffhanger.
The whole storyboard was written from the beat's own prose, so text mode and
video mode say the same things in the same order.

## Continuity is a whole-graph property, not a local one

The trap with branching stories is writing each fork against its neighbour and
ending up with beats that contradict each other three steps later. What keeps
this one straight:

- **Layers.** Every beat sits at a fixed step number, and every choice moves
  *forward* a layer or more. There are no side-steps and no back-edges, so a
  beat only ever has to make sense to people arriving from an earlier layer.
- **Merges are written for every entrance.** A beat that three different routes
  reach opens on the thing all three have in common (`THE OLD WARDEN` starts
  with the old one's hand on your arm, whether you got there by stealing a core
  or by pulling a crew out of a recycler).
- **The endings are the spine.** They were written first. The middle is the
  argument that earns each of them.
- **The show is the ceiling.** Side files run alongside `Stories/S01E01_The_Green_Line.md`
  and never contradict a scene in it. One ending per file is flagged `canon:
  true` — the one the episode agrees with. The rest are what-ifs, and the reader
  says so.

## Cost

Nodes are the unit of cost, and merges are the lever: 36 beats cover every
route through a ten-step story because twelve of them are reached from more than
one direction. Budget a side file at **30–40 beats**, which is 15–20 minutes of
generated video *for the whole graph* while any single run is 5 minutes. Price
that against the current Seedance rate before committing to video mode — the
graph shape is what makes it affordable, not the clip price.

---

## What is built (text mode)

| Piece | Where |
|---|---|
| Story format + loader + endings-found progress | `Assets/Scripts/Adventure/AdventureStory.cs` |
| The reader screen (hook / beat / cliffhanger / 2 choices) | `Assets/Scripts/Adventure/AdventureReader.cs` |
| The shelf of mini adventures | `Assets/Scripts/Adventure/AdventureSelect.cs` |
| Mode wiring (`GameMode.Adventure`, menu card) | `GameModeController.cs`, `MainMenu.cs` |
| Validator + readable script renderer | `Tools/adventure_doc.py` |
| Import rules for the stills | `Assets/Editor/AdventureArtImporter.cs` |
| Side File 01 | `Assets/Resources/Adventures/quiet-confirmation.json` |

## The art: paint the room, composite the real robots

One still per beat, and it is **not** generated whole. Prompting an image model
for "a sleek orange panther robot" returns a handsome robot that is not
Panther — this project already rejected exactly that once, for the menu cards.
So the still is assembled in three stages:

| Stage | Tool | What it makes |
|---|---|---|
| 1. Paint the room | `Tools/adventure_art.py` | 36 environment plates, explicitly empty of characters, framed from each beat's `key` shot |
| 2. Cut out the cast | `Tools/adventure_cutout.py` | RGBA cutouts of the ACTUAL robots, jets and tanks from `ExternalData/` and `PreviewCaptures/` |
| 3. Put them in | `Tools/adventure_compose.py` | Places, scales, grades and shadows the cutouts into the plate |

What the compositor had to learn, both worth keeping:

- **Never multiply a robot by the scene light.** An orange robot times a teal
  night comes back gold, and his colour is his identity. The scene tint is a
  28% blend, and the glowing panels are treated as emissive and exempt from
  the dimming entirely.
- **A contact shadow and a one-sided rim light** are the difference between a
  character standing in a scene and a sticker lying on top of one.

The keyer flood-fills the background in from the border rather than keying by
colour distance, so the dark navy ON a robot survives a dark navy background,
and it keeps only the largest blob, which is what silently drops watermarks
and video letterbox bars.

Roughly a third of the beats are prop and place shots — a heel print, a name
tag, one word on a wet display — and those are the bare plate with no cutout
at all, which is both cheaper and better.

**Adding a mini adventure is one file.** Drop a JSON in
`Assets/Resources/Adventures/` and the shelf finds it — there is no index to
update. Then run:

```bash
python Tools/adventure_doc.py
```

which validates every adventure in the folder, and with `--md` / `--html`
renders one for reading.

The reader is keyboard and touch: `1` / `2` pick, `ESC` exits, and the shelf
remembers which endings have been found (`3 / 7 ENDINGS`), which is the replay.

---

## Shipped

### SIDE FILE 01 — "QUIET CONFIRMATION" · PANTHER

The night between the Foundry gates and the crawler's dawn in S01E01. Titan
sends Panther to confirm the green is real, quietly — and somebody else goes
out into the storm the same night. 36 beats, 7 endings, longest route 10.

Canon ending: `e_catch` — the dark hand that catches Bolt on the ladder in
scene 9. Everything else is a road the episode did not take.

## Backlog — the rest of the shelf

Each one is a different robot, a different tone, and a different corner of the
same week, so the shelf reads as a season and not as one story rewritten:

1. **"THE LAST MILE" · RACER** — the courier run carrying the sprout footage to
   the Uplink Spire ahead of the storm. Pure speed and comedy; the surprises are
   what he has to give up to keep moving.
2. **"THE SKY CALLED MY BLUFF" · HAWK** — flying the decoy beacon into an acid
   squall (S01E01 scene 8, from inside the weather). The antagonist is the sky,
   until it isn't.
3. **"ARCHIVE NIGHT" · KNIGHT** — the Spire, the dead beacon, and what a
   librarian chooses to write down. Quiet, creeping, all the surprises are in
   the records.
4. **"SALVAGE RIGHTS" · SCOUT & RACER** — going back down into the seed vault
   for tools, with company already inside.
5. **"THE SHELF" · RANGER, YEARS EARLIER** — the first name tag. Why a robot
   who stopped believing keeps polishing.
6. **"T-9" · TITAN, 200 YEARS AGO** — the walk from the garden to the Foundry.
   Every route ends in iron; the file is about which iron.

## Next

- [ ] Play-test Side File 01 in the editor (the mode is runtime-built — Play is
      enough, no arena rebuild).
- [ ] Menu art: `Assets/Resources/Menu/adventure.png` via `Tools/menuart.py`
      (the card works without it, like STORY's does today).
- [ ] Video mode: 4–6 clips per beat straight off the `shots` array, VO from
      each shot's `vo` line, with the reader's graph walk kept exactly as it
      is. 180 shots for the whole file; a single run is ~50 clips.
- [ ] Optional upgrade: `seedream-4-0-250828` on the BytePlus Ark account is
      recognised but NOT ACTIVATED. Turning it on in the Ark console would
      allow true reference-image conditioning, which could replace the
      cutout-compositing stage with generated-but-faithful characters in
      poses the reference renders cannot strike.
