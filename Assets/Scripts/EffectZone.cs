using System.Collections.Generic;
using UnityEngine;

public enum ZoneParticles { None, Snow, Rain, Sand, Sparkle }

/// <summary>
/// A timed spherical area effect: visual dome + optional weather particles +
/// a periodic tick over characters inside (damage, slows, statuses via
/// callback). Also powers time bubbles — GenericBolt asks TimeScaleAt() so
/// projectiles visibly crawl through slow-time domes.
/// </summary>
public class EffectZone : MonoBehaviour
{
    static readonly List<EffectZone> Active = new List<EffectZone>();

    public float radius = 4f;
    public float duration = 4f;
    public int teamId;
    public Transform ownerRoot;
    public Color color = Color.white;

    [Tooltip("Time bubbles set this below 1 — projectiles inside slow down.")]
    public float timeScale = 1f;
    public bool affectAllTeams;
    public float tickDamagePerSecond;
    /// <summary>Called every tick for each character inside (enemies only unless affectAllTeams).</summary>
    public System.Action<EffectZone, EnergyShield> onCharacterTick;

    const float TickInterval = 0.3f;
    float _age;
    float _nextTick;
    GameObject _dome;

    /// <summary>Slowest time bubble covering this position (1 = normal time).</summary>
    public static float TimeScaleAt(Vector3 position)
    {
        float scale = 1f;
        foreach (var zone in Active)
        {
            if (zone != null && zone.timeScale < scale
                && (position - zone.transform.position).sqrMagnitude <= zone.radius * zone.radius)
                scale = zone.timeScale;
        }
        return scale;
    }

    public static EffectZone Spawn(Vector3 position, float radius, float duration, Color color,
        ZoneParticles particles, int teamId, Transform ownerRoot, bool showDome = true)
    {
        var go = new GameObject("EffectZone");
        go.transform.position = position;
        var zone = go.AddComponent<EffectZone>();
        zone.radius = radius;
        zone.duration = duration;
        zone.color = color;
        zone.teamId = teamId;
        zone.ownerRoot = ownerRoot;

        if (showDome)
        {
            // Very faint: domes overlap in real fights and additively stack —
            // 0.35 was enough for two overlapping domes to white out the camera.
            zone._dome = WeaponUtil.GhostShell(PrimitiveType.Sphere, position, Vector3.one * radius * 2f, color, 0.12f);
            zone._dome.transform.SetParent(go.transform, true);
        }

        if (particles != ZoneParticles.None)
            zone.BuildParticles(particles);

        return zone;
    }

    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    void Update()
    {
        _age += Time.deltaTime;
        if (_age >= duration)
        {
            Destroy(gameObject);
            return;
        }

        // Dome breathes gently and fades near the end.
        if (_dome != null)
        {
            float pulse = 1f + Mathf.Sin(_age * 3f) * 0.02f;
            float fade = Mathf.Clamp01((duration - _age) / 0.6f);
            _dome.transform.localScale = Vector3.one * radius * 2f * pulse * Mathf.Lerp(0.6f, 1f, fade);
        }

        if (Time.time < _nextTick)
            return;
        _nextTick = Time.time + TickInterval;

        foreach (var shield in Object.FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.IsDown || !shield.gameObject.activeInHierarchy)
                continue;
            if (!affectAllTeams && shield.teamId == teamId)
                continue;
            if ((shield.transform.position - transform.position).sqrMagnitude > radius * radius)
                continue;

            if (tickDamagePerSecond > 0f)
                shield.TakeHit(tickDamagePerSecond * TickInterval, WeaponUtil.Center(shield), ownerRoot);
            onCharacterTick?.Invoke(this, shield);
        }
    }

    void BuildParticles(ZoneParticles style)
    {
        var go = new GameObject("ZoneParticles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = style == ZoneParticles.Rain || style == ZoneParticles.Snow
            ? Vector3.up * radius * 0.8f
            : Vector3.zero;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = color * 1.6f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.85f;

        var emission = ps.emission;
        emission.enabled = true;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        // Soft round glow for drifting styles; the streak sprite suits stretched rain.
        renderer.material = VfxUtil.MakeAdditiveMaterial(
            style == ZoneParticles.Rain ? VfxUtil.SparkTexture : VfxUtil.GlowTexture, Color.white, 1.5f);

        switch (style)
        {
            case ZoneParticles.Snow:
                main.startLifetime = 1.6f;
                main.startSpeed = 0.1f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                main.gravityModifier = 0.12f;
                emission.rateOverTime = 45f;
                break;
            case ZoneParticles.Rain:
                main.startLifetime = 0.8f;
                main.startSpeed = 0.1f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                main.gravityModifier = 1.6f;
                emission.rateOverTime = 90f;
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 6f;
                renderer.velocityScale = 0.08f;
                break;
            case ZoneParticles.Sand:
                main.startLifetime = 1.1f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                emission.rateOverTime = 70f;
                var orbit = ps.velocityOverLifetime;
                orbit.enabled = true;
                orbit.orbitalY = 2.4f;
                break;
            case ZoneParticles.Sparkle:
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
                main.gravityModifier = -0.05f;
                emission.rateOverTime = 35f;
                break;
        }

        ps.Play();
    }
}
