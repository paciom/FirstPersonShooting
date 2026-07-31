using System.Collections.Generic;
using UnityEngine;

/// <summary>One Brawl stage: its name, backdrop dressing, and which toys it turns on.</summary>
public struct BrawlArenaDef
{
    public string name;
    /// <summary>Fight INSIDE an FPS arena instead of over the void.</summary>
    public bool remixArena;
    public int arenaIndex;
    public bool crates;
    public bool balls;
    public bool lifts;
    public bool crystalCorners;
    public System.Action<GameObject, RobotRoster> dress;
}

/// <summary>
/// The Brawl stage roster and the remembered pick per mode. Three authored
/// sets — the Commander frontline, a carrier deck, a crystal quarry — plus
/// one remix entry for every FPS arena, each stage with its own toys:
/// cargo rain, bouncer balls, lift pads, crystal corners. Selection 0 is
/// RANDOM, the kid default.
/// </summary>
public static class BrawlArenas
{
    static List<BrawlArenaDef> _all;

    public static IReadOnlyList<BrawlArenaDef> All
    {
        get { EnsureBuilt(); return _all; }
    }

    static void EnsureBuilt()
    {
        if (_all != null)
            return;
        _all = new List<BrawlArenaDef>();
        // The FPS arenas lead — they ARE the brawl grounds of choice, the
        // same places the shooter fights in. Each brings a toy.
        for (int i = 0; i < ArenaLibrary.Count; i++)
            _all.Add(new BrawlArenaDef
            {
                name = ArenaLibrary.Get(i).DisplayName.ToUpperInvariant(),
                remixArena = true,
                arenaIndex = i,
                crates = i % 2 == 0,
                balls = i % 2 == 1,
            });
        // The authored sets stay as explicit picks after the arenas.
        _all.Add(new BrawlArenaDef { name = "FRONTLINE", crates = true, dress = Frontline });
        _all.Add(new BrawlArenaDef { name = "CARRIER DECK", balls = true, lifts = true, dress = CarrierDeck });
        _all.Add(new BrawlArenaDef { name = "CRYSTAL QUARRY", crates = true, crystalCorners = true, dress = CrystalQuarry });
    }

    // ---- selection memory (0 = RANDOM, 1.. = All[i-1]) ----

    public static int CountWithRandom
    {
        get { EnsureBuilt(); return _all.Count + 1; }
    }

    public static string NameOf(int selection)
    {
        EnsureBuilt();
        return selection <= 0 || selection > _all.Count ? "RANDOM" : _all[selection - 1].name;
    }

    public static int SelectedFor(GameMode mode)
    {
        EnsureBuilt();
        return Mathf.Clamp(PlayerPrefs.GetInt(Key(mode), 0), 0, _all.Count);
    }

    public static void SetSelected(GameMode mode, int selection)
    {
        EnsureBuilt();
        PlayerPrefs.SetInt(Key(mode), Mathf.Clamp(selection, 0, _all.Count));
    }

    public static BrawlArenaDef Resolve(int selection)
    {
        EnsureBuilt();
        if (selection > 0 && selection <= _all.Count)
            return _all[selection - 1];

        // RANDOM rolls the FPS arenas only — the shooter's own grounds are
        // the brawl's home turf; the authored sets are explicit picks.
        var arenas = new List<BrawlArenaDef>();
        foreach (var def in _all)
            if (def.remixArena)
                arenas.Add(def);
        var pool = arenas.Count > 0 ? arenas : _all;
        return pool[Random.Range(0, pool.Count)];
    }

    static string Key(GameMode mode)
    {
        return mode == GameMode.BrawlWar ? "PhotonArena.BrawlWarStage" : "PhotonArena.BrawlStage";
    }

    // ------------------------------------------------------------ dressers

    /// <summary>
    /// The battle league staged at the war's edge: command towers and
    /// turret posts behind the ropes, crystal fields glowing, and a column
    /// of the fleet's own vehicles rolling past in the distance.
    /// </summary>
    static void Frontline(GameObject root, RobotRoster roster)
    {
        Tower(root, new Vector3(-9f, 0f, 13f), 3.4f);
        Tower(root, new Vector3(6.5f, 0f, 15f), 4.6f);
        Tower(root, new Vector3(12f, 0f, 12f), 2.6f);
        TurretPost(root, new Vector3(-3f, 0f, 11f));
        TurretPost(root, new Vector3(10f, 0f, 10f));
        for (int i = 0; i < 7; i++)
            Crystal(root, new Vector3(Random.Range(-14f, 14f), 0f, Random.Range(9f, 12f)),
                Random.Range(0.5f, 1.3f));

        BrawlParade.Build(root.transform, roster, z: 22f, y: 0f, scale: 1.4f, speed: 1.6f);
    }

