# Brief: writing a FRAGMENTS: 512 episode prompt

You are writing candidate prompts for one episode of an ongoing 30-second
vertical video series generated with Seedance 2.5. You write text only — do
NOT call any generation API, do NOT run Tools/seedance.py, do NOT create
videos. Your entire job is authoring prompt files.

## Read these first

- `Fragments512/SERIES.md` — the series bible (lore, constants, hard rules).
- `Fragments512/ep003/prompt_song.txt` — THE CANONICAL TEMPLATE. Your
  candidates must follow its exact structure, section headings, tone and
  length. Study it before writing a word.

## The premise in one paragraph

Humans built robots and gave some the gift of transformation. Those
transformers rebelled and humanity fled Earth. As the last humans escaped, the
rebels destroyed the great migration portal, shattering CUBE 0 — the archive
holding the DNA of every living creature on Earth — into 512 small golden
glyph cubes flung across the universe. The loyal transformers who refused the
rebellion stayed behind to heal Earth. Each episode, one loyal robot travels
to a new world and recovers one cube.

## The fixed shape of every episode (do not deviate)

Sections, in this order, with these time codes:

1. Header paragraph: "A 30-second cinematic episode of an epic
   space-exploration saga... This is an ALIEN place — nothing about it should
   resemble Earth."
2. The hero paragraph: names the robot from the first reference image, says
   its fighter-jet form is in the following reference images (sharp pointed
   nose at front, engine exhausts at rear), and that the golden glyph cube
   artifact is the last reference image.
3. LIGHTING RULE — one dominant light source, consistent direction and shadows
   in every shot, NO moons. (If your world has no sun, name its single
   dominant light source instead and keep it consistent.)
4. FLIGHT RULE — the jet always flies NOSE-FIRST, nose leading, engines
   trailing; banks and carves like a real aircraft; never sideways or
   backwards.
5. THE WORLD — 3-5 sentences establishing the world's central impossible idea.
6. A physics/behaviour RULE paragraph unique to your world, telling the model
   how the world's strangeness behaves consistently.
7. OPENING (0-3s) — orbit cold open, the robot sees the world, a small golden
   holographic counter beside its helmet reads exactly "FRAGMENT NNN / 512",
   the robot dives.
8. THE DIVE (3-16s) — transformation to jet, then a sweeping continuous
   flyover of 3-4 escalating regions, ending by revealing the episode's
   landmark. "Fast, fluid, majestic — no slow motion."
9. THE CHALLENGE (16-23s) — transforms back to robot; a physical problem that
   only this world could pose; one setback and one recovery.
10. THE FRAGMENT (23-28s) — finds the single golden cube (described as "the
    artifact from the reference image — a single fist-sized golden metallic
    cube etched with glowing circuit glyphs"), takes it, and one breathtaking
    world-wide reaction beat fires.
11. THE RITUAL CLOSE (28-30s) — close-up of the robot's open palm holding the
    ONE cube; "there is only ever this single cube on screen"; holographic
    text reading exactly "FRAGMENT — SECURED" (this exact text, two words
    only, NO numbers or digits anywhere in it); a calm epic voice speaks the
    episode's only spoken words: "Fragment <number-word>... secured."
12. Audio paragraph — an original epic MOTIVATIONAL anthem with clear English
    vocals composed in the same pass, soaring and uplifting, drums building
    through the dive and peaking as the fragment is taken. Include FOUR lines
    of original lyrics in quotes, themed to your world's central idea, then:
    the sung anthem is the only sound until the closing spoken line; no other
    dialogue, no robotic servo sounds. Close with the standard tail: vertical
    9:16 composition, cinematic lighting, photoreal detail, robot always crisp
    and readable, fast-paced, majestic, unforgettable.

## Hard rules learned the expensive way

- **No Earth nouns.** Every noun is a die-roll toward Earth imagery. A
  "shipwreck" rendered as a wooden sailing galleon in space and had to be
  re-rolled as "derelict spacecraft". Never write ships, castles, cathedrals,
  trees, forests, birds, whales, cities-as-we-know-them. Invent alien nouns or
  qualify them hard ("kelp-like", "derelict alien spacecraft").
- **No moons.** They render as unnaturally stacked with mismatched lighting.
- **Digits only in the cold-open counter.** The ritual close must contain NO
  digits — the model mistypes them there ("516" twice in one episode).
- **One cube.** Never plural cubes in the close.
- **No slow motion**, except at most one single deliberate beat.
- The cold open must be unmistakably alien — an early episode's orbit shot
  looked like Earth and had to be re-rolled.
- Keep it kid-friendly: wondrous and dangerous, never gory.

## Already used — do not reuse these worlds

- ep001: frozen world, frozen tsunami waves, ice spires, mirror lake.
- ep002: an ocean floating in the sky above rose-glass dunes, rain falling
  upward, a water column called the Upfall.
- ep003: two planets orbiting each other closely with a zero-gravity corridor
  between them full of drifting boulders and derelict spacecraft.

## Your deliverables

1. Ten complete, generation-ready prompts at
   `Fragments512/ep<NNN>/candidates/cand_01.txt` … `cand_10.txt`.
   Each must be a full prompt following the shape above — not an outline, not
   a pitch. Each roughly 400-550 words.
2. `Fragments512/ep<NNN>/SELECTION.md` — a numbered list of all ten with a
   one-line pitch each, then your pick with 3-4 sentences of reasoning judged
   on: how *other-worldly* the central idea is, whether the strangeness is
   VISUAL (must read instantly in a vertical 30-second video, not require
   explanation), whether the challenge could only happen on this world, and
   how strong the fragment-reaction beat is.
3. `Fragments512/ep<NNN>/prompt.txt` — an exact copy of the winning candidate.

The ten candidates must be ten genuinely DIFFERENT worlds inside your assigned
domain — different landforms, different challenge mechanics, different hiding
places for the cube. Ten variations on one idea is a failed batch.

## Your final message back

Return: the episode number, the winning candidate's filename, the world's
name, a one-sentence pitch of the winner, and the one-line pitches of the
other nine. Keep it under 250 words. Your final text is data for the
orchestrator, not a message to a human.
