using UnityEngine;

/// <summary>What a street block is made of — which decides how it dies and how it moves.</summary>
public enum TankBlockKind
{
    /// <summary>Solid stone: shrugs off fire, and a tank shoving one mostly shoves itself.</summary>
    Rock,
    /// <summary>Masonry: pushable, and a few shells turn it to dust.</summary>
    Brick,
    /// <summary>Crates: light enough to bulldoze, one good hit turns them to splinters.</summary>
    Wood,
}

/// <summary>
/// A block on the street: cover that obeys the mode's one law of motion —
/// everything moves by writing its own transform. No Rigidbody: the deck has
/// no floor collider to catch one, the bands teleport on recycle, and every
/// other mover on the field already resolves contact by hand.
///
/// Pushing happens in TankPawn.AvoidScenery: instead of the pawn taking the
/// whole depenetration, the block takes a share by material — a crate slides
/// out of a tank's way, masonry grinds, stone barely gives. What the pawn
/// hands over becomes displacement plus a little glide velocity here, so a
/// shoved block coasts to rest instead of stopping dead at the bumper.
///
/// Brick and wood are also SHOOTABLE, routed through WeaponUtil.DamageProp
/// like every other prop in the game. Stone is not — the field keeps some
/// cover no amount of fire removes, or the late game bulldozes itself flat.
/// </summary>
public class TankBlock : MonoBehaviour
{
    [SerializeField] TankBlockKind _kind;
    [SerializeField] float _hp;

    Vector3 _velocity;
    float _radius = 1f;
    bool _movedThisFrame;

    public TankBlockKind Kind => _kind;

    /// <summary>Fraction of a pawn's depenetration this block absorbs by moving.</summary>
    public float PushShare
    {
        get
        {
            switch (_kind)
            {
                case TankBlockKind.Wood: return 0.85f;
                case TankBlockKind.Brick: return 0.6f;
                default: return 0.25f;
            }
        }
    }

    public bool Destructible => _kind != TankBlockKind.Rock;

    public void Configure(TankBlockKind kind)
    {
        _kind = kind;
        _hp = kind == TankBlockKind.Brick ? 90f : kind == TankBlockKind.Wood ? 25f : float.MaxValue;
        // Horizontal footprint radius, from the box's own scale — used to keep
        // a sliding block out of berms, spires and its neighbours.
        var scale = transform.localScale;
        _radius = Mathf.Max(scale.x, scale.z) * 0.55f;
    }

    /// <summary>
    /// A pawn's shove: immediate displacement (contact resolution, this frame)
    /// plus banked glide, so releasing the throttle leaves the block coasting
    /// rather than parked mid-shove.
    /// </summary>
    public void Push(Vector3 displacement)
    {
        displacement.y = 0f;
        transform.position += displacement;
        _velocity = Vector3.ClampMagnitude(_velocity + displacement * 5f, 6f);
        _movedThisFrame = true;
    }

    /// <summary>Shot. Stone just sparks (the impact burst is the shooter's), the rest bleed.</summary>
    public void TakeHit(float damage, Vector3 point)
    {
        if (!Destructible)
            return;
        _hp -= damage;
        if (_hp > 0f)
            return;

        Color dust = _kind == TankBlockKind.Brick
            ? new Color(0.75f, 0.4f, 0.25f)
            : new Color(0.85f, 0.65f, 0.3f);
        VfxUtil.Explosion(transform.position + Vector3.up * 0.4f, dust, 1.1f);
        // Chunks live under the same band, so a mode teardown (or the band
        // recycling out from behind the hero) sweeps them with everything else.
        TankDebris.Burst(transform.position, transform.localScale,
            GetComponent<MeshRenderer>()?.sharedMaterial, transform.parent);
        Destroy(gameObject);
    }

    void Update()
    {
        // The coast-out. Exponential decay: a shove reads for half a second,
        // then the street is still again.
        if (_velocity.sqrMagnitude > 1e-4f)
        {
            transform.position += _velocity * Time.deltaTime;
            _velocity *= Mathf.Max(0f, 1f - 4.5f * Time.deltaTime);
            _movedThisFrame = true;
        }

        if (!_movedThisFrame)
            return;
        _movedThisFrame = false;
        KeepOutOfScenery();
        KeepOnTheStreet();
    }

    static readonly Collider[] Probe = new Collider[8];

    /// <summary>
    /// A moved block resolves its own contacts, the same way pawns do: pushed
    /// out of berms, spires and neighbouring blocks along the shortest way
    /// out. Pawns are skipped — their AvoidScenery is already pushing THEM
    /// clear of this block, and both resolving the same contact doubles it.
    /// </summary>
    void KeepOutOfScenery()
    {
        Vector3 centre = transform.position + Vector3.up * 0.5f;
        int count = Physics.OverlapSphereNonAlloc(centre, _radius, Probe, ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = Probe[i];
            // Self only by CHILD test — every piece of field scenery shares
            // one transform.root, so a root comparison would skip the world.
            if (collider == null || collider.transform.IsChildOf(transform))
                continue;
            if (collider.GetComponentInParent<TankPawn>() != null)
                continue;
            var otherBlock = collider.GetComponentInParent<TankBlock>();
            Vector3 away = centre - collider.ClosestPoint(centre);
            away.y = 0f;
            float distance = away.magnitude;
            if (distance < 1e-4f || distance >= _radius)
                continue;
            Vector3 resolve = away / distance * (_radius - distance);
            if (otherBlock != null)
            {
                // Block-on-block: split it, weighted the same way pawn
                // shoves are, so a crate rolls off a boulder and not the
                // other way round.
                otherBlock.Push(-resolve * otherBlock.PushShare);
                transform.position += resolve * (1f - otherBlock.PushShare);
            }
            else
            {
                transform.position += resolve;
                // A wall ends a glide dead — no rubber walls.
                _velocity = Vector3.zero;
            }
        }
    }

    /// <summary>The fence applies to furniture too: a block bulldozed past the berm line is gone from play.</summary>
    void KeepOnTheStreet()
    {
        var position = transform.position;
        float limit = TankField.HalfWidth - _radius * 0.5f;
        position.x = Mathf.Clamp(position.x, -limit, limit);
        transform.position = position;
    }
}
