using System.Collections;
using UnityEngine;

/// <summary>
/// Handles the visual "de-rez → wait → re-materialize at spawn" cycle when the
/// EnergyShield on this object hits zero. Scales the body down in a burst of
/// light, disables control, then teleports back to the spawn point and scales
/// back up. Works for the player, AI bots, and target dummies.
/// </summary>
[RequireComponent(typeof(EnergyShield))]
public class DeRezEffect : MonoBehaviour
{
    [Tooltip("Visual body to shrink; falls back to this transform's first child or itself.")]
    public Transform body;
    public float respawnDelay = 3f;
    public Color burstColor = new Color(0.2f, 0.9f, 1f);

    EnergyShield _shield;
    Vector3 _spawnPosition;
    Quaternion _spawnRotation;
    Vector3 _bodyScale;

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;

        if (body == null)
            body = transform.childCount > 0 ? transform.GetChild(0) : transform;
        _bodyScale = body.localScale;

        _shield.OnDeRezzed += HandleDeRez;
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDeRezzed -= HandleDeRez;
    }

    void HandleDeRez()
    {
        StartCoroutine(DeRezRoutine());
    }

    IEnumerator DeRezRoutine()
    {
        VfxUtil.SpawnBurst(body.position + Vector3.up * 0.8f, burstColor, 40, 5f);
        SetControlEnabled(false);

        // Shrink into nothing — dissolving into light, never dying.
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            body.localScale = _bodyScale * (1f - t);
            yield return null;
        }
        body.localScale = Vector3.zero;

        yield return new WaitForSeconds(respawnDelay);

        // Re-materialize at the spawn point.
        var motor = GetComponent<CharacterMotor>();
        if (motor != null)
            motor.Teleport(_spawnPosition);
        else
            transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;

        VfxUtil.SpawnBurst(_spawnPosition + Vector3.up * 0.8f, burstColor, 25, 3f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            body.localScale = _bodyScale * t;
            yield return null;
        }
        body.localScale = _bodyScale;

        SetControlEnabled(true);
        _shield.Rematerialize();
    }

    void SetControlEnabled(bool enabled)
    {
        var playerBrain = GetComponent<PlayerBrain>();
        if (playerBrain != null) playerBrain.enabled = enabled;

        var aiBrain = GetComponent<AIBrain>();
        if (aiBrain != null) aiBrain.SetActive(enabled);

        foreach (var col in GetComponentsInChildren<Collider>())
            if (!(col is CharacterController))
                col.enabled = enabled;
    }
}
