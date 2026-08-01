# Drop-in effect overrides

**In use now:** `fire.prefab` and `firecomet.prefab` are copies of *Free
Fire VFX - URP* by Vefects (imported to `Assets/Vefects/`), picked from
its 15 prefabs as follows:

- `fire` = `VFX_Fire_Floor_01` — a floor fire, which is exactly what a
  burning patch is. All six of the pack's Floor prefabs are complete.
- `firecomet` = `VFX_Fire_01_Big` scaled to 0.3 by the caller. The Small
  and Medium variants look right for a comet but ship with **missing
  materials** (their "Lights" and "Sparkles Handler" renderers reference
  materials the download omits), so the Big one is used shrunk instead.

Replacing either is just a matter of copying a different prefab over it.

Any prefab placed in this folder **replaces** the matching runtime-built
effect, with no code change. `BrawlFx.TryPrefab` looks them up by name:

| File name here      | Replaces                                        |
|---------------------|-------------------------------------------------|
| `fire.prefab`       | the burning patch left by falling fire (30 s)   |
| `firecomet.prefab`  | the trail on the fire as it falls               |
| `steam.prefab`      | the steam geyser's eruption column              |

Requirements for a prefab dropped here:

- It must **loop** (`Looping` on, `Play On Awake` on). The game controls
  when it stops: fire calls `Stop(withChildren, StopEmitting)` as it dies
  down, and the geyser drives `emission.rateOverTime` directly, so leave
  the emission rate as the effect's natural full-strength value.
- It should sit at its own origin, pointing **up**, sized for roughly a
  **2.5 m wide** patch (the fire) or a **2 m wide** vent (the steam).
  Scale the prefab's root to fit rather than editing the systems.
- URP materials. If an imported effect renders bright magenta, its
  materials are Built-in RP — use `Edit > Rendering > Materials > Convert
  Selected Built-in Materials to URP`.

## Where to get good fire

Verified on the Unity Asset Store (2026-08-01):

- **Particle Pack** — Unity Technologies, **free**, URP-ready. Unity's own
  effect collection, includes realistic fire and explosions. The safest
  first try; its `Fire` prefabs drop straight in.
  <https://assetstore.unity.com/packages/vfx/particles/particle-pack-127325>
- **Cartoon FX Remaster Free** — Jean Moreno, **free**, URP-ready. Stylised
  rather than realistic, which arguably suits this game's look better than
  photoreal flames. The paid *Cartoon FX Remaster* expands it.
  <https://assetstore.unity.com/packages/vfx/particles/cartoon-fx-remaster-free-109565>

Search terms that surface the rest: "fire VFX URP", "stylized fire",
"realistic fire particle". Anything that ships a looping fire *prefab*
works — this hook does not care how the effect is built.

If nothing is in this folder, `BrawlFx` builds its own flames from the
project's additive sprite pipeline, so the game always has fire.
