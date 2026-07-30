using UnityEngine;

/// <summary>
/// The CPU's hands on P2: a decision tick every ~0.15–0.3 s picks a
/// directive from range and what the player is doing; between ticks the
/// directive holds, which is what gives the CPU a readable "mind" instead
/// of frame-perfect twitching. Personality is three dials — aggression
/// (pressure), caution (blocking), flair (jump-ins) — sitting ready to be
/// tied to the battle-league personalities.
///
/// Same execution-order rule as BrawlInput: intents are written before any
/// fighter reads, or one-frame button edges would be lost.
/// </summary>
[DefaultExecutionOrder(-10)]
public class BrawlBrain : MonoBehaviour
{
    public BrawlFighter Fighter { get; set; }

    [Range(0f, 1f)] public float aggression = 0.65f;
    [Range(0f, 1f)] public float caution = 0.5f;
    [Range(0f, 1f)] public float flair = 0.45f;

    float _nextThink;
    float _moveHeld;
    float _blockUntil;
    float _retreatUntil;
    bool _punchOnce, _kickOnce, _jumpOnce, _blastOnce;
    bool _flyKickQueued;
    BrawlFighter.State _lastPhase;

    void Start()
    {
        // No two CPUs fight quite alike.
        aggression = Mathf.Clamp01(aggression + Random.Range(-0.15f, 0.15f));
        caution = Mathf.Clamp01(caution + Random.Range(-0.15f, 0.15f));
        flair = Mathf.Clamp01(flair + Random.Range(-0.15f, 0.15f));
    }

    void Update()
    {
        var self = Fighter;
        var foe = self != null ? self.Opponent : null;
        if (self == null || foe == null)
            return;

        // Getting knocked down teaches a beat of respect.
        if (_lastPhase == BrawlFighter.State.Knockdown
            && self.Phase == BrawlFighter.State.Neutral)
            _retreatUntil = Time.time + 0.4f + caution * 0.5f;
        _lastPhase = self.Phase;

        if (Time.time >= _nextThink && self.Phase == BrawlFighter.State.Neutral)
        {
            _nextThink = Time.time + Random.Range(0.14f, 0.30f - 0.10f * aggression);
            Think(self, foe);
        }

        var intent = new BrawlFighter.Intent
        {
            move = _moveHeld,
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
        float toFoe = Mathf.Sign(foe.transform.position.x - self.transform.position.x);
        if (toFoe == 0f)
            toFoe = 1f;
        float gap = Mathf.Abs(foe.transform.position.x - self.transform.position.x);
        float punchRange = BrawlMoveSet.Table[BrawlMoveSet.Move.Punch].range;
        float kickRange = BrawlMoveSet.Table[BrawlMoveSet.Move.Kick].range;

        // Respect window after a knockdown: give ground, keep the guard up —
        // unless the corner is already at our back, where retreat means pin.
        if (Time.time < _retreatUntil)
        {
            bool cornered = Mathf.Abs(self.transform.position.x) > BrawlStage.LaneHalf - 1.2f
                            && Mathf.Sign(self.transform.position.x) == -toFoe;
            _moveHeld = cornered ? 0f : -toFoe * 0.8f;
            _blockUntil = Time.time + 0.25f;
            return;
        }

        // See a swing coming and sometimes just take it on the guard.
        bool foeSwinging = foe.Phase == BrawlFighter.State.Attacking
                           || foe.Phase == BrawlFighter.State.AirAttack;
        if (foeSwinging && gap < kickRange + 0.5f && Random.value < caution)
        {
            _moveHeld = 0f;
            _blockUntil = Time.time + Random.Range(0.25f, 0.50f);
            return;
        }

        // A loaded meter wants firing: from range, at a grounded target,
        // with conviction proportional to aggression.
        if (self.Charge >= 1f && !foe.IsAirborne && gap > punchRange
            && Random.value < 0.25f + aggression * 0.4f)
        {
            _moveHeld = 0f;
            _blastOnce = true;
            return;
        }

        // The player hanging in the air is an anti-air invitation.
        if (foe.IsAirborne && gap < punchRange + 0.4f && Random.value < 0.5f + caution * 0.3f)
        {
            _moveHeld = 0f;
            _punchOnce = true;
            return;
        }

        if (gap <= punchRange * 0.95f)
        {
            float roll = Random.value;
            if (roll < 0.40f + 0.22f * aggression) { _punchOnce = true; _moveHeld = toFoe * 0.2f; }
            else if (roll < 0.70f + 0.15f * aggression) { _kickOnce = true; _moveHeld = 0f; }
            else if (roll < 0.86f) _moveHeld = -toFoe * 0.9f;
            else { _blockUntil = Time.time + 0.30f; _moveHeld = 0f; }
        }
        else if (gap <= kickRange + 0.3f)
        {
            float roll = Random.value;
            if (roll < 0.40f + 0.20f * aggression) { _kickOnce = true; _moveHeld = 0f; }
            else if (roll < 0.55f + 0.25f * flair && gap > 1.4f)
            {
                _jumpOnce = true;
                _flyKickQueued = true;
                _moveHeld = toFoe;
            }
            else
                _moveHeld = toFoe * 0.8f;
        }
        else
        {
            _moveHeld = toFoe * (0.7f + 0.3f * aggression);
            if (gap < 5.5f && Random.value < flair * 0.35f)
            {
                _jumpOnce = true;
                _flyKickQueued = true;
                _moveHeld = toFoe;
            }
        }
    }
}