    /// <summary>A hangar deck under a looming ship — cargo lifts included.</summary>
    static void CarrierDeck(GameObject root, RobotRoster roster)
    {
        // Deck stripes read as a flight line.
        var stripe = ArenaMaterials.Emissive("brawl-deck-stripe", new Color(1f, 0.8f, 0.2f), 1.5f);
        foreach (float x in new[] { -6f, 0f, 6f })
            Box(root, new Vector3(x, 0.015f, 0f), new Vector3(0.18f, 0.01f, 5.6f), stripe);

        // The carrier itself: one of the fleet's own hulls, huge, overhead.
        var ship = PickVehicle(roster);
        if (ship != null)
        {
            var hull = Object.Instantiate(ship, root.transform);
            hull.name = "Carrier";
            hull.transform.localPosition = new Vector3(-2f, 8.5f, 17f);
            hull.transform.localRotation = Quaternion.Euler(8f, 205f, 0f);
            hull.transform.localScale = Vector3.one * 7f;
        }
        foreach (float x in new[] { -5.5f, 1.5f, 8.5f })
        {
            var engine = Box(root, new Vector3(x, 7.2f, 15.5f), new Vector3(0.9f, 0.9f, 0.9f),
                ArenaMaterials.Emissive("brawl-engine-glow", new Color(0.3f, 0.8f, 1f), 2.3f));
            engine.name = "EngineGlow";
        }
    }

    /// <summary>Mining pit: crystal walls — the corners BITE (wall slams).</summary>
    static void CrystalQuarry(GameObject root, RobotRoster roster)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * (BrawlStage.LaneHalf + 0.9f);
            for (int i = 0; i < 4; i++)
                Crystal(root, new Vector3(x + side * Random.Range(0f, 0.8f),
                    0f, Random.Range(-1.8f, 1.8f)), Random.Range(0.8f, 1.8f));
        }
        for (int i = 0; i < 9; i++)
            Crystal(root, new Vector3(Random.Range(-15f, 15f), 0f, Random.Range(8f, 13f)),
                Random.Range(0.6f, 2.2f));
    }

    // ---------------------------------------------------------- primitives

    static void Tower(GameObject root, Vector3 position, float height)
    {
        var body = Box(root, position + Vector3.up * (height * 0.5f),
            new Vector3(2.2f, height, 2.2f),
            ArenaMaterials.Surface("brawl-tower", new Color(0.10f, 0.12f, 0.17f),
                new Color(0.2f, 0.9f, 1f), 2.2f, 0.6f));
        body.name = "Tower";
        Box(root, position + Vector3.up * (height + 0.15f),
            new Vector3(2.4f, 0.3f, 2.4f),
            ArenaMaterials.Emissive("brawl-tower-cap", new Color(0.2f, 0.9f, 1f), 1.8f));
    }

    static void TurretPost(GameObject root, Vector3 position)
    {
        Box(root, position + Vector3.up * 0.9f, new Vector3(0.7f, 1.8f, 0.7f),
            ArenaMaterials.Lit("brawl-turret-post", new Color(0.08f, 0.10f, 0.14f), 0.4f));
        var barrel = Box(root, position + new Vector3(0.35f, 1.95f, 0f),
            new Vector3(1.4f, 0.22f, 0.22f),
            ArenaMaterials.Lit("brawl-turret-barrel", new Color(0.13f, 0.15f, 0.2f), 0.5f));
        barrel.name = "TurretBarrel";
    }

    static void Crystal(GameObject root, Vector3 position, float scale)
    {
        var shard = Box(root, position + Vector3.up * (scale * 0.6f),
            new Vector3(scale * 0.4f, scale * 1.2f, scale * 0.4f),
            ArenaMaterials.Emissive("brawl-crystal", new Color(0.45f, 0.9f, 1f), 1.6f));
        shard.name = "Crystal";
        shard.transform.localRotation = Quaternion.Euler(
            Random.Range(-14f, 14f), Random.Range(0f, 360f), 45f);
    }

    static GameObject Box(GameObject root, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static GameObject PickVehicle(RobotRoster roster)
    {
        if (roster == null || !roster.HasRobots)
            return null;
        for (int i = 0; i < roster.robots.Length; i++)
            if (roster.robots[i].vehiclePrefab != null)
                return roster.robots[i].vehiclePrefab;
        return null;
    }
}

