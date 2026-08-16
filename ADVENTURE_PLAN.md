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
| A beat's prose runs **280–380 words** | A beat is a MINI STORY — opening image, escalating middle, a turn, a landing. At 190 words it reads as a summary of a scene rather than the scene |
| A beat is a **9–10 shot storyboard summing to exactly 30s** | 4–6 shots read as slow and empty on screen; cutting on 3s and 4s is what gives a beat weight. At a 3s floor, 11 shots cannot fit in 30s and 10 forces every shot to 3s — so 9–10 is the ceiling at this length |
| Voice-over is capped at **75 words a beat**, and each line must fit its own shot | ~22s of speech inside 30s of picture; the images carry the rest |
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

## The art: one pass, conditioned on the real robots

One still per beat, generated in a single call with the robots' own reference
renders attached, so the character is the character and the pose is whatever
the beat needs. `Tools/adventure_onepass.py --openai` runs the whole file.

| Piece | What it does |
|---|---|
| `Tools/adventure_onepass.py` | Reads each beat's cast off its KEY shot, attaches those robots' reference renders, asks gpt-image-1 for character and room together |
| `Tools/adventure_cutout.py` | Keys the reference renders out of `ExternalData/` and `PreviewCaptures/` — robots, six jet angles each, tank forms from the transformation clips |
| `Tools/adventure_art.py` | Paints character-free plates (the fallback pipeline, below) |
| `Tools/adventure_compose.py` | Composites cutouts into plates (the fallback pipeline, below) |

**What works, verified rather than assumed:**

- **OpenAI `gpt-image-1`, via `/v1/images/edits` with the references as
  repeated `image[]` fields.** Panther comes back as Panther, and — the thing
  compositing could never do — he *kneels*, reaches, hangs. Titan's
  blue-and-yellow, Samurai's horned ice-blue crest and Bolt's navy all survive,
  and 36 independently generated stills share one cel-shaded look.
  Key in `.secrets/openai_key.txt`; ~40 s per image, so run the full set
  detached (a foreground shell call dies at ten minutes).
- **MiniMax image-01 cannot do it.** `subject_reference` tested four ways —
  keyed cutout on white, front preview render, width/height instead of
  aspect_ratio, optimizer on and off. A robot that is not Panther every time,
  in photoreal gloom instead of the toon look.
- **BytePlus Seedream 4.0** answers `ModelNotOpen`; never activated.

**Two things the prompt builder has to get right:** take the cast from the KEY
shot only (scanning all six shots drags in characters standing somewhere else
in the thirty seconds, and sending their reference invites the model to paint
them in), and strip the plate prompt's compositing clauses ("the right half is
empty ground") — they are the exact opposite of what one pass wants.

**Known limitation:** the model favours character portraits over choreography.
Beats asking for a specific physical action — a hand catching a falling kid off
a ladder — come back as two robots standing near a ladder. Identity and mood
are excellent; staging is looser than the storyboard asks.

### The fallback: paint the room, composite the real robots

Still built, still runs, and worth keeping because it cannot get a character
wrong: `adventure_art.py` paints 36 plates empty of characters,
`adventure_cutout.py` keys the actual models, `adventure_compose.py` places,
grades and shadows them in. All 36 composited stills are kept in
`Tools/adventure_composited/` for comparison. Two lessons from it that still
apply anywhere art gets graded: never multiply a robot by the scene light (an
orange robot times a teal night comes back gold, and his colour is his
identity), and a contact shadow plus a one-sided rim light is the difference
between standing in a scene and lying on top of one.

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
- [x] **Video: the first three beats are cut.** `Tools/adventure_video.py n01`
      builds a beat's 30 seconds from its own storyboard — one still per shot
      (gpt-image-1, cast references attached), each pinned as the FIRST FRAME
      of a Seedance 2.5 image-to-video job, then ffmpeg trims and joins them.
      Output in `Renders/adventure/<beat>.mp4`. Done: `n01` and both of its
      choices, `n02` and `n03`.

      Three things that bit, all fixed in the tool:

      - Seedance **rejects `ratio` when a first frame is pinned** — the output
        ratio follows the frame. Send the frame, not the ratio.
      - It only makes certain clip lengths, so a 7-second shot comes back as a
        6-second clip and the beat finishes early. The cut now retimes each
        clip to the length the storyboard asked for (a freeze for holds), so
        thirty seconds stays thirty seconds.
      - A shot's line often refers to somebody it never names ("the empty
        hand" is Titan's), so the frame came back with two Panthers in it.
        `SHOT_CAST` names who is actually in frame for those shots.

      **Sound:** clips are generated with `generate_audio` ON and the shot's
      own `sfx` line as the sound brief, restricted to ambient effects so a
      narration track can be laid over later without fighting generated music.
      The first pass shipped silent because the flag was off AND ffmpeg was
      passed `-an` — check both if a cut comes back quiet.

      Still to do: voice-over (each shot already carries its `vo` line and
      Azure TTS is wired for the movie mode), and the other 33 beats.
- [ ] **One-pass art — written, waiting on a key.** The better pipeline is one
      generation per beat with the robots' own reference renders conditioning
      it: character and room together, no compositing, and the characters can
      hold poses the reference renders cannot strike. It lives in
      `Tools/adventure_onepass.py`, which reads each beat's cast off its KEY
      shot (33 of 36 beats have one; the other three are prop inserts), sends
      those reference renders with the prompt, and writes straight over the
      composited still so the two approaches can be compared beat by beat.

      | Provider | State |
      |---|---|
      | MiniMax image-01 | **Cannot do it.** `subject_reference` tested four ways — keyed cutout on white, front preview render, width/height instead of aspect_ratio, optimizer on and off. Returns a robot that is not Panther every time, and trades the toon look for photoreal gloom. Second confirmation of the house rule after the menu cards. |
      | BytePlus Seedream 4.0 | Recognised, answers `ModelNotOpen`. One activation in the Ark console (Model Services → Seedream 4.0). |
      | OpenAI `gpt-image-1` | No key. Drop one in `.secrets/openai_key.txt`; the model needs a verified organisation. Uses `/v1/images/edits` with the references as `image[]`. |
      | Gemini 2.5 Flash Image | No key. Drop one in `.secrets/gemini_key.txt`. Uses `inline_data` parts. |

      The OpenAI and Gemini paths are written from the documented request
      shapes and have **never been run**, because there is no key on this
      machine for either. The first invocation is the test, and it prints
      whatever the API actually returned rather than guessing.

      Two things the prompt builder has to get right, both learned the hard
      way: read the cast from the KEY shot only (scanning all six shots drags
      in characters standing somewhere else in the thirty seconds, and sending
      their reference invites the model to paint them into frame), and strip
      the plate's compositing clauses (a plate deliberately reserves empty
      ground for a cutout, which is the exact opposite of what one-pass wants).
