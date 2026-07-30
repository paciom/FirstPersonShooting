using UnityEngine;

/// <summary>
/// One combatant on the Brawl lane: a deterministic state machine over the
/// frame data in BrawlMoveSet. No physics bodies — lane position, a jump arc
/// and range checks are the whole simulation, which keeps every hit
/// explainable (and immune to the skinned-bounds lies the FPS modes learned
/// to distrust).
///
/// The fighter is driven through <see cref="Driven"/>, written each frame by
/// BrawlInput (the player) or BrawlBrain (the CPU). It never reads keys
/// itself, so player and AI are the same class with different chauffeurs.
/// </summary>
public class BrawlFighter : MonoBehaviour
{
    public enum State
    {
        Neutral,     // grounded, free to act (walking is Neutral with velocity)
        Air,         // jump arc, may fly-kick
        Attacking,   // grounded move in progress
        AirAttack,   // fly kick, hot until landing
        Blocking,    // held guard
        HitStun,     // just took one
        Knockdown,   // on the floor, invulnerable, getting up
        KO,          // round over, stays down
        Celebrating, // round won
    }

    /// <summary>What the driver wants this frame. Buttons are edges except block.</summary>
    public struct Intent
    {
        public float move;   // -1..1 along the lane
        public bool jump;
        public bool punch;
        public bool kick;
        public bool block;   // held
        public bool blast;   // the special — needs a full charge meter
    }

    public Intent Driven;

    /// <summary>
    /// The referee's whistle: while locked (round intro, round end) the
    /// driver's intent reads as nothing, but physics — knockback in flight,
    /// a jump mid-arc — still resolves.
    /// </summary>
    public bool ControlsLocked { get; set; }

    Intent Live => ControlsLocked ? default : Driven;

    public int TeamId { get; private set; }
    public BrawlFighter Opponent { get; set; }
    public State Phase { get; private set; } = State.Neutral;
    public float Health { get; private set; } = BrawlMoveSet.MaxHealth;

    /// <summary>
    /// The PHOTON BLAST meter, 0..1. Landing hits charges it; taking hits
    /// charges it a little too (the comeback trickle). Persists between
    /// rounds, Street Fighter style.
    /// </summary>
    public float Charge { get; private set; }

    /// <summary>+1 facing right (toward +X), -1 facing left.</summary>
    public float Facing { get; private set; } = 1f;

    public bool IsAirborne => transform.position.y > 0.02f;

    /// <summary>Raised once when health reaches zero. BrawlMatch owns what happens next.</summary>
    public System.Action<BrawlFighter> OnKnockedOut;

    /// <summary>Raised for every landed (non-blocked) hit: victim, damage, wasKnockdown.</summary>
    public System.Action<BrawlFighter, int, bool> OnHitLanded;

    /// <summary>Raised on the ATTACKER when the defender's guard ate the hit.</summary>
    public System.Action<BrawlFighter, BrawlMoveSet.Move> OnHitBlocked;

    /// <summary>Raised when a strike's window closed without touching anyone.</summary>
    public System.Action<BrawlMoveSet.Move> OnWhiffed;

    /// <summary>Raised when a strike found a limb, not the body: "arm"/"leg".</summary>
    public System.Action<string> OnGrazed;

    /// <summary>True when this fighter carries a per-bone hurtbox rig.</summary>
    public bool HasHurtboxes { get; private set; }

    Transform _body;
    Animator _animator;
    Transform _handR;
    Transform _handL;
    Transform _forearmR;
    Transform _footR;
    Transform _footL;
    float _spawnX;
    Color _tint;

    // ---- motion ----
    float _verticalVelocity;
    float _airVelocityX;
    float _knockbackVelocity;
    float _animatorSpeed;

    // ---- move in progress ----
    BrawlMoveSet.Data _move;
    BrawlMoveSet.Variant _variant;
    float _moveTime;
    bool _moveHasHit;
    bool _boltFired;
    bool _grazedThisMove;

