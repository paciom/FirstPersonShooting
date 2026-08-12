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
| 000 | Scout+Hawk | Prologue: Cube 0, rebellion, exodus | DONE 2026-08-12 — v4 recommended (5 rolls: v4 clean narration + gridded cube; v5 best shatter shot, worst audio) |
| 001 | Scout | Frozen world, mirror lake (v2 canonical) | DONE 2026-08-12 |
| 002 | Hawk  | Sky-Sea: floating ocean, upward rain, the Upfall | DONE 2026-08-12 (song_v3 canonical) |

Episode 1 v2 verified: counter and "FRAGMENT 001 / 512 — SECURED" text crisp,
voice line confirmed by STT, no moons, nose-first flight, single cube in the
close. (v1's lattice close and anchor_lattice_frame.png are retired.)
