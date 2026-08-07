using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a wreck leaves behind: a WEAPON POD or a REPAIR KIT, sitting on the
/// ground where the raider stopped.
///
/// Collected by PROXIMITY rather than by a trigger. Every pawn in this mode
/// moves by writing its own transform, so nothing here generates the physics
/// events OnTriggerEnter needs — a trigger collider would be a pickup that can
/// only ever be collected by accident. Distance to the hero, once a frame, is
/// the rule that actually works.
///
/// Only the hero can collect. A raider driving over the pod that fell out of its
/// team-mate would be an invisible loss the player never sees happen, and the
/// drops exist to reward the shot that earned them.
///
/// They expire by falling off the bottom of the screen, not on a timer: the one
/// thing that can put a pickup out of reach in a scroller is the scroll, and a
/// clock would take one away while it was still on screen and still winnable.
/// </summary>
public class TankPickup : MonoBehaviour
{
    public enum Kind
    {
        /// <summary>One gun from <see cref="TankArsenal"/>, on the clock.</summary>
        Weapon,
        /// <summary>Shield back. The mode's only healing.</summary>
        Repair,
        /// <summary>A recruit beacon: one tank changes sides and drives with you.</summary>
        Recruit,
    }

    static readonly List<TankPickup> Live = new List<TankPickup>();

    public static IReadOnlyList<TankPickup> All => Live;

    public static void DespawnAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
            if (Live[i] != null)
                Destroy(Live[i].gameObject);
        Live.Clear();
    }

    const float Reach = 3.2f;
    const float BobHeight = 0.28f;
    const float SpinDegrees = 95f;

    static readonly Color PodColor = new Color(1f, 0.75f, 0.2f);
    static readonly Color KitColor = new Color(0.35f, 1f, 0.6f);
    static readonly Color RecruitColor = new Color(0.35f, 0.7f, 1f);

    /// <summary>The colour a kind is read by from across the field.</summary>
    public static Color ColorOf(Kind sort) =>
        sort == Kind.Weapon ? PodColor : sort == Kind.Repair ? KitColor : RecruitColor;

    public Kind Sort { get; private set; }

    /// <summary>Raised when the hero reaches it — the mode grants the reward and says so.</summary>
    public System.Action<TankPickup> OnCollected;

    Transform _spinner;
    float _phase;

    void OnEnable()
    {
        if (!Live.Contains(this))
            Live.Add(this);
    }

    void OnDisable() => Live.Remove(this);

    public static TankPickup Drop(Transform parent, Vector3 where, Kind sort)
    {
        var go = new GameObject($"Pickup_{sort}");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(where.x, 0f, where.z);
        var pickup = go.AddComponent<TankPickup>();
        pickup.Sort = sort;
        pickup.Compose();
        return pickup;
    }

    /// <summary>
    /// A floating shape and the light that finds it. Read at eighteen metres up
    /// by SILHOUETTE and COLOUR rather than by any detail: the pod is an amber
    /// box turning on its corner, the kit a green cross. Nothing here carries a
    /// collider — see the class note.
    /// </summary>
    void Compose()
    {
        Color color = ColorOf(Sort);

        _spinner = new GameObject("Spinner").transform;
        _spinner.SetParent(transform, false);
        _spinner.localPosition = new Vector3(0f, 1.1f, 0f);

        var glow = ArenaMaterials.Emissive($"tank-pickup-{Sort}", color,
            // Under 2.5: past that the bloom takes any colour to white, and three
            // pickups that all read white are three the player cannot tell apart
            // from across the field.
            1.8f);

        switch (Sort)
        {
            // A crate on its corner.
            case Kind.Weapon:
                Shape(_spinner, PrimitiveType.Cube, Vector3.zero, Vector3.one * 0.85f, glow,
                    new Vector3(35f, 0f, 35f));
                break;

            // A cross.
            case Kind.Repair:
                Shape(_spinner, PrimitiveType.Cube, Vector3.zero,
                    new Vector3(1.1f, 0.34f, 0.34f), glow, null);
                Shape(_spinner, PrimitiveType.Cube, Vector3.zero,
                    new Vector3(0.34f, 1.1f, 0.34f), glow, null);
                break;

            // A chevron pointing up the field: the shape armies mark their own
            // vehicles with, and the only pickup here that gives you one.
            default:
                Shape(_spinner, PrimitiveType.Cube, new Vector3(-0.26f, 0f, 0f),
                    new Vector3(0.9f, 0.3f, 0.3f), glow, new Vector3(0f, 40f, 0f));
                Shape(_spinner, PrimitiveType.Cube, new Vector3(0.26f, 0f, 0f),
                    new Vector3(0.9f, 0.3f, 0.3f), glow, new Vector3(0f, -40f, 0f));
                break;
        }

        // A ground ring, so the thing that matters — WHERE it is — survives the
        // shape bobbing about above it.
        Shape(transform, PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f),
            new Vector3(2.4f, 0.02f, 2.4f), glow, null);

        var light = new GameObject("Beacon").AddComponent<Light>();
        light.transform.SetParent(transform, false);
        light.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        light.type = LightType.Point;
        light.color = color;
        light.intensity = 3.2f;
        light.range = 8f;
        light.shadows = LightShadows.None;
    }

    static void Shape(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale,
        Material material, Vector3? euler)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "Piece";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        if (euler.HasValue)
            go.transform.localRotation = Quaternion.Euler(euler.Value);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>
    /// Turn, bob, and watch for the hero. Driven from the mode rather than from
    /// here so that a pickup cannot be collected during the beat between a
    /// de-rez and a continue, when the hero is not really on the field.
    /// </summary>
    public void Tick(TankPawn hero, float dt)
    {
        _phase += dt;
        if (_spinner != null)
        {
            _spinner.localRotation = Quaternion.Euler(0f, _phase * SpinDegrees, 0f);
            _spinner.localPosition = new Vector3(0f, 1.1f + Mathf.Sin(_phase * 2.6f) * BobHeight, 0f);
        }

        if (TankField.FallenBehind(transform.position))
        {
            Destroy(gameObject);
            return;
        }

        if (hero == null || hero.IsDown)
            return;
        Vector3 gap = hero.transform.position - transform.position;
        gap.y = 0f;
        if (gap.sqrMagnitude > Reach * Reach)
            return;

        VfxUtil.SpawnBurst(transform.position + Vector3.up, ColorOf(Sort), 16, 5f, 0.14f);
        OnCollected?.Invoke(this);
        Destroy(gameObject);
    }
}
