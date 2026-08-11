# FRAGMENTS: 512

An awe-inspiring space-exploration saga in 30-second episodes. A master cube was
shattered in a space-portal explosion into 8x8x8 = 512 fragments, scattered
across the universe. Each episode, one of the nine robots recovers one fragment
from a new space entity.

## The constants (every episode)

- **The fragment**: a fist-sized golden metallic cube etched with glowing
  circuit glyphs, always identical, always rising on a column of light.
- **The counter**: holographic `FRAGMENT NNN / 512` in the cold open.
- **The ritual close**: the cube snaps into a floating 8x8x8 lattice, one slot
  ignites, a calm epic voice says "Fragment <n>... secured."
- **The formula**: 0-3s orbit cold open -> 3-16s transformation dive + planet
  flyover (the awe pass) -> 16-23s landing + challenge -> 23-28s recovery ->
  28-30s ritual close.

## Production notes

- Generator: Seedance 2.5 via Tools/seedance.py, 30s 720p 9:16.
- References per episode: robot portrait + vehicle-form still, both sourced
  from ExternalData (vehicle stills extracted from the *_Transformation.mp4
  clips' final frames).
- Robot rotation: a different robot may lead each episode; Episode 1 is Scout.
- After Episode 1: extract the final lattice frame and pin it as last_frame
  (--last-frame) of future episodes so the ritual close stays visually
  identical across the series.
- Folder layout: `epNNN/prompt.txt`, `epNNN/refs/`, `epNNN/output/` (mp4 +
  request-sidecar JSON).

## Episode log

| Ep  | Robot | Entity                                | Status |
|-----|-------|---------------------------------------|--------|
| 001 | Scout | Frozen world, five moons, mirror lake | DONE 2026-08-12 |

Episode 1 verified: counter text rendered crisply, voice line confirmed by
STT ("Fragment 1 secured."), lattice close extracted to
`anchor_lattice_frame.png` — pin it with --last-frame on future episodes.