    /// <summary>The face of the current move — display name, limb, trigger.</summary>
    public BrawlMoveSet.Variant CurrentVariant => _variant;

    // ---- timers ----
    float _stunTime;
    float _floorTime;
    bool _getUpFired;

    public static BrawlFighter Spawn(Transform stageRoot, RobotRoster roster,
        int robotIndex, int teamId)
    {
        var go = new GameObject(teamId == 0 ? "BrawlFighter_P1" : "BrawlFighter_P2");
        go.transform.SetParent(stageRoot, false);
        // Cyan opens on the left, the classic P1 side.
        float side = teamId == 0 ? -1f : 1f;
        go.transform.localPosition = new Vector3(side * BrawlStage.StartOffset, 0f, 0f);

        var fighter = go.AddComponent<BrawlFighter>();
        fighter.TeamId = teamId;
        fighter._spawnX = side * BrawlStage.StartOffset;

        // Root -> Body -> Model, the shape every character in the project
        // has; effects and factories all expect a "Body" to hang off.
        var body = new GameObject("Body").transform;
        body.SetParent(go.transform, false);
        body.localPosition = new Vector3(0f, 1.0f, 0f);
        fighter._body = body;

        Color tint = MatchAnnouncer.TeamColor(teamId);
        fighter._tint = tint;
        var entry = (roster != null && roster.HasRobots)
            ? roster.Get(robotIndex)
            : default(RobotRoster.Entry);
        if (entry.modelPrefab != null)
        {
            // The forge may have built a dedicated fighter prefab (the
            // re-rigged Meshy skeleton with its own walk); the FPS walker is
            // the fallback. Either way the Brawl controller replaces the
            // locomotion-only one — Resources, so nothing serializes into
            // the scene.
            string robot = RobotName(entry);
            var fighterPrefab = Resources.Load<GameObject>($"Brawl/{robot}-fighter");
            var model = RobotFactory.InstantiateNormalized(
                fighterPrefab != null ? fighterPrefab : entry.modelPrefab, body, tint);
            fighter._animator = model.GetComponentInChildren<Animator>(true);

            // Contact is judged where the striking limb ACTUALLY is, so a
            // hit can only land when the strike visually reaches — cache
            // the effector bones (all Meshy rigs share the names). Left
            // side too: the hook is a left-hand punch.
            fighter._handR = FindDeep(model.transform, "RightHand");
            fighter._handL = FindDeep(model.transform, "LeftHand");
            fighter._forearmR = FindDeep(model.transform, "RightForeArm");
            fighter._footR = FindDeep(model.transform, "RightFoot");
            fighter._footL = FindDeep(model.transform, "LeftFoot");

            // Hurtboxes ride the bones, so a crumpled or kicking body is
            // hittable exactly where it visibly is.
            fighter.HasHurtboxes = BrawlHurtboxes.Build(fighter, model);

            var controller = Resources.Load<RuntimeAnimatorController>($"Brawl/{robot}");
            if (controller != null && fighter._animator != null)
                fighter._animator.runtimeAnimatorController = controller;

            // RobotLocomotion sniffs speed from position deltas of whatever it
            // decides the character root is — under the stage hierarchy that
            // is the wrong transform entirely. The fighter knows its own
            // velocity exactly and drives the Speed float itself.
            var locomotion = model.GetComponentInChildren<RobotLocomotion>(true);
            if (locomotion != null)
                locomotion.enabled = false;
        }
        else
        {
            // No roster (scene built before any robots were downloaded):
            // a tinted capsule keeps the mode testable.
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Model";
            capsule.transform.SetParent(body, false);
            Object.Destroy(capsule.GetComponent<Collider>());
            capsule.GetComponent<MeshRenderer>().sharedMaterial =
                ArenaMaterials.Lit($"brawl-fallback-{teamId}", tint, 0.5f);
        }

        return fighter;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            var hit = FindDeep(child, name);
            if (hit != null)
                return hit;
        }
        return null;
    }

    /// <summary>
    /// "ranger" out of a roster entry — prefab names are "&lt;robot&gt;-robot"
    /// by the walker forge's convention, and the Brawl assets key off the
    /// same word.
    /// </summary>
    static string RobotName(RobotRoster.Entry entry)
    {
        string name = entry.modelPrefab.name;
        return name.EndsWith("-robot") ? name.Substring(0, name.Length - "-robot".Length) : name;
    }

    /// <summary>Back to the corner, full health, feet down — the next round's shape.</summary>
    public void ResetForRound()
    {
        Health = BrawlMoveSet.MaxHealth;
        Phase = State.Neutral;
        Driven = default;
        _verticalVelocity = 0f;
        _airVelocityX = 0f;
        _knockbackVelocity = 0f;
        _moveTime = 0f;
        _stunTime = 0f;
        _floorTime = 0f;
        _getUpFired = false;
        transform.localPosition = new Vector3(_spawnX, 0f, 0f);
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.Update(0f);
        }
    }

    /// <summary>Round lost: fall and stay down. Fires no further events.</summary>
    public void KnockOut()
    {
        Phase = State.KO;
        Trigger(BrawlAnim.KO);
    }

    /// <summary>Round won: strike the pose until the next round resets us.</summary>
    public void Celebrate()
    {
        if (Phase == State.KO)
            return;
        Phase = State.Celebrating;
        Trigger(BrawlAnim.Victory);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        switch (Phase)
        {
            case State.Neutral: TickNeutral(dt); break;
            case State.Air: TickAir(dt, hot: false); break;
            case State.Attacking: TickAttack(dt); break;
            case State.AirAttack: TickAir(dt, hot: true); break;
            case State.Blocking: TickBlocking(); break;
            case State.HitStun: TickHitStun(dt); break;
            case State.Knockdown: TickKnockdown(dt); break;
            case State.KO:
            case State.Celebrating:
                break;
        }

        Separate();
        ClampToLane();
        DriveAnimator(dt);
    }

    // ------------------------------------------------------------- states

    void TickNeutral(float dt)
    {
        FaceOpponent();
        var intent = Live;

        if (intent.block)
        {
            Phase = State.Blocking;
            SetBlock(true);
            return;
        }
        if (intent.blast && Charge >= 1f)
        {
            Charge = 0f;
            StartMove(BrawlMoveSet.Move.Blast);
            return;
        }
        if (intent.punch) { StartMove(BrawlMoveSet.Move.Punch); return; }
        if (intent.kick) { StartMove(BrawlMoveSet.Move.Kick); return; }
        if (intent.jump)
        {
            Phase = State.Air;
            _verticalVelocity = BrawlMoveSet.JumpVelocity;
            _airVelocityX = intent.move * BrawlMoveSet.WalkSpeed;
            BrawlAudio.Play(BrawlAudio.Id.Jump, transform.position, 0.4f);
            return;
        }

        Walk(intent.move, dt);
    }

    void TickAir(float dt, bool hot)
    {
        // A fly kick can start any time on the way up or down.
        if (!hot && Live.kick)
        {
            Phase = State.AirAttack;
            _move = BrawlMoveSet.Table[BrawlMoveSet.Move.FlyKick];
            _variant = BrawlMoveSet.FlyKickVariant;
            _moveTime = 0f;
            _moveHasHit = false;
            _grazedThisMove = false;
            // The lunge: committing adds forward speed toward the opponent.
            _airVelocityX += Facing * 2.2f;
            Trigger(BrawlAnim.FlyKick);
            hot = true;
        }

        if (hot)
        {
            _moveTime += dt;
            bool active = _moveTime >= _move.startup
                          && _moveTime <= _move.startup + _move.active;
            if (active && !_moveHasHit)
                TryHit();
        }

        _verticalVelocity -= BrawlMoveSet.Gravity * dt;
        Move(_airVelocityX * dt, _verticalVelocity * dt);

        if (transform.position.y <= 0f && _verticalVelocity <= 0f)
        {
            SetY(0f);
            _verticalVelocity = 0f;
            if (hot)
            {
                // Landing recovery keeps the committed feel of the move.
                Phase = State.Attacking;
                _moveTime = _move.startup + _move.active;
            }
            else
            {
                Phase = State.Neutral;
            }
        }
    }

    void TickAttack(float dt)
    {
        _moveTime += dt;

        // The blast is a projectile, not a limb: it leaves the hands the
        // moment startup ends and the melee window never applies.
        if (_move.move == BrawlMoveSet.Move.Blast)
        {
            if (!_boltFired && _moveTime >= _move.startup)
            {
                _boltFired = true;
                BrawlAudio.Play(BrawlAudio.Id.BlastFire, transform.position + Vector3.up * 1.15f);
                BrawlBolt.Fire(this, Opponent, _tint);
            }
        }
        else
        {
            bool active = _moveTime >= _move.startup
                          && _moveTime <= _move.startup + _move.active;
            if (active && !_moveHasHit)
                TryHit();
        }

        if (_moveTime >= _move.Duration)
        {
            if (!_moveHasHit && _move.move != BrawlMoveSet.Move.Blast)
                OnWhiffed?.Invoke(_move.move);
            Phase = State.Neutral;
        }
    }

    void TickBlocking()
    {
        FaceOpponent();
        if (!Live.block)
        {
            SetBlock(false);
            Phase = State.Neutral;
        }
    }

    void TickHitStun(float dt)
    {
        _stunTime -= dt;
        Move(_knockbackVelocity * dt, 0f);
        _knockbackVelocity = Mathf.MoveTowards(_knockbackVelocity, 0f, 12f * dt);
        if (_stunTime <= 0f)
            Phase = State.Neutral;
    }

    void TickKnockdown(float dt)
    {
        _floorTime -= dt;
        // The slide: a knocked-down robot travels, it doesn't drop in place.
        Move(_knockbackVelocity * dt, 0f);
        _knockbackVelocity = Mathf.MoveTowards(_knockbackVelocity, 0f, 5f * dt);
        // The rise is its own clip, cued so it completes as control returns.
        if (!_getUpFired && _floorTime <= BrawlMoveSet.GetUpTime)
        {
            _getUpFired = true;
            Trigger(BrawlAnim.GetUp);
        }
        if (_floorTime <= 0f)
            Phase = State.Neutral;
    }

    // ------------------------------------------------------------- attacks

    /// <summary>
    /// One button, many moves: the family's frame data always applies, but
    /// WHICH punch or kick plays is rolled fresh every press.
    /// </summary>
    void StartMove(BrawlMoveSet.Move which)
    {
        Phase = State.Attacking;
        _move = BrawlMoveSet.Table[which];
        var variants = BrawlMoveSet.VariantsOf(which);
        _variant = variants != null
            ? variants[Random.Range(0, variants.Length)]
            : which == BrawlMoveSet.Move.Blast ? BrawlMoveSet.BlastVariant
            : BrawlMoveSet.FlyKickVariant;
        _moveTime = 0f;
        _moveHasHit = false;
        _boltFired = false;
        _grazedThisMove = false;
        BrawlAudio.Play(BrawlAudio.Id.Whoosh, transform.position + Vector3.up * 1.2f, 0.35f);
        Trigger(_variant.trigger);
    }

    /// <summary>
    /// The striking limb this move hits with, live — null outside a strike
    /// (or when a rig is missing the bone). The F3 debug overlay draws it.
    /// </summary>
    public Transform ActiveEffector
    {
        get
        {
            if (Phase != State.Attacking && Phase != State.AirAttack)
                return null;
            if (_move.move == BrawlMoveSet.Move.Blast)
                return null;   // the bolt is the effector
            switch (_variant.limb)
            {
                case BrawlMoveSet.Limb.LeftHand: return _handL;
                case BrawlMoveSet.Limb.RightForeArm: return _forearmR;
                case BrawlMoveSet.Limb.LeftFoot: return _footL;
                case BrawlMoveSet.Limb.RightFoot: return _footR;
                default: return _handR;
            }
        }
    }

    /// <summary>True while the current move's hit window is open.</summary>
    public bool AttackWindowOpen =>
        (Phase == State.Attacking || Phase == State.AirAttack)
        && _moveTime >= _move.startup
        && _moveTime <= _move.startup + _move.active;

    /// <summary>
    /// A hit is contact against the body the animation is actually showing:
    /// the striking bone (plus the fist's own radius) is tested against the
    /// defender's per-bone hurtboxes. Torso and head are damage; a limb is
    /// a graze — sparks, no health — and the strike stays live in case the
    /// fist finds the body deeper in the window.
    /// </summary>
    void TryHit()
    {
        var target = Opponent;
        if (target == null)
            return;
        float gap = Mathf.Abs(target.transform.position.x - transform.position.x);
        if (gap > _move.range + 1f)
            return;

        var effector = ActiveEffector;
        if (effector == null || !target.HasHurtboxes)
        {
            // Capsule-fallback robots: the old tuned column check.
            float dy = Mathf.Abs(target.transform.position.y - transform.position.y);
            float band = _move.move == BrawlMoveSet.Move.FlyKick ? 1.8f : 1.2f;
            if (gap <= _move.range && dy <= band)
            {
                _moveHasHit = true;
                target.TakeHit(_move, this);
            }
            return;
        }

        var part = BrawlHurtboxes.Query(effector.position,
            BrawlMoveSet.StrikeRadius + 0.04f, target);
        if (part == null)
            return;
        if (part.Vital)
        {
            _moveHasHit = true;
            target.TakeHit(_move, this);
        }
        else if (!_grazedThisMove)
        {
            _grazedThisMove = true;
            VfxUtil.SpawnBurst(effector.position, new Color(0.8f, 0.95f, 1f), 5, 2.5f, 0.08f);
            BrawlAudio.Play(BrawlAudio.Id.Graze, effector.position, 0.6f);
            OnGrazed?.Invoke(part.Label);
        }
    }

    public void TakeHit(BrawlMoveSet.Data hit, BrawlFighter attacker)
    {
        if (Phase == State.Knockdown || Phase == State.KO || Phase == State.Celebrating)
            return;

        float away = Mathf.Sign(transform.position.x - attacker.transform.position.x);
        if (away == 0f)
            away = -attacker.Facing;

        Vector3 chest = transform.position + new Vector3(-away * 0.35f, 1.2f, 0f);

        // A standing guard eats the hit: no damage (kid rules — no chip),
        // a shove instead of a stagger.
        if (Phase == State.Blocking && !IsAirborne)
        {
            _knockbackVelocity = away * BrawlMoveSet.HitKnockback * 1.2f;
            _stunTime = BrawlMoveSet.HitStun * 0.6f;
            Phase = State.HitStun;   // brief guard-shove; block anim persists via bool
            VfxUtil.SpawnBurst(chest, _tint, 6, 3f, 0.10f);
            BrawlAudio.Play(BrawlAudio.Id.Block, chest, 0.8f);
            attacker.OnHitBlocked?.Invoke(this, hit.move);
            return;
        }

        Health = Mathf.Max(0f, Health - hit.damage);
        SetBlock(false);
        VfxUtil.ImpactBurst(chest, new Color(1f, 0.9f, 0.6f));

        // Dealing charges the meter fast, absorbing trickles it up — the
        // robot getting beaten is quietly loading a comeback.
        attacker.GainCharge(hit.damage / 55f);
        GainCharge(hit.damage / 130f);

        bool knockdown = hit.move == BrawlMoveSet.Move.FlyKick
                         || hit.move == BrawlMoveSet.Move.Blast
                         || Health <= 0f;
        // Metal on metal: the heavy clang for anything that floors a robot.
        BrawlAudio.Play(knockdown ? BrawlAudio.Id.HitHeavy : BrawlAudio.Id.Hit, chest);
        attacker.OnHitLanded?.Invoke(this, hit.damage, knockdown);

        if (Health <= 0f)
        {
            KnockOut();
            VfxUtil.Explosion(transform.position + Vector3.up, _tint, 0.7f);
            BrawlAudio.Play(BrawlAudio.Id.KO, transform.position + Vector3.up);
            OnKnockedOut?.Invoke(this);
            return;
        }

        _verticalVelocity = 0f;
        SetY(0f);
        if (knockdown)
        {
            Phase = State.Knockdown;
            _floorTime = BrawlMoveSet.KnockdownTime + BrawlMoveSet.GetUpTime;
            _getUpFired = false;
            // The clips play in place, so this slide IS the being-blown-
            // backward — ~2.3 m before it decays, and the rise happens
            // wherever it ends.
            _knockbackVelocity = away * BrawlMoveSet.HitKnockback * 6f;
            Trigger(BrawlAnim.Knockdown);
        }
        else
        {
            Phase = State.HitStun;
            _stunTime = BrawlMoveSet.HitStun;
            _knockbackVelocity = away * (BrawlMoveSet.HitKnockback / BrawlMoveSet.HitStun);
            Trigger(BrawlAnim.Hit);
        }
    }

    void GainCharge(float amount)
    {
        bool wasReady = Charge >= 1f;
        Charge = Mathf.Clamp01(Charge + amount);
        if (!wasReady && Charge >= 1f)
            BrawlAudio.PlayFlat(BrawlAudio.Id.ChargeReady, 0.7f);
    }

    // -------------------------------------------------------------- motion

    void Walk(float direction, float dt)
    {
        Move(direction * BrawlMoveSet.WalkSpeed * dt, 0f);
    }

    void Move(float dx, float dy)
    {
        var p = transform.position;
        transform.position = new Vector3(p.x + dx, Mathf.Max(0f, p.y + dy), p.z);
    }

    void SetY(float y)
    {
        var p = transform.position;
        transform.position = new Vector3(p.x, y, p.z);
    }

    /// <summary>
    /// Grounded fighters cannot overlap: both sides run this and shed half
    /// the intrusion each, so the pair converges without a referee.
    /// </summary>
    void Separate()
    {
        if (Opponent == null || IsAirborne || Opponent.IsAirborne)
            return;
        float gap = transform.position.x - Opponent.transform.position.x;
        float overlap = BrawlMoveSet.MinSeparation - Mathf.Abs(gap);
        if (overlap <= 0f)
            return;
        float push = (gap >= 0f ? 1f : -1f) * overlap * 0.5f;
        Move(push, 0f);
    }

    void ClampToLane()
    {
        var p = transform.position;
        float x = Mathf.Clamp(p.x, -BrawlStage.LaneHalf, BrawlStage.LaneHalf);
        if (x != p.x)
            transform.position = new Vector3(x, p.y, p.z);
    }

    void FaceOpponent()
    {
        if (Opponent == null)
            return;
        Facing = Opponent.transform.position.x >= transform.position.x ? 1f : -1f;
        // Robots model +Z as forward and the lane runs along X: ±90° yaw.
        transform.rotation = Quaternion.Euler(0f, 90f * Facing, 0f);
    }

    // ------------------------------------------------------------ animator

    void DriveAnimator(float dt)
    {
        if (_animator == null)
            return;
        float target = Phase == State.Neutral ? Mathf.Abs(Live.move) * BrawlMoveSet.WalkSpeed : 0f;
        _animatorSpeed = Mathf.Lerp(_animatorSpeed, target, 1f - Mathf.Exp(-12f * dt));
        _animator.SetFloat(BrawlAnim.SpeedHash, _animatorSpeed);
    }

    void Trigger(string name)
    {
        if (_animator != null)
            _animator.SetTrigger(name);
    }

    void SetBlock(bool held)
    {
        if (_animator != null)
            _animator.SetBool(BrawlAnim.BlockHash, held);
    }
}
