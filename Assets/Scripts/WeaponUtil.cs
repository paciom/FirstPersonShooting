using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared helpers for the expanded arsenal: enemy queries, splash damage,
/// chain lightning, glowing primitives, paint splats, and the mimic-memory
/// registry (which weapon flavour last hit a character — the Mimic Cube reads
/// it). Static so weapons, projectiles, and zones all share one implementation.
/// </summary>
public static class WeaponUtil
{
    // ------------------------------------------------------------------ enemies

    public static readonly List<EnergyShield> ScratchShields = new List<EnergyShield>();

    /// <summary>All live enemy shields for a team (allocates a shared scratch list — consume immediately).</summary>
    public static List<EnergyShield> FindEnemies(int teamId)
    {
        ScratchShields.Clear();
        foreach (var shield in Object.FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.teamId != teamId && !shield.IsDown && shield.gameObject.activeInHierarchy)
                ScratchShields.Add(shield);
        }
        return ScratchShields;
    }

    public static EnergyShield NearestEnemy(Vector3 position, int teamId, float maxRange = float.MaxValue,
        Transform exclude = null)
    {
        EnergyShield best = null;
        float bestSqr = maxRange * maxRange;
        foreach (var shield in Object.FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.teamId == teamId || shield.IsDown || !shield.gameObject.activeInHierarchy)
                continue;
            if (exclude != null && shield.transform.root == exclude)
                continue;
            float d = (shield.transform.position - position).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = shield;
            }
        }
        return best;
    }

    /// <summary>Aim point for a shield (chest height).</summary>
    public static Vector3 Center(EnergyShield shield) => shield.transform.position + Vector3.up * 1.2f;

    // ------------------------------------------------------------------ damage

    /// <summary>
    /// Static version of Weapon.ApplyHit for projectiles and zones: damages an
    /// opposing shield or an arena block and pops an impact burst.
    /// </summary>
    public static void ResolveHit(RaycastHit hit, float damage, int teamId, Transform ownerRoot, Color color)
    {
        var shield = hit.transform.root.GetComponent<EnergyShield>();
        if (shield != null && shield.teamId != teamId)
            shield.TakeHit(damage, hit.point, ownerRoot);
        else
            DamageProp(hit.collider, damage, hit.point);
        VfxUtil.ImpactBurst(hit.point + hit.normal * 0.05f, color);
    }

    /// <summary>
    /// Damage whatever non-character thing a shot landed on: a cover block or
    /// an airdrop crate. Every weapon resolves props through here, so anything
    /// shootable that isn't a robot only has to be taught to this one method.
    /// </summary>
    public static void DamageProp(Collider collider, float damage, Vector3 point)
    {
        if (collider == null)
            return;
        var block = collider.GetComponentInParent<ArenaBlock>();
        if (block != null)
        {
            block.TakeHit(damage, point);
            return;
        }
        // Tank Raid's street furniture: brick crumbles, crates splinter,
        // stone just takes the shot.
        var street = collider.GetComponentInParent<TankBlock>();
        if (street != null)
        {
            street.TakeHit(damage, point);
            return;
        }
        collider.GetComponentInParent<TreasureDrop>()?.TakeHit(damage, point);
    }

    /// <summary>PlasmaOrb-style splash: falloff damage to enemy shields and cover blocks in a radius.</summary>
    public static void SplashDamage(Vector3 position, float radius, float damage, int teamId, Transform ownerRoot)
    {
        foreach (var shield in Object.FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.teamId == teamId || shield.IsDown)
                continue;
            float d = Vector3.Distance(position, Center(shield));
            if (d <= radius)
                shield.TakeHit(damage * (1f - d / radius), Center(shield), ownerRoot);
        }

        foreach (var col in Physics.OverlapSphere(position, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            // Cover blocks and airdrop crates both take falloff damage — which
            // is how a lobbed plasma orb can set off a Scrap Mine from cover.
            float d = Vector3.Distance(position, col.transform.position);
            DamageProp(col, damage * Mathf.Clamp01(1f - d / radius), position);
        }
    }

    /// <summary>
    /// Multi-strand electric arc that reads as ONE bolt: a single jagged path
    /// (displacement peaks mid-span, pinned at both ends, and scales with arc
    /// length) drawn as a white-hot core plus two colored strands hugging the
    /// same path a few centimeters off. Independent per-strand wander turns
    /// into scribble-spaghetti — don't go back to that.
    /// </summary>
    public static void LightningArc(Vector3 from, Vector3 to, Color color, float life = 0.16f)
    {
        const int Segments = 12;
        float amplitude = Mathf.Min(0.45f, Vector3.Distance(from, to) * 0.09f);

        var path = new Vector3[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            path[i] = Vector3.Lerp(from, to, t)
                + Random.insideUnitSphere * (amplitude * Mathf.Sin(t * Mathf.PI));
        }

        FadingLine.SpawnPath(path, Color.white, 0.025f, life, 2.8f);
        FadingLine.SpawnPath(OffsetPath(path, 0.06f), color, 0.045f, life * 1.15f, 1.6f);
        FadingLine.SpawnPath(OffsetPath(path, 0.1f), color, 0.03f, life * 0.8f, 1.4f);
    }

    static Vector3[] OffsetPath(Vector3[] source, float amount)
    {
        var points = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++)
            points[i] = source[i] + Random.insideUnitSphere * amount;
        return points;
    }

    /// <summary>
    /// Chain lightning: zap the first target, then arc to up to `hops` more
    /// enemies within `hopRange` of the previous link. Returns targets hit.
    /// </summary>
    public static int ChainLightning(Vector3 origin, EnergyShield first, float damage, int hops, float hopRange,
        int teamId, Transform ownerRoot, Color color)
    {
        int hit = 0;
        var previous = origin;
        var current = first;
        var alreadyHit = new List<Transform>();
        while (current != null && hit <= hops)
        {
            Vector3 point = Center(current);
            LightningArc(previous, point, color, 0.18f);
            VfxUtil.ImpactBurst(point, color);
            current.TakeHit(damage, point, ownerRoot);
            alreadyHit.Add(current.transform.root);
            hit++;

            previous = point;
            EnergyShield next = null;
            float bestSqr = hopRange * hopRange;
            foreach (var shield in Object.FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
            {
                if (shield.teamId == teamId || shield.IsDown || alreadyHit.Contains(shield.transform.root))
                    continue;
                float d = (shield.transform.position - point).sqrMagnitude;
                if (d < bestSqr)
                {
                    bestSqr = d;
                    next = shield;
                }
            }
            current = next;
        }
        return hit;
    }

    // ------------------------------------------------------------------ visuals

    /// <summary>A glowing collider-less primitive — the basic building block for orbs, shells, and blobs.</summary>
    public static GameObject GlowPrimitive(PrimitiveType type, Vector3 position, Vector3 scale, Color color,
        float intensity = 3f)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().material = VfxUtil.MakeGlowMaterial(color, intensity);
        return go;
    }

    /// <summary>A ghostly additive shell (domes, bubbles, freeze shells) — glows but see-through.</summary>
    public static GameObject GhostShell(PrimitiveType type, Vector3 position, Vector3 scale, Color color,
        float intensity = 0.8f)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().material = VfxUtil.MakeAdditiveMaterial(null, color, intensity);
        return go;
    }

    // Paint splats are capped so a long paint fight can't bury the frame rate.
    static readonly Queue<GameObject> Splats = new Queue<GameObject>();
    const int MaxSplats = 90;

    /// <summary>Flat coloured splat pressed onto a surface; oldest splats recycle out.</summary>
    public static void PaintSplat(Vector3 point, Vector3 normal, Color color, float size, float lifeSeconds = 30f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "PaintSplat";
        go.transform.position = point + normal * 0.02f;
        go.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        go.transform.localScale = Vector3.one * size * Random.Range(0.8f, 1.3f);
        // The puff sprite gives the splat an irregular blob outline — an
        // untextured quad renders as a plain colored SQUARE on the floor.
        go.GetComponent<MeshRenderer>().material =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.PuffTexture, color, 1.2f);
        Object.Destroy(go, lifeSeconds);

        Splats.Enqueue(go);
        while (Splats.Count > MaxSplats)
        {
            var old = Splats.Dequeue();
            if (old != null)
                Object.Destroy(old);
        }
    }

    /// <summary>
    /// Layered fireball dressing for big flaming projectiles (Comet Sling,
    /// Meteor Caller): white-hot heart, faint corona shell, and a stream of
    /// ember sparks shed behind in world space.
    /// </summary>
    public static void DressAsFireball(GenericBolt bolt, Color color)
    {
        var heart = GlowPrimitive(PrimitiveType.Sphere, bolt.transform.position,
            Vector3.one * 0.4f, Color.white, 3f);
        heart.transform.SetParent(bolt.transform, true);

        var corona = GhostShell(PrimitiveType.Sphere, bolt.transform.position,
            Vector3.one * 1.05f, color, 0.2f);
        corona.transform.SetParent(bolt.transform, true);

        var psGo = new GameObject("Embers");
        psGo.transform.SetParent(bolt.transform, false);
        var ps = psGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = color * 1.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.gravityModifier = 0.25f;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.35f;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 45f;
        psGo.GetComponent<ParticleSystemRenderer>().material =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.SparkTexture, Color.white, 1.5f);
        ps.Play();
    }

    // ------------------------------------------------------------------ mimic memory

    static readonly Dictionary<Transform, BoltSpec> LastHitBy = new Dictionary<Transform, BoltSpec>();

    /// <summary>Called by GenericBolt when it damages a shield, so the Mimic Cube can copy the flavour.</summary>
    public static void RecordHit(Transform victimRoot, BoltSpec spec)
    {
        if (victimRoot != null && spec != null)
            LastHitBy[victimRoot] = spec;
    }

    /// <summary>The spec of the last bolt that hit this character, or null.</summary>
    public static BoltSpec LastSpecAgainst(Transform victimRoot)
    {
        return victimRoot != null && LastHitBy.TryGetValue(victimRoot, out var spec) ? spec : null;
    }
}
