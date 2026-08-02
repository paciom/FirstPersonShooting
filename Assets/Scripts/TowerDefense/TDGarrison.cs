using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The defender corps' barracks and standing orders: spawns bought robots
/// beside the Core, keeps the headcount under the cap, and owns the RALLY
/// FLAG — the one point every defender attack-moves to, fighting whatever
/// it meets on the way and holding there (CommanderUnit's own idle
/// auto-engage and leash do the holding; nothing here micro-manages).
///
/// Moving the flag re-orders every living defender, future hires included —
/// one flag, one mental model: the robots defend where the flag stands.
/// </summary>
public class TDGarrison : MonoBehaviour
{
    public static TDGarrison Instance { get; private set; }

    /// <summary>Standing headcount limit — a defense line, not a second army.</summary>
    public const int MaxDefenders = 8;

    [SerializeField] Vector3 _rally;
    GameObject _flag;
    RobotRoster _roster;

    public Vector3 Rally => _rally;

    void Awake()
    {
        Instance = this;
        _roster = GetComponentInParent<RobotRoster>();
        // Opening posture: hold the pocket mouth, one lane-bend up from the
        // Core — the last stand line until the player says otherwise.
        _rally = TDMap.CoreSite + new Vector3(0f, 0f, 10f);
        BuildFlag();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Living hires. Derived, never stored: defenders are the team-0
    /// CommanderUnits that aren't buildings' business — raiders are TDCreep
    /// on team 1, so the team check alone is the census.
    /// </summary>
    public static int DefenderCount
    {
        get
        {
            int count = 0;
            foreach (var unit in CommanderUnit.All)
                if (unit != null && unit.TeamId == 0 && unit.IsAlive)
                    count++;
            return count;
        }
    }

    /// <summary>
    /// Buy one. Pays, materializes beside the Core, marches it to the flag.
    /// False (nothing spent) at the cap or when the wallet says no.
    /// </summary>
    public bool TryHire(TDDefenderDefinition def)
    {
        if (def == null || DefenderCount >= MaxDefenders)
            return false;
        if (!TDEconomy.Spend(def.cost))
            return false;

        var entry = UnitCatalog.EntryOf(_roster, def.robotName);
        var pos = TDMap.CoreSite
            + new Vector3(Random.Range(-2f, 2f), 0f, 5.5f + Random.Range(0f, 1.5f));
        var unit = CommanderUnit.Build<CommanderUnit>($"TDDefender_{def.key}",
            entry.modelPrefab, entry.vehiclePrefab, teamId: 0, pos, yaw: 0f,
            armed: true, secondaryWeapon: def.secondaryWeapon,
            transformStages: entry.transformStages, paintAnchorHue: entry.paintAnchorHue);

        unit.sightRange = def.sightRange;
        unit.attackRange = def.attackRange;

        var shield = unit.GetComponent<EnergyShield>();
        shield.maxShield = def.maxShield;
        shield.Rematerialize();   // resync Current after the change, as ever

        unit.GetComponent<NavMeshAgent>().speed = def.speed;

        // The whole loadout takes the defender's damage number; each weapon
        // keeps its own cadence and projectile — the variety on camera.
        foreach (var weapon in unit.GetComponentsInChildren<Weapon>())
        {
            weapon.damage = def.damage;
            if (weapon is LaserBlaster blaster)
                blaster.shotsPerSecond = def.shotsPerSecond;
        }

        VfxUtil.Explosion(pos + Vector3.up * 1f, MatchAnnouncer.TeamColor(0), 0.9f);
        unit.IssueAttackMove(_rally);
        return true;
    }

    /// <summary>
    /// Plant the flag somewhere new and march the whole corps there. Units
    /// mid-fight re-acquire their target on the next think tick — an
    /// attack-move through an enemy IS a fight — so this never yanks a
    /// robot out of a firefight, it only re-points where the line stands.
    /// </summary>
    public void SetRally(Vector3 point)
    {
        _rally = point;
        BuildFlag();
        foreach (var unit in CommanderUnit.All)
            if (unit != null && unit.TeamId == 0 && unit.IsAlive)
                unit.IssueAttackMove(point);
    }

    /// <summary>
    /// The flag itself: a cyan banner pole with a ground ring, rebuilt on
    /// every move. A child of the session object, so teardown never has to
    /// know it existed.
    /// </summary>
    void BuildFlag()
    {
        if (_flag != null)
            Destroy(_flag);
        _flag = new GameObject("RallyFlag");
        _flag.transform.SetParent(transform, false);
        _flag.transform.position = _rally;

        var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(pole.GetComponent<Collider>());
        pole.name = "Pole";
        pole.transform.SetParent(_flag.transform, false);
        pole.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        pole.transform.localScale = new Vector3(0.12f, 2.2f, 0.12f);
        pole.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("Cmd_GunMetal", new Color(0.10f, 0.11f, 0.13f), 0.5f, 0.6f);

        var banner = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(banner.GetComponent<Collider>());
        banner.name = "Banner";
        banner.transform.SetParent(_flag.transform, false);
        banner.transform.localPosition = new Vector3(0.42f, 1.85f, 0f);
        banner.transform.localScale = new Vector3(0.72f, 0.5f, 0.06f);
        banner.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("TD_RallyBanner", new Color(0.2f, 0.9f, 1f), 1.6f);

        CommanderUnit.GlowQuad(_flag.transform, "RallyRing", "VFX/ring",
            new Color(0.2f, 0.9f, 1f), 0.9f, 3.4f, 0.05f);
    }
}
