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
- **The ritual close** (changed 2026-08-13, from ep014): close-up of the
  robot's open palm holding the ONE cube (never show multiple cubes), with
  holographic text "<n> SECURED. <512-n> TO GO." and NO spoken line at all.
  The anthem plays unbroken to the last frame.
  Why: the old spoken "Fragment <n>... secured." duplicated the on-screen text,
  cut the anthem at its climax, and closed the loop on the exact beat where a
  short either repeats or gets scrolled — and it was the series' single most
  frequent defect. The countdown card keeps the ritual but ends on an open
  loop. Digits render fine in this short form (verified ep014 v4/v5); the old
  "516" mistype came from the longer "FRAGMENT NNN / 512 — SECURED" string.
  State the silence rule three times in the prompt (close, LYRIC RULE, audio
  paragraph) — that reliably produces a silent tail.
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
| 001 | Scout | the Stillwater: an ocean frozen mid-break with its spray still hanging in the air | DONE 2026-08-13 (regenerated to current standard; v3 recommended — the 2026-08-12 v2 cut predates the sung anthem and still has digits in its close) |
| 002 | Hawk  | Sky-Sea: floating ocean, upward rain, the Upfall | DONE 2026-08-13 (regenerated on the countdown-close standard; v4 recommended. Earth nouns forest/trees/cathedral removed) |
| 003 | Samurai | Twin Waltz: binary planets, zero-g Still Zone | DONE 2026-08-12 (song version canonical — sung anthem, two distinct planets in cold open; v1's wreck was an Earth galleon: debris must be derelict SPACECRAFT, never 'ships') |
| 004 | Titan | the Spindle: a world tidally stretched toward a dead star | DONE 2026-08-12 (first take accepted; sung anthem) |
| 005 | Panther | the Cinder Lattice: starless world lit only by its own molten seams | DONE 2026-08-13 (2 takes; v1 recommended) |
| 006 | Racer | the Timetide: moving wavefronts of accelerated time | DONE 2026-08-13 (2 takes; v2 recommended) |
| 007 | Knight | the Hanging Works: hollow world of inward-hanging machine-spires | DONE 2026-08-13 (2 takes; v1 recommended) |
| 008 | Bolt | the Petrified Storm: lightning frozen mid-strike, re-firing in waves | DONE 2026-08-13 (2 takes; v2 recommended) |
| 009 | Ranger | Respira: the crust is one breathing membrane | DONE 2026-08-13 (regenerated; v4 recommended — prompt de-anatomised after a moderation refusal; lyric guard added) |
| 010 | Scout | the Reiteration: mountains built of smaller copies of themselves | DONE 2026-08-13 (regenerated; v4 recommended — world rewritten — recursion was invisible as snowy peaks) |
| 011 | Hawk | the Thousand Panes: stacked mirror layers inside a giant | DONE 2026-08-13 (2 takes; v2 recommended) |
| 012 | Samurai | the Chladni Plains: a vibrating crust patterned by its own note | DONE 2026-08-13 (2 takes; v1 recommended) |
| 013 | Titan | the Sunlattice: sunbeams hardened into solid girders over a void | DONE 2026-08-13 (regenerated; v4 recommended — lattice respecified as a 3D cage; lyric guard added) |
| 014 | Knight | the Sheen: a planet of colossal iridescent foam cells; duel with the Sheenwalker | DONE 2026-08-13 (first creature-antagonist episode; v1 recommended) |

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

Regeneration round (009, 010, 012, 013): BytePlus refuses takes under two
separate filters — `OutputVideoSensitiveContentDetected` and
`OutputAudioSensitiveContentDetected.PolicyViolation`. Both cleared on a plain
retry of an identical prompt, so treat them as variance, not content faults.
The LYRIC RULE (singer performs only the quoted lines; scene text is never
sung) cut the stage-direction defect but did not eliminate it — ep010 v3 still
sang "scale by scale". Transcribe every take.

### Creature episodes (from ep014)

A living antagonist needs its own beat budget: dive compressed to 3-13s, fight
13-24s. Give the creature a dedicated THE CREATURE paragraph (build it out of
the world's own material so it cannot default to an Earth animal) and a FIGHT
RULE keeping the duel to impacts, sparks and throws, with the creature driven
off rather than killed.

Canonical prompt files: every episode's live prompt is `epNNN/prompt.txt`.
ep001-003 previously kept theirs under prompt_v3.txt / prompt_song.txt while a
superseded draft sat at prompt.txt — which the batch driver and the compliance
checker both read. Superseded drafts are now suffixed (prompt_orchestral.txt,
prompt_v1_original.txt) and prompt.txt is always the canonical one.
