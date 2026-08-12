# FRAGMENTS: 512

An awe-inspiring space-exploration saga in 30-second episodes.

## The lore (canon as of 2026-08-12)

Humans created robots and gave some the gift of transformation. The
transforming robots rebelled; humanity was forced to flee Earth. The rebels
destroyed the seed vault and all seeds. Humanity's DNA archive — CUBE 0, one
colossal cube assembled from 512 small glyph cubes (8x8x8) holding the DNA of
every living creature on Earth — was being carried through the great migration
portal during the exodus when rebel fire destroyed the portal: the last ships
slipped through, but the explosion shattered Cube 0 at the threshold and
dispersed its 512 cubes across the universe. A loyal remnant stayed on Earth —
including the only transformers who refused the rebellion (our nine heroes;
this is why the heroes transform even though transformation was the rebels'
gift). They restore Earth's environment, prepare for humanity's return, and
recover the cubes: the hope of Earth's rebirth. The rebels remain at large and
can appear as antagonists hunting the same cubes. The ep000 title card reads
exactly "CUBE 0".

Optional episodic payoff: a secured cube may project a hologram of one Earth
creature it preserves (whale, butterfly, wolf...) — a collect-the-animals hook.

## The constants (every episode)

- **The fragment**: a fist-sized golden metallic cube etched with glowing
  circuit glyphs, always identical, always rising on a column of light.
- **The counter**: holographic `FRAGMENT NNN / 512` in the cold open.
- **The ritual close**: close-up of the robot's open palm holding the ONE cube
  (never show multiple cubes), holographic text "FRAGMENT — SECURED" (NO digits
  — the model reliably mistypes numbers in this shot; ep002 typed "516" twice),
  a calm epic voice says "Fragment <n>... secured." The cold-open counter and
  the voice line carry the numbers.
- **Hard rules learned from ep001 v1**: no moons (they render as unnaturally
  stacked with inconsistent lighting); ONE sun with consistent shadows; jets
  fly NOSE-FIRST (give front-3/4, rear-3/4 and profile angle refs from
  ExternalData/JetRefs/<Robot>/); include cube_ref.png as the last reference.
- **The formula**: 0-3s orbit cold open -> 3-16s transformation dive + planet
  flyover (the awe pass) -> 16-23s landing + challenge -> 23-28s recovery ->
  28-30s ritual close.

## Production notes

- DEFAULT AUDIO for every fragment episode: an original English MOTIVATIONAL
  anthem with sung lyrics, composed by Seedance in the same single pass
  (lyrics written into the prompt, themed to the episode's world), plus the
  closing spoken voice line. Orchestral-only is the exception, not the rule.

- Generator: Seedance 2.5 via Tools/seedance.py, 30s 720p 9:16.
- References per episode: robot portrait + vehicle-form still, both sourced
  from ExternalData (vehicle stills extracted from the *_Transformation.mp4
  clips' final frames).
- Robot rotation: a different robot may lead each episode; Episode 1 is Scout.
- The ritual close carries the episode number in its holographic text, so a
  shared pinned last_frame is NOT usable across episodes; consistency comes
  from cube_ref.png + the fixed close-up staging instead.
- Jet references: ExternalData/JetRefs/<Robot>/angle_*.png are frames of a
  Seedance-generated studio turntable seeded from each robot's transformation
  clip (the clips themselves hold only one fixed 3/4 view).
- Folder layout: `epNNN/prompt.txt`, `epNNN/refs/`, `epNNN/output/` (mp4 +
  request-sidecar JSON).

## Episode log

| Ep  | Robot | Entity                                | Status |
|-----|-------|---------------------------------------|--------|
| 000 | Scout+Hawk | Prologue: Cube 0, rebellion, exodus | DONE 2026-08-12 — v7 recommended (rewritten three-party narration; v1-v5 use the old pronoun script). Narration flubs ~half of rolls: transcribe before accepting. |
| 001 | Scout | Frozen world, mirror lake (v2 canonical) | DONE 2026-08-12 |
| 002 | Hawk  | Sky-Sea: floating ocean, upward rain, the Upfall | DONE 2026-08-12 (song_v3 canonical) |
| 003 | Samurai | Twin Waltz: binary planets, zero-g Still Zone | DONE 2026-08-12 (song version canonical — sung anthem, two distinct planets in cold open; v1's wreck was an Earth galleon: debris must be derelict SPACECRAFT, never 'ships') |
| 004 | Titan | the Spindle: a world tidally stretched toward a dead star | DONE 2026-08-12 (first take accepted; sung anthem) |
| 005 | Panther | the Cinder Lattice: starless world lit only by its own molten seams | DONE 2026-08-13 (2 takes; v1 recommended) |
| 006 | Racer | the Timetide: moving wavefronts of accelerated time | DONE 2026-08-13 (2 takes; v2 recommended) |
| 007 | Knight | the Hanging Works: hollow world of inward-hanging machine-spires | DONE 2026-08-13 (2 takes; v1 recommended) |
| 008 | Bolt | the Petrified Storm: lightning frozen mid-strike, re-firing in waves | DONE 2026-08-13 (2 takes; v2 recommended) |
| 009 | Ranger | Respira: the crust is one breathing membrane | DONE 2026-08-13 (2 takes; v2 recommended) |
| 010 | Scout | the Reiteration: mountains built of smaller copies of themselves | DONE 2026-08-13 (2 takes; v2 recommended) |
| 011 | Hawk | the Thousand Panes: stacked mirror layers inside a giant | DONE 2026-08-13 (2 takes; v2 recommended) |
| 012 | Samurai | the Chladni Plains: a vibrating crust patterned by its own note | DONE 2026-08-13 (2 takes; v1 recommended) |
| 013 | Titan | the Sunlattice: sunbeams hardened into solid girders over a void | DONE 2026-08-13 (2 takes; v1 recommended) |

Episode 1 v2 verified: counter and "FRAGMENT 001 / 512 — SECURED" text crisp,
voice line confirmed by STT, no moons, nose-first flight, single cube in the
close. (v1's lattice close and anchor_lattice_frame.png are retired.)

## Episode 004-013 slate

Each was chosen from ten candidate prompts written against a single assigned
domain, so no two episodes share a world concept. Candidates are kept in
`epNNN/candidates/`; the reasoning for each pick is in `epNNN/SELECTION.md`;
the winner is `epNNN/prompt.txt`, generation-ready. Domains assigned:
gravity (004), darkness (005), time (006), inside-out (007), plasma (008),
biology (009), geometry (010), gas-giant depth (011), sound (012), light (013).
Shared authoring rules live in `AGENT_BRIEF.md`.

### Generation notes, 005-013

Two takes each via `Tools/fragments_batch.sh` in three parallel lanes.
ep009 v2 was refused once with `OutputVideoSensitiveContentDetected` — the
output-side moderation filter, not a prompt error, since v1 of the identical
prompt passed; a straight retry cleared it. Respira's flesh-like terrain is
the likely trigger, so soften anatomical wording if it recurs.

Recurring audio defect across the series: the singer sometimes performs a
stage direction as a lyric (ep009 v1 sings "sphere pulses gold"; ep012 v2
sings "collapses flat at once"). Transcribe every take before accepting it.
