using UnityEngine;

/// <summary>
/// The CPU's hands: a decision tick picks a directive from range and what
/// the opponent is doing; between ticks the directive holds, which is what
/// gives the CPU a readable "mind" instead of frame-perfect twitching.
///
/// Skill comes from a BrawlDifficulty level — the tick cadence IS the
/// reaction delay, and with it come guard odds, spacing judgment, opening
/// punishes and blast memory. A small per-spawn jitter keeps two CPUs on
/// the same level from mirroring each other.
///
/// Same execution-order rule as BrawlInput: intents are written before any
/// fighter reads, or one-frame button edges would be lost.
/// </summary>
[DefaultExecutionOrder(-10)]
public class BrawlBrain : MonoBehaviour
{
    public BrawlFighter Fighter { get; set; }

    BrawlDifficulty.Level _level;
    float _aggression;
    float _flair;

    float _nextThink;
    Vector3 _moveHeld;          // world-space heading, magnitude ≤ 1
    float _blockUntil;
    float _retreatUntil;
    float _orbitSign = 1f;      // which way this robot likes to circle
    bool _punchOnce, _kickOnce, _jumpOnce, _blastOnce;
    bool _flyKickQueued;
    BrawlFighter.State _lastPhase;

    void Awake()
    {
        // A safe default until the controller applies the chosen level —
        // CONTENDER, the same middle the AI war defaults to.
        ApplyDifficulty(3);
    }

    /// <summary>Set every dial from the chosen level, plus this robot's jitter.</summary>
    public void ApplyDifficulty(int levelIndex)
    {
        _level = BrawlDifficulty.Get(levelIndex);
        _aggression = Mathf.Clamp01(_level.aggression + Random.Range(-0.08f, 0.08f));
        // Showmanship rises a little with skill, but even rookies jump.
        _flair = Mathf.Clamp01(0.30f + levelIndex * 0.07f + Random.Range(-0.10f, 0.10f));
    }

    void Update()
    {
        var self = Fighter;
        var foe = self != null ? self.Opponent : null;
        if (self == null || foe == null)
            return;

        // Getting knocked down teaches a beat of respect — more of one at
        // the timid end of the dial.
        if (_lastPhase == BrawlFighter.State.Knockdown
            && self.Phase == BrawlFighter.State.Neutral)
            _retreatUntil = Time.time + 0.35f + (1f - _aggression) * 0.6f;
        _lastPhase = self.Phase;

        if (Time.time >= _nextThink && self.Phase == BrawlFighter.State.Neutral)
        {
            // The reaction delay, straight from the difficulty row.
            _nextThink = Time.time + Random.Range(_level.thinkMin, _level.thinkMax);
            Think(self, foe);
        }

        var intent = new BrawlFighter.Intent
        {
            move = new Vector2(_moveHeld.x, _moveHeld.z),
            block = Time.time < _blockUntil,
            punch = _punchOnce,
            kick = _kickOnce,
            jump = _jumpOnce,
            blast = _blastOnce,
        };
        _punchOnce = _kickOnce = _jumpOnce = _blastOnce = false;

        // The queued fly kick fires the frame the arc begins.
        if (_flyKickQueued && self.IsAirborne)
        {
            intent.kick = true;
            _flyKickQueued = false;
        }

        self.Driven = intent;
    }

