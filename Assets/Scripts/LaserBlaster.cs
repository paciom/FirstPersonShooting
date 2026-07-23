using UnityEngine;

/// <summary>
/// Fires visible glowing laser bolts. Projectiles have travel time on purpose:
/// they read far better on camera than hitscan, for players and viewers alike.
/// Bolts are built procedurally during the greybox phase (no prefab needed).
/// </summary>
public class LaserBlaster : MonoBehaviour
{
    [Header("Firing")]
    public float shotsPerSecond = 5f;
    public float boltSpeed = 40f;
    public float boltDamage = 20f;
    public Color boltColor = new Color(0.2f, 0.9f, 1f);

    [Tooltip("Bolts spawn here; defaults to this transform.")]
    public Transform muzzle;

    [Tooltip("Root of the character firing — its colliders are ignored by the bolt.")]
    public Transform ownerRoot;

    float _nextFireTime;
    int _teamId;
    Light _muzzleLight;

    void Awake()
    {
        if (muzzle == null) muzzle = transform;
        if (ownerRoot == null) ownerRoot = transform.root;

        var shield = ownerRoot.GetComponent<EnergyShield>();
        _teamId = shield != null ? shield.teamId : -1;

        // Brief muzzle flash light — cheap juice that reads on camera.
        var lightGo = new GameObject("MuzzleLight");
        lightGo.transform.SetParent(muzzle, false);
        _muzzleLight = lightGo.AddComponent<Light>();
        _muzzleLight.type = LightType.Point;
        _muzzleLight.color = boltColor;
        _muzzleLight.intensity = 3f;
        _muzzleLight.range = 4f;
        _muzzleLight.enabled = false;
    }

    public bool TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return false;

        _nextFireTime = Time.time + 1f / shotsPerSecond;
        LaserBolt.Spawn(muzzle.position, direction.normalized, boltSpeed, boltDamage, boltColor, _teamId, ownerRoot);

        _muzzleLight.enabled = true;
        Invoke(nameof(DisableMuzzleLight), 0.05f);
        return true;
    }

    void DisableMuzzleLight() => _muzzleLight.enabled = false;
}