/// <summary>
/// The background vehicle column: a few of the fleet's own hulls rolling
/// past behind the fight, wrapping around forever. Set dressing only.
/// </summary>
public class BrawlParade : MonoBehaviour
{
    const float WrapHalf = 26f;
    float _speed;

    public static void Build(Transform root, RobotRoster roster, float z, float y,
        float scale, float speed)
    {
        if (roster == null || !roster.HasRobots)
            return;
        var go = new GameObject("Parade");
        go.transform.SetParent(root, false);
        var parade = go.AddComponent<BrawlParade>();
        parade._speed = speed;

        int placed = 0;
        for (int i = 0; i < roster.robots.Length && placed < 4; i++)
        {
            var vehicle = roster.robots[i].vehiclePrefab;
            if (vehicle == null)
                continue;
            var hull = Instantiate(vehicle, go.transform);
            hull.transform.localPosition = new Vector3(-WrapHalf + placed * 12f, y, z);
            hull.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            hull.transform.localScale = Vector3.one * scale;
            placed++;
        }
    }

    void Update()
    {
        foreach (Transform hull in transform)
        {
            var p = hull.localPosition;
            p.x += _speed * Time.deltaTime;
            if (p.x > WrapHalf)
                p.x = -WrapHalf;
            hull.localPosition = p;
        }
    }
}

/// <summary>
/// Spawn scheduling for a stage's falling toys, capped and unhurried —
/// and aimed AT THE FIGHT: cargo that lands across the arena is scenery;
/// cargo that lands beside (or between) the fighters is gameplay.
/// </summary>
public class BrawlHazards : MonoBehaviour
{
    public bool crates;
    public bool balls;
    public Transform stageRoot;

    float _crateTimer = 4f;
    float _ballTimer = 6f;

    void Update()
    {
        float dt = Time.deltaTime;
        if (crates)
        {
            _crateTimer -= dt;
            if (_crateTimer <= 0f)
            {
                _crateTimer = Random.Range(7f, 13f);
                if (BrawlProps.Count<BrawlCrate>() < 3)
                {
                    var spot = NearTheFight(1.4f, 4.5f, 2f);
                    BrawlCrate.Spawn(stageRoot, spot.x, spot.y);
                }
            }
        }
        if (balls)
        {
            _ballTimer -= dt;
            if (_ballTimer <= 0f)
            {
                _ballTimer = Random.Range(14f, 22f);
                if (BrawlProps.Count<BrawlBall>() < 1)
                {
                    var spot = NearTheFight(2.5f, 6f, 3f);
                    BrawlBall.Spawn(stageRoot, spot.x, spot.y);
                }
            }
        }
    }

    /// <summary>
    /// A drop point around the fighters' midpoint: a ring of scatter, with
    /// one drop in four aimed right between them — the warning ring gives
    /// fair notice. Clamped inside the bounds; falls back to centre-field
    /// scatter if the fighters aren't around (they always are, in a fight).
    /// </summary>
    static Vector2 NearTheFight(float minRadius, float maxRadius, float margin)
    {
        Vector2 focus = Vector2.zero;
        var controller = BrawlController.Instance;
        if (controller != null && controller.Cyan != null && controller.Magenta != null)
        {
            Vector3 mid = (controller.Cyan.transform.position
                           + controller.Magenta.transform.position) * 0.5f;
            focus = new Vector2(mid.x, mid.z);
        }

        Vector2 spot;
        if (Random.value < 0.25f)
        {
            // Right into the duel.
            spot = focus + Random.insideUnitCircle * 0.8f;
        }
        else
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(minRadius, maxRadius);
            spot = focus + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        var half = BrawlStage.BoundsHalf;
        spot.x = Mathf.Clamp(spot.x, -(half.x - margin), half.x - margin);
        spot.y = Mathf.Clamp(spot.y, -(half.y - margin), half.y - margin);
        return spot;
    }
}
