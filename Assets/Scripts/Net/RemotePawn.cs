using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The other player's robot, as seen on this client: a stripped clone of the
/// local player rig (built by <see cref="NetMatch"/>) that renders 20 Hz
/// position snapshots ~120 ms in the past and replays fire/transform events
/// on its own weapon components.
///
/// Its shield is a remote proxy — see EnergyShield.remoteProxy — so local
/// hits show feedback but only the owning client's broadcasts move the value.
/// The CharacterController stays enabled purely as the hit collider; the
/// CharacterMotor is disabled and the transform is set directly.
/// </summary>
public class RemotePawn : MonoBehaviour
{
    /// <summary>Render this far behind the newest snapshot so there is almost
    /// always a pair to interpolate between at 20 Hz.</summary>
    const float InterpolationDelay = 0.12f;
    /// <summary>Faster than any robot can run, slower than a respawn jump:
    /// above this implied speed, a gap is a teleport, not movement.</summary>
    const float TeleportSpeed = 25f;
    /// <summary>How long a fire intent keeps the trigger held on the mirror.
    /// Longer than the 0.05 s send interval so one lost packet doesn't stutter
    /// a beam, short enough that a released trigger stops promptly.</summary>
    const float FireHold = 0.15f;

    struct Snapshot
    {
        public float time;
        public Vector3 position;
        public float yaw;
        public float pitch;
    }

    readonly List<Snapshot> _buffer = new List<Snapshot>(32);
    Transform _head;
    WeaponLoadout _loadout;
    TransformMode _vehicle;
    EnergyShield _shield;
    int _lastSeq = -1;
    int _holdSlot = -1;
    Vector3 _holdDirection;
    float _holdUntil;

    void Awake()
    {
        var motor = GetComponent<CharacterMotor>();
        _head = motor != null ? motor.head : null;
        _loadout = GetComponent<WeaponLoadout>();
        _vehicle = GetComponent<TransformMode>();
        _shield = GetComponent<EnergyShield>();
    }

    /// <summary>
    /// A snapshot off the unreliable channel. Sequence numbers are what make
    /// that channel usable: it may reorder, and a late arrival appended after
    /// a newer one would drag the mirror backwards through space.
    /// </summary>
    public void PushSnapshot(int sequence, Vector3 position, float yaw, float pitch)
    {
        if (sequence <= _lastSeq)
            return;
        _lastSeq = sequence;
        _buffer.Add(new Snapshot
        {
            time = Time.unscaledTime,
            position = position,
            yaw = yaw,
            pitch = pitch,
        });
        // Anything older than a second can never be rendered again.
        float horizon = Time.unscaledTime - 1f;
        while (_buffer.Count > 2 && _buffer[0].time < horizon)
            _buffer.RemoveAt(0);
    }

    void Update()
    {
        if (_buffer.Count == 0)
            return;

        float renderTime = Time.unscaledTime - InterpolationDelay;

        // Find the pair bracketing renderTime; hold the newest when we're
        // ahead of the buffer (packet gap) rather than extrapolating.
        Snapshot from = _buffer[0], to = _buffer[_buffer.Count - 1];
        for (int i = 0; i < _buffer.Count - 1; i++)
        {
            if (_buffer[i].time <= renderTime && renderTime <= _buffer[i + 1].time)
            {
                from = _buffer[i];
                to = _buffer[i + 1];
                break;
            }
        }
        if (renderTime >= to.time)
            from = to;

        float span = to.time - from.time;
        float k = span > 0.0001f ? Mathf.Clamp01((renderTime - from.time) / span) : 1f;

        // Respawn teleports must not become a screaming slide across the map.
        // Judged by implied SPEED, not raw distance: after packet loss a
        // legitimately-moving robot covers a lot of ground between two
        // snapshots, and jumping it there would stutter ordinary running.
        bool teleported = span > 0.0001f
            && (to.position - from.position).magnitude / span > TeleportSpeed;
        transform.position = teleported ? to.position : Vector3.Lerp(from.position, to.position, k);

        float yaw = Mathf.LerpAngle(from.yaw, to.yaw, k);
        float pitch = Mathf.LerpAngle(from.pitch, to.pitch, k);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (_head != null)
            _head.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // Held-trigger weapons (beams, charge rails) expect TryFire EVERY
        // frame the trigger is down; 20 Hz intents alone would make a beam
        // flicker and a rail never finish charging. Each intent extends the
        // hold, and silence lets it lapse.
        if (Time.unscaledTime < _holdUntil)
            FireHeld();
    }

    void FireHeld()
    {
        var weapons = _loadout != null ? _loadout.Available : null;
        if (weapons == null || _holdSlot < 0 || _holdSlot >= weapons.Length
            || weapons[_holdSlot] == null)
            return;
        if (_shield != null && _shield.IsDown)
            return;
        // Point the mirror's turret where its owner is shooting. Only fire
        // intent crosses the wire, so this is the one moment the mirror learns
        // where the other player's gun is aimed — and a held beam has to keep
        // re-aiming as they track, not freeze at the first frame's direction.
        if (_vehicle != null && _vehicle.IsVehicle && _vehicle.Turret != null)
            _vehicle.Turret.AimAlong(_holdDirection);
        weapons[_holdSlot].TryFire(_holdDirection);
    }

    /// <summary>Replay the owner's fire intent on this mirror's own weapons.
    /// Slot indexes the same WeaponLoadout.Available both clients share
    /// (no airdrops in online v1, so the lists match). The intent also HOLDS
    /// the trigger briefly — see FireHold — so continuous weapons work.</summary>
    public void RemoteFire(int slot, Vector3 direction)
    {
        _holdSlot = slot;
        _holdDirection = direction.normalized;
        _holdUntil = Time.unscaledTime + FireHold;
        FireHeld();
    }

    /// <summary>
    /// Absolute form, not a toggle: one dropped or duplicated message would
    /// otherwise leave the clients disagreeing about whether the enemy is a
    /// robot or a tank — which changes its hitbox, its armour, and which
    /// weapons the fire slot indexes into.
    /// </summary>
    public void RemoteSetVehicle(bool vehicle)
    {
        if (_vehicle != null && _vehicle.CanTransform)
            _vehicle.SetVehicle(vehicle);
    }

    /// <summary>The owner's authoritative shield broadcast.</summary>
    public void ApplyShield(float current, float max, bool down)
    {
        if (_shield == null)
            return;
        if (down)
        {
            if (!_shield.IsDown)
                _shield.NetworkForceDown();
            return;
        }
        // While our local de-rez cycle is still playing, leave it be — the
        // owner's respawn and ours run the same 3 s timer and re-sync via
        // position snapshots when both are back.
        if (!_shield.IsDown)
            _shield.NetworkSet(current, max);
    }
}