    void Think(BrawlFighter self, BrawlFighter foe)
    {
        Vector3 offset = foe.transform.position - self.transform.position;
        offset.y = 0f;
        float gap = offset.magnitude;
        Vector3 toFoe = gap > 1e-4f ? offset / gap : Vector3.right;
        Vector3 orbit = Vector3.Cross(Vector3.up, toFoe) * _orbitSign;
        // Circling is what makes an arena fight look like one — and every
        // so often the robot changes its mind about which way around.
        if (Random.value < 0.12f)
            _orbitSign = -_orbitSign;

        // Spacing judgment scales the bands: a rookie swings from too far
        // and eats the whiff; a master steps in until the arm arrives.
        float punchBand = BrawlMoveSet.Table[BrawlMoveSet.Move.Punch].range
                          * 0.85f * _level.spacingError;
        float kickBand = BrawlMoveSet.Table[BrawlMoveSet.Move.Kick].range
                         * 0.95f * _level.spacingError;

        // Respect window after a knockdown: give ground on an angle, keep
        // the guard up — unless the ring's edge is already at our back.
        if (Time.time < _retreatUntil)
        {
            Vector3 back = self.transform.position - toFoe * 1.2f;
            var half = BrawlStage.BoundsHalf;
            bool cornered = Mathf.Abs(back.x) > half.x - 0.5f || Mathf.Abs(back.z) > half.y - 0.5f;
            _moveHeld = cornered ? orbit * 0.7f : (-toFoe * 0.7f + orbit * 0.4f);
            _blockUntil = Time.time + 0.25f;
            return;
        }

        // A repair kit on the floor while we're hurting rewrites the errand
        // list: go GET it. The opponent may be sprinting for the same box,
        // and that race is arena drama all by itself.
        var kit = BrawlRepairKit.Active;
        if (kit != null && self.Health < 60f)
        {
            Vector3 toKit = kit.transform.position - self.transform.position;
            toKit.y = 0f;
            float kitGap = toKit.magnitude;
            if (kitGap > 1e-3f && kitGap < 8f)
            {
                Vector3 heading = toKit / kitGap;
                _moveHeld = heading;
                if (WallAhead(self, heading, out bool hop))
                {
                    if (hop && !self.IsAirborne)
                        _jumpOnce = true;
                    else
                        _moveHeld = heading * 0.3f
                                    + Vector3.Cross(Vector3.up, heading) * (_orbitSign * 0.9f);
                }
                return;
            }
        }

        // Different floors: swinging across a metre of elevation is how a
        // CPU flails forever at someone it can't reach. Close the height
        // gap first — jump up to them, or walk off the ledge onto them.
        float heightGap = foe.transform.position.y - self.transform.position.y;
        if (Mathf.Abs(heightGap) > 0.9f && !self.IsAirborne && !foe.IsAirborne)
        {
            _moveHeld = toFoe * 0.9f;
            if (heightGap > 0f && gap < 1.8f && Random.value < 0.6f)
                _jumpOnce = true;
            return;
        }

        // An open opponent — reeling from a hit, or recovering from a swing
        // that missed — is the punisher's moment.
        bool foeOpen = foe.Phase == BrawlFighter.State.HitStun
                       || ((foe.Phase == BrawlFighter.State.Attacking
                            || foe.Phase == BrawlFighter.State.AirAttack)
                           && !foe.AttackWindowOpen);
        if (foeOpen && gap <= punchBand * 1.1f && Random.value < _level.punishChance)
        {
            _moveHeld = toFoe * 0.4f;
            if (Random.value < 0.5f) _punchOnce = true; else _kickOnce = true;
            return;
        }

        // See a swing coming and sometimes take it on the guard.
        bool foeSwinging = foe.Phase == BrawlFighter.State.Attacking
                           || foe.Phase == BrawlFighter.State.AirAttack;
        if (foeSwinging && gap < kickBand + 0.5f && Random.value < _level.guardChance)
        {
            _moveHeld = Vector3.zero;
            _blockUntil = Time.time + Random.Range(0.25f, 0.50f);
            return;
        }

        // A loaded meter wants firing: from range, at a grounded target.
        if (self.Charge >= 1f && !foe.IsAirborne && gap > punchBand
            && Random.value < _level.blastChance * 0.7f)
        {
            _moveHeld = Vector3.zero;
            _blastOnce = true;
            return;
        }

        // The player hanging in the air is an anti-air invitation.
        if (foe.IsAirborne && gap < punchBand + 0.4f
            && Random.value < 0.30f + _level.guardChance * 0.5f)
        {
            _moveHeld = Vector3.zero;
            _punchOnce = true;
            return;
        }

        if (gap <= punchBand)
        {
            float roll = Random.value;
            if (roll < 0.40f + 0.22f * _aggression) { _punchOnce = true; _moveHeld = toFoe * 0.2f; }
            else if (roll < 0.70f + 0.15f * _aggression) { _kickOnce = true; _moveHeld = Vector3.zero; }
            else if (roll < 0.86f) _moveHeld = -toFoe * 0.6f + orbit * 0.5f;
            else { _blockUntil = Time.time + 0.30f; _moveHeld = Vector3.zero; }
        }
        else if (gap <= kickBand + 0.3f)
        {
            float roll = Random.value;
            if (roll < 0.40f + 0.20f * _aggression) { _kickOnce = true; _moveHeld = Vector3.zero; }
            else if (roll < 0.55f + 0.25f * _flair && gap > 1.4f)
            {
                _jumpOnce = true;
                _flyKickQueued = true;
                _moveHeld = toFoe;
            }
            else
                // The footsie dance: pressure on a curve, not a rail.
                _moveHeld = toFoe * 0.6f + orbit * 0.55f;
        }
        else
        {
            _moveHeld = toFoe * (0.7f + 0.3f * _aggression) + orbit * 0.3f;
            if (WallAhead(self, toFoe, out bool jumpable))
            {
                // Scenery in the path: hop what's hoppable, or FLOW AROUND
                // it — a plane has an 'around', the lane never did.
                if (jumpable && !self.IsAirborne)
                    _jumpOnce = true;
                else
                    _moveHeld = toFoe * 0.25f + orbit * 0.9f;
            }
            else if (gap < 5.5f && Random.value < _flair * 0.35f)
            {
                _jumpOnce = true;
                _flyKickQueued = true;
                _moveHeld = toFoe;
            }
        }
        if (_moveHeld.sqrMagnitude > 1f)
            _moveHeld.Normalize();
    }

    /// <summary>
    /// Is a wall in the next stride toward the foe? Horizontal rays, like
    /// the fighter's own mover: one above step height (a wall exists),
    /// one above jump reach (too tall to hop).
    /// </summary>
    static bool WallAhead(BrawlFighter self, Vector3 toFoe, out bool jumpable)
    {
        Vector3 feet = self.transform.position;
        bool wall = Physics.Raycast(feet + Vector3.up * 0.8f, toFoe, 1.1f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        jumpable = wall && !Physics.Raycast(feet + Vector3.up * 1.55f, toFoe, 1.1f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        return wall;
    }
}
