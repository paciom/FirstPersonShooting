using UnityEngine;

/// <summary>
/// Turns one robot's combat events into requests for a conversation. Every
/// decision about whether the request is granted lives in ChatterDirector; this
/// class only reports what happened.
///
/// Self-bootstraps onto every shielded character and rescans for late spawns, the
/// same pattern FloatingShieldBar uses, so reinforcements and clone decoys start
/// talking without any scene wiring.
///
/// The periodic think is jittered per robot. Nine robots scanning on the same
/// frame would spike, and worse, they would all notice the same enemy on the same
/// tick and then lose the race to the director's cue cooldown — so the fastest
/// robot would always be the one who calls contact.
/// </summary>
[RequireComponent(typeof(EnergyShield))]
public class RobotChatter : MonoBehaviour
{
    const float ThinkInterval = 1.6f;
    const float SpotRange = 85f;
    const float BanterAfterQuietSeconds = 12f;
    const float CriticalShield = 0.3f;

    EnergyShield _shield;
    TransformMode _transformMode;
    Transform _lastSpotted;
    float _nextThink;
    float _quietSince;

    // ------------------------------------------------------------- bootstrap

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("RobotChatterSpawner");
        go.AddComponent<Spawner>();
    }

    class Spawner : MonoBehaviour
    {
        float _nextScan;

        void Update()
        {
            if (Time.time < _nextScan)
                return;
            _nextScan = Time.time + 2f;
            foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
                if (shield.GetComponent<RobotChatter>() == null)
                    shield.gameObject.AddComponent<RobotChatter>();
        }
    }

    // ------------------------------------------------------------- lifecycle

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
        _transformMode = GetComponent<TransformMode>();
        _nextThink = Time.time + Random.Range(0f, ThinkInterval);
        _quietSince = Time.time;
    }

    void OnEnable()
    {
        if (_shield != null)
        {
            _shield.OnDamaged += HandleDamaged;
            _shield.OnDeRezzed += HandleDeRezzed;
        }
        if (_transformMode != null)
            _transformMode.OnFoldStarted += HandleFold;
    }

    void OnDisable()
    {
        if (_shield != null)
        {
            _shield.OnDamaged -= HandleDamaged;
            _shield.OnDeRezzed -= HandleDeRezzed;
        }
        if (_transformMode != null)
            _transformMode.OnFoldStarted -= HandleFold;
    }

    // ---------------------------------------------------------------- reactive

    void HandleDamaged(float damage, Vector3 hitPoint)
    {
        _quietSince = Time.time;
        var attacker = _shield.LastAttacker;

        // Below the threshold this is no longer "I'm hit", it is "I'm about to
        // lose". Asking for help instead of reporting damage is what a teammate
        // can actually act on, so prefer it when anyone is around to hear.
        if (_shield.Normalized <= CriticalShield)
        {
            string cue = Random.value < 0.55f ? "REQUEST_HELP" : "SHIELD_CRITICAL";
            Speak(cue, attacker);
            return;
        }
        Speak("TAKING_DAMAGE", attacker);
    }

    void HandleDeRezzed()
    {
        var killer = _shield.LastAttacker;

        // The victim says nothing — it is gone. Two other calls happen instead:
        // its team reports the loss, and the killer's team confirms it.
        var mourner = NearestLiving(_shield.teamId, transform);
        if (mourner != null)
        {
            var mournerShield = mourner.GetComponent<EnergyShield>();
            ChatterDirector.Request("ALLY_DOWN", mourner, transform, _shield.teamId,
                mournerShield != null ? mournerShield.Normalized : 1f);
        }

        if (killer != null)
        {
            var killerShield = killer.GetComponent<EnergyShield>();
            if (killerShield != null && killerShield.teamId != _shield.teamId)
            {
                ChatterDirector.Request("KILL_CONFIRM", killer, transform,
                    killerShield.teamId, killerShield.Normalized);
            }
        }
    }

    void HandleFold(bool toVehicle)
    {
        Speak("TRANSFORM", _lastSpotted);
    }

    // ----------------------------------------------------------------- periodic

    void Update()
    {
        if (Time.time < _nextThink || _shield == null || _shield.IsDown)
            return;
        _nextThink = Time.time + ThinkInterval + Random.Range(-0.3f, 0.5f);

        if (GameModeController.Instance == null
            || (GameModeController.Instance.Mode != GameMode.PlayerVsAI
                && GameModeController.Instance.Mode != GameMode.AIvAI))
            return;

        var enemy = NearestEnemyInSight();
        if (enemy != null)
        {
            _quietSince = Time.time;
            // Only call it out on the transition. A robot that can see an enemy
            // for twenty seconds should not keep announcing it.
            if (enemy != _lastSpotted)
            {
                _lastSpotted = enemy;
                Speak("CONTACT", enemy);
            }
            return;
        }

        _lastSpotted = null;
        if (NearbyCrate(out var crate))
        {
            _quietSince = Time.time;
            Speak("OBJECTIVE", crate);
            return;
        }

        // Nothing happening: fill the silence, which is the only time idle
        // banter is welcome.
        if (Time.time - _quietSince >= BanterAfterQuietSeconds)
        {
            _quietSince = Time.time;
            Speak("BANTER", null);
        }
    }

    void Speak(string cue, Transform about)
    {
        ChatterDirector.Request(cue, transform, about, _shield.teamId, _shield.Normalized);
    }

    // ----------------------------------------------------------------- sensing

    /// <summary>
    /// Nearest live enemy with clear line of sight. The linecast matters: without
    /// it robots call contact through walls, which reads as cheating to anyone
    /// watching an AI-v-AI match.
    /// </summary>
    Transform NearestEnemyInSight()
    {
        Transform best = null;
        float bestDistance = SpotRange * SpotRange;
        Vector3 eye = transform.position + Vector3.up * 1.4f;

        foreach (var other in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (other == null || other.IsDown || other.teamId == _shield.teamId)
                continue;
            if (other.transform == transform)
                continue;
            float distance = Vector3.SqrMagnitude(other.transform.position - transform.position);
            if (distance >= bestDistance)
                continue;

            Vector3 target = other.transform.position + Vector3.up * 1.4f;
            if (Physics.Linecast(eye, target, out var hit)
                && hit.transform.root != other.transform.root)
                continue;   // something solid in the way

            bestDistance = distance;
            best = other.transform;
        }
        return best;
    }

    bool NearbyCrate(out Transform crate)
    {
        crate = null;
        float best = 45f * 45f;
        foreach (var drop in FindObjectsByType<TreasureDrop>(FindObjectsSortMode.None))
        {
            if (drop == null)
                continue;
            float distance = Vector3.SqrMagnitude(drop.transform.position - transform.position);
            if (distance < best)
            {
                best = distance;
                crate = drop.transform;
            }
        }
        return crate != null;
    }

    static Transform NearestLiving(int teamId, Transform excluding)
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield == null || shield.IsDown || shield.teamId != teamId)
                continue;
            if (shield.transform == excluding)
                continue;
            float distance = Vector3.SqrMagnitude(shield.transform.position - excluding.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = shield.transform;
            }
        }
        return best;
    }
}
