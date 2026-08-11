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
        public Vector2 move; // world-space XZ direction, magnitude ≤ 1
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

    /// <summary>Horizontal unit vector toward the opponent — the fight axis.</summary>
    public Vector3 FacingDir { get; private set; } = Vector3.right;

    float _tempo = 1f;

    /// <summary>
    /// How fast this fighter's own clock runs. 1 is the bout's speed; 2 walks,
    /// strikes, staggers and falls at double.
    ///
    /// It is ONE knob rather than a set of them because the whole point of
    /// this class is that animation and frame data stay locked together — the
    /// forge bakes clips to exactly Duration. Scaling only the animation
    /// desyncs the hit window from the fist; scaling only the frame data
    /// leaves the fist behind. So Tempo scales the delta time every state
    /// integrates against AND the Animator's playback rate, which keeps the
    /// pair honest for free and costs one multiply.
    ///
    /// Gravity comes along correctly: the arc is the same shape traversed
    /// faster, not a heavier robot, because velocity integrates in the same
    /// scaled clock.
    ///
    /// Chinese Quest runs its cast at 2 (and briefly higher for the charge
    /// across the stage) because a quiz answered forty times in a run cannot
    /// afford a bout's deliberate pacing. Brawl leaves it at 1.
    /// </summary>
    public float Tempo
    {
        get => _tempo;
        set
        {
            _tempo = Mathf.Clamp(value, 0.05f, 8f);
            if (_animator != null)
                _animator.speed = _tempo;
        }
    }

    /// <summary>
    /// Extra clock on ATTACKS only. While a strike (grounded or aerial) is
    /// in progress, the fighter's clock — and the Animator's rate with it,
    /// the same locked pair Tempo keeps — is multiplied by this, so actions
    /// snap while walking, stun and falls keep the bout's pacing. Brawl
    /// bouts run BrawlMoveSet.ActionTempo; everyone else's default of 1
    /// leaves the tabled timings literal (Chinese Quest scripts its waits
    /// against Tempo and must not have attacks quietly doubled under it).
    /// </summary>
    public float ActionTempo { get; set; } = 1f;

    /// <summary>True while a strike's clock (and animator) run at ActionTempo.</summary>
    bool Striking => Phase == State.Attacking || Phase == State.AirAttack;

    /// <summary>The tallest step a walking robot climbs without jumping.</summary>
    const float StepUp = 0.6f;

    /// <summary>
    /// The standable surface under this fighter right now — probed from
    /// the fighter's own height, so a robot atop a tall tier reads the
    /// tier, not the ground floor beneath it.
    /// </summary>
    public float Ground => BrawlGround.HeightAt(transform.position.x,
        transform.position.z, aboveY: transform.position.y);

    public bool IsAirborne => transform.position.y > Ground + 0.02f;

    /// <summary>Raised when knockback drives the fighter into a lane end (speed passed).</summary>
    public System.Action<BrawlFighter, float> OnWallSlam;

    /// <summary>Raised as any attack begins, with its display name — the HUD captions ride this.</summary>
    public System.Action<string> OnMoveStarted;

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

    /// <summary>
    /// Raised by the ATTACKER for every contact its strike makes — vital or
    /// graze — with the exact body part touched. The F3 overlay flashes it.
    /// </summary>
    public System.Action<BrawlBodyPart> OnPartStruck;

    /// <summary>True when this fighter carries a per-bone hurtbox rig.</summary>
    public bool HasHurtboxes { get; private set; }

    Transform _body;
    Animator _animator;
    Transform _handR;
    Transform _handL;
    Transform _forearmR;
    Transform _footR;
    Transform _footL;
    Transform _kneeR;
    float _spawnX;
    Color _tint;

    // ---- motion ----
    float _verticalVelocity;
    Vector3 _airVelocity;       // horizontal (XZ) flight
    Vector3 _knockback;         // horizontal shove, decaying
    Vector3 _nudge;             // the graze shove: ground given, no stagger
    float _animatorSpeed;

    // ---- move in progress ----
    BrawlMoveSet.Data _move;
    BrawlMoveSet.Variant _variant;
    float _moveTime;
    bool _moveHasHit;
    // Last frame's striking-bone position — the start of the swept test.
    Vector3 _effectorPrev;
    bool _effectorPrevValid;
    bool _boltFired;
    bool _grazedThisMove;
    bool _propHitThisMove;

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
        // Cyan opens on the left, the classic P1 side, on the spawn line.
        float side = teamId == 0 ? -1f : 1f;
        go.transform.localPosition =
            new Vector3(side * BrawlStage.StartOffset, 0f, BrawlStage.SpawnZ);

        var fighter = go.AddComponent<BrawlFighter>();
        fighter.TeamId = teamId;
        fighter._spawnX = side * BrawlStage.StartOffset;

        // The solid the physics crates bounce off. Ignore Raycast layer so
        // the terrain probe and camera never read a robot as scenery; the
        // capsule has no rigidbody, so props carom while the robot stays
        // script-driven.
        go.layer = 2;
        var bumper = go.AddComponent<CapsuleCollider>();
        bumper.center = new Vector3(0f, BrawlMoveSet.BodyHeight * 0.5f, 0f);
        bumper.radius = BrawlMoveSet.BodyHalfWidth;
        bumper.height = BrawlMoveSet.BodyHeight;
        go.AddComponent<BrawlBodyBumper>().Owner = fighter;

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
            // The shin bone's origin IS the knee joint.
            fighter._kneeR = FindDeep(model.transform, "RightLeg");

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
        _airVelocity = Vector3.zero;
        _knockback = Vector3.zero;
        _nudge = Vector3.zero;
        _moveTime = 0f;
        _stunTime = 0f;
        _floorTime = 0f;
        _getUpFired = false;
        transform.localPosition = new Vector3(_spawnX, 0f, BrawlStage.SpawnZ);
        // Spawn ON whatever stands here — raised tiles, tall tiers — never
        // inside it: the probe runs from high above.
        SetY(BrawlGround.HeightAt(transform.position.x, transform.position.z, aboveY: 30f));
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.Update(0f);
        }
    }

    /// <summary>
    /// Full health, empty meter, clean animator, standing exactly here.
    ///
    /// ResetForRound is the two-corner version: it sends every fighter back
    /// to ITS OWN spawn either side of the brawl line, which is the only
    /// arrangement a bout has. Chinese Quest stands five robots at five
    /// scripted posts and resets them between questions, so the spot has to
    /// be an argument.
    /// </summary>
    public void ResetAt(float x, float z)
    {
        Health = BrawlMoveSet.MaxHealth;
        Charge = 0f;
        _moveTime = 0f;
        _stunTime = 0f;
        _floorTime = 0f;
        _getUpFired = false;
        // Reposition already clears velocity, guard and Phase — including
        // the KO the last question may have left this robot lying in.
        Reposition(x, z);
        if (_animator != null)
        {
            _animator.Rebind();
            // Re-asserted after the rebind, not assumed to survive it.
            _animator.speed = _tempo;
            _animator.Update(0f);
        }
    }

    /// <summary>
    /// Hand the blast meter over from outside the fight's economy. A bout
    /// earns the special by landing and eating hits; a quiz hands it out as
    /// the reward for reading a character right.
    /// </summary>
    public void GrantCharge(float amount) => GainCharge(amount);

    /// <summary>
    /// The referee's break: back to the corner, everything ELSE kept —
    /// health, charge, the animator's stride. For untangling soft-locks
    /// (a lane wall neither side can cross), not for round resets.
    /// </summary>
    public void Reposition()
    {
        Reposition(_spawnX, BrawlStage.SpawnZ);
    }

    /// <summary>The referee points at a spot; the fighter stands there.</summary>
    public void Reposition(float x, float z)
    {
        Phase = State.Neutral;
        Driven = default;
        _verticalVelocity = 0f;
        _airVelocity = Vector3.zero;
        _knockback = Vector3.zero;
        _nudge = Vector3.zero;
        SetBlock(false);
        transform.localPosition = new Vector3(x, 0f, z);
        SetY(BrawlGround.HeightAt(transform.position.x, transform.position.z, aboveY: 30f));
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
        // Every state below integrates against this, so Tempo reaches the walk,
        // the strike windows, the stagger, the fall and the get-up in one go.
        float dt = Time.deltaTime * _tempo;
        if (dt <= 0f)
            return;

        // The self-heal: embedded in solid terrain by ANY path — a spawn
        // inside a tier, a slide the blind probe mis-set — every ray-based
        // sense fails from inside a collider, so the one working sensor is
        // this overlap check. Swallowed by a COLUMN (a stalk, a pillar):
        // escape sideways, away from its axis — surfacing on top of a tree
        // trunk is not a rescue. Anything else: surface on top of it.
        Vector3 chest = transform.position + Vector3.up * 0.9f;
        if (Physics.CheckSphere(chest, 0.25f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            ArenaColumn column = null;
            foreach (var overlap in Physics.OverlapSphere(chest, 0.25f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                column = overlap.GetComponent<ArenaColumn>();
                if (column != null)
                    break;
            }
            if (column != null)
            {
                Vector3 away = transform.position - column.transform.position;
                away.y = 0f;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : -FacingDir;
                for (int step = 0; step < 14; step++)
                {
                    transform.position += away * 0.3f;
                    if (!Physics.CheckSphere(transform.position + Vector3.up * 0.9f, 0.25f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        break;
                }
                SetY(Ground);
            }
            else if (Physics.Raycast(new Vector3(transform.position.x, 40f, transform.position.z),
                         Vector3.down, out var surface, 45f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                SetY(surface.point.y);
        }

        switch (Phase)
        {
            case State.Neutral: TickNeutral(dt); break;
            case State.Air: TickAir(dt, hot: false); break;
            case State.Attacking: TickAttack(dt * ActionTempo); break;
            // The whole aerial tick accelerates, arc included: a fly kick
            // that only ticked its windows faster would close them mid-air.
            case State.AirAttack: TickAir(dt * ActionTempo, hot: true); break;
            case State.Blocking: TickBlocking(); break;
            case State.HitStun: TickHitStun(dt); break;
            case State.Knockdown: TickKnockdown(dt); break;
            case State.KO:
            case State.Celebrating:
                break;
        }

        // The graze shove, integrated in EVERY mobile state: unlike
        // _knockback (which only HitStun and Knockdown tick), this must
        // move a robot that is mid-swing or mid-walk without interrupting
        // what it is doing — impact you can see, no stagger.
        if (_nudge.sqrMagnitude > 1e-6f)
        {
            SlideAlongGround(_nudge * dt);
            _nudge = Vector3.MoveTowards(_nudge, Vector3.zero, 14f * dt);
        }

        Separate();
        ClampToLane();
        DriveAnimator(dt);
    }

    /// <summary>
    /// A shove with no stagger: the body gives a little ground while its
    /// current action continues. The impact read for limb hits — a strike
    /// that costs no health must still visibly land.
    /// </summary>
    public void Nudge(Vector3 shove)
    {
        if (Phase == State.Knockdown || Phase == State.KO || Phase == State.Celebrating)
            return;
        _nudge += shove;
    }

    // ------------------------------------------------------------- states

    void TickNeutral(float dt)
    {
        FaceOpponent();
        if (!FollowGround())
            return;   // walked or was carried off an edge — now falling
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
            _airVelocity = new Vector3(intent.move.x, 0f, intent.move.y) * BrawlMoveSet.WalkSpeed;
            // Silent by design, like the swings: the jump clip was the same
            // whoosh, and robots jump constantly — only impacts speak.
            return;
        }

        Vector3 stride = new Vector3(intent.move.x, 0f, intent.move.y);
        if (stride.sqrMagnitude > 1f)
            stride.Normalize();
        GroundWalk(stride * (BrawlMoveSet.WalkSpeed * dt), dt);
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
            _effectorPrevValid = false;
            // The lunge: committing adds forward speed toward the opponent.
            _airVelocity += FacingDir * 2.2f;
            Trigger(BrawlAnim.FlyKick);
            OnMoveStarted?.Invoke(_variant.display);
            hot = true;
        }

        if (hot)
        {
            _moveTime += dt;
            bool active = _moveTime >= _move.startup
                          && _moveTime <= _move.startup + _move.active;
            if (active && !_moveHasHit)
                TryHit();
            TrackEffector();
        }

        _verticalVelocity -= BrawlMoveSet.Gravity * dt;
        // Horizontal flight goes through the wall-checked mover: a wall
        // stops it dead instead of letting the body enter and pop out on
        // the block's lid.
        if (_airVelocity.sqrMagnitude > 1e-6f && !TryMove(_airVelocity * dt))
            _airVelocity = Vector3.zero;
        Move(0f, _verticalVelocity * dt);

        float landing = Ground;
        if (transform.position.y <= landing && _verticalVelocity <= 0f)
        {
            SetY(landing);
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
            // The lunge: the ROOT carries the move's forward commitment
            // (clips are in place), stopping when the window closes — or at
            // a wall. Raw lunging was the drill that sank punching robots
            // into block faces one press at a time.
            float window = _move.startup + _move.active;
            if (_variant.lunge > 0f && _moveTime <= window)
                TryMove(FacingDir * (_variant.lunge / window * dt));

            bool active = _moveTime >= _move.startup && _moveTime <= window;
            if (active && !_moveHasHit)
                TryHit();
            TrackEffector();
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
        if (!FollowGround())
        {
            SetBlock(false);
            return;
        }
        if (!Live.block)
        {
            SetBlock(false);
            Phase = State.Neutral;
        }
    }

    void TickHitStun(float dt)
    {
        _stunTime -= dt;
        SlideAlongGround(_knockback * dt);
        _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 12f * dt);
        if (_stunTime <= 0f)
            Phase = State.Neutral;
    }

    void TickKnockdown(float dt)
    {
        _floorTime -= dt;
        // The slide: a knocked-down robot travels, it doesn't drop in place.
        SlideAlongGround(_knockback * dt);
        _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 5f * dt);
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
        _propHitThisMove = false;
        _effectorPrevValid = false;
        // No swing whoosh by design: a bout at action tempo throws moves
        // constantly, and the air sound drowned the contact sounds that
        // actually carry information — only impacts speak.
        Trigger(_variant.trigger);
        OnMoveStarted?.Invoke(_variant.display);
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
                case BrawlMoveSet.Limb.RightKnee: return _kneeR;
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
        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        float gap = offset.magnitude;
        if (gap > _move.range + 1f)
            return;

        var effector = ActiveEffector;
        if (effector == null || !target.HasHurtboxes)
        {
            // Capsule-fallback robots: the tuned distance check.
            float dy = Mathf.Abs(target.transform.position.y - transform.position.y);
            float band = _move.move == BrawlMoveSet.Move.FlyKick ? 1.8f : 1.2f;
            if (gap <= _move.range && dy <= band)
            {
                _moveHasHit = true;
                target.TakeHit(_move, this);
            }
            return;
        }

        // Swept, not sampled: at action tempo the fist crosses more than a
        // forearm-width between frames, and the single-point test tunneled
        // clean through limbs — most visible overlaps registered nothing.
        // Walk last frame's bone position to this frame's in strike-radius
        // steps and take the first contact along the path.
        Vector3 current = effector.position;
        Vector3 start = _effectorPrevValid ? _effectorPrev : current;
        int steps = Mathf.Clamp(
            Mathf.CeilToInt((current - start).magnitude / BrawlMoveSet.StrikeRadius), 1, 8);
        BrawlBodyPart part = null;
        Vector3 contact = current;
        for (int i = 1; i <= steps && part == null; i++)
        {
            contact = Vector3.Lerp(start, current, i / (float)steps);
            part = BrawlHurtboxes.Query(contact,
                BrawlMoveSet.StrikeRadius + 0.04f, target);
        }
        if (part == null)
        {
            // No robot in reach — maybe a crate was.
            if (!_propHitThisMove
                && BrawlProps.TryStrike(current, BrawlMoveSet.StrikeRadius + 0.12f, this))
                _propHitThisMove = true;
            return;
        }
        OnPartStruck?.Invoke(part);
        if (part.Vital)
        {
            _moveHasHit = true;
            target.TakeHit(_move, this);
        }
        else if (!_grazedThisMove)
        {
            _grazedThisMove = true;
            // The struck robot gives ~a quarter metre of ground: no damage
            // for a limb hit, but contact must never read as touching
            // nothing.
            Vector3 away = gap > 1e-3f ? offset / gap : FacingDir;
            target.Nudge(away * 2.6f);
            VfxUtil.SpawnBurst(contact, new Color(0.8f, 0.95f, 1f), 5, 2.5f, 0.08f);
            BrawlAudio.Play(BrawlAudio.Id.Graze, contact, 0.85f);
            OnGrazed?.Invoke(part.Label);
        }
    }

    /// <summary>
    /// Record the striking bone's position for next frame's sweep. Runs
    /// every frame of a strike — startup included, so the first active
    /// frame already has a path behind it.
    /// </summary>
    void TrackEffector()
    {
        var effector = ActiveEffector;
        if (effector == null)
            return;
        _effectorPrev = effector.position;
        _effectorPrevValid = true;
    }

    public void TakeHit(BrawlMoveSet.Data hit, BrawlFighter attacker)
    {
        if (Phase == State.Knockdown || Phase == State.KO || Phase == State.Celebrating)
            return;

        Vector3 away = transform.position - attacker.transform.position;
        away.y = 0f;
        away = away.sqrMagnitude > 1e-4f ? away.normalized : attacker.FacingDir;

        Vector3 chest = transform.position - away * 0.35f + Vector3.up * 1.2f;

        // A standing guard eats the hit: no damage (kid rules — no chip),
        // a shove instead of a stagger.
        if (Phase == State.Blocking && !IsAirborne)
        {
            _knockback = away * 2.5f;
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
        SetY(Ground);
        if (knockdown)
        {
            Phase = State.Knockdown;
            _floorTime = BrawlMoveSet.KnockdownTime + BrawlMoveSet.GetUpTime;
            _getUpFired = false;
            // The clips play in place, so this slide IS the being-blown-
            // backward — ~2.3 m before it decays, and the rise happens
            // wherever it ends.
            _knockback = away * (BrawlMoveSet.HitKnockback * 6f);
            Trigger(BrawlAnim.Knockdown);
        }
        else
        {
            Phase = State.HitStun;
            _stunTime = BrawlMoveSet.HitStun;
            // The visible shove: ~1 m of ground given along the attack
            // direction. The old 0.4 m vanished under hit-stop and the
            // attacker's own advance, and hits read as no reaction at all.
            _knockback = away * 4.8f;
            Trigger(BrawlAnim.Hit);
        }
    }

    /// <summary>A burst crate's gift: patched up or powered up, coin flip.</summary>
    public void GrantPickup()
    {
        if (Random.value < 0.5f)
            Health = Mathf.Min(BrawlMoveSet.MaxHealth, Health + 12f);
        else
            GainCharge(0.4f);
    }

    /// <summary>The repair kit's patch-up: a real chunk of health, green fizz.</summary>
    public void Heal(float amount)
    {
        Health = Mathf.Min(BrawlMoveSet.MaxHealth, Health + amount);
        VfxUtil.SpawnBurst(transform.position + Vector3.up * 1.2f,
            new Color(0.35f, 1f, 0.55f), 12, 2.8f, 0.13f);
    }

    /// <summary>
    /// Environmental damage — a mine, a fire — radiating from a POINT with
    /// no attacker to credit: both sides eat it the same. Blocking still
    /// works (kid rules hold), knockback runs away from the point, and a
    /// zeroed bar still ends the round properly.
    /// </summary>
    public void TakeAreaHit(int damage, Vector3 fromPoint, bool heavy)
    {
        if (Phase == State.Knockdown || Phase == State.KO || Phase == State.Celebrating)
            return;

        Vector3 away = transform.position - fromPoint;
        away.y = 0f;
        away = away.sqrMagnitude > 1e-4f ? away.normalized : -FacingDir;
        Vector3 chest = transform.position + Vector3.up * 1.2f;

        if (Phase == State.Blocking && !IsAirborne)
        {
            _knockback = away * 3f;
            _stunTime = BrawlMoveSet.HitStun * 0.6f;
            Phase = State.HitStun;
            VfxUtil.SpawnBurst(chest, _tint, 6, 3f, 0.10f);
            BrawlAudio.Play(BrawlAudio.Id.Block, chest, 0.8f);
            return;
        }

        Health = Mathf.Max(0f, Health - damage);
        SetBlock(false);
        VfxUtil.ImpactBurst(chest, new Color(1f, 0.75f, 0.4f));
        GainCharge(damage / 130f);   // absorbing still trickles the comeback
        BrawlAudio.Play(heavy ? BrawlAudio.Id.HitHeavy : BrawlAudio.Id.Hit, chest, 0.9f);

        if (Health <= 0f)
        {
            KnockOut();
            VfxUtil.Explosion(transform.position + Vector3.up, _tint, 0.7f);
            BrawlAudio.Play(BrawlAudio.Id.KO, transform.position + Vector3.up);
            OnKnockedOut?.Invoke(this);
            return;
        }

        _verticalVelocity = 0f;
        SetY(Ground);
        if (heavy)
        {
            Phase = State.Knockdown;
            _floorTime = BrawlMoveSet.KnockdownTime + BrawlMoveSet.GetUpTime;
            _getUpFired = false;
            _knockback = away * (BrawlMoveSet.HitKnockback * 6f);
            Trigger(BrawlAnim.Knockdown);
        }
        else
        {
            Phase = State.HitStun;
            _stunTime = BrawlMoveSet.HitStun;
            _knockback = away * 4.8f;
            Trigger(BrawlAnim.Hit);
        }
    }

    /// <summary>
    /// A geyser's pop: straight up, no damage. Only interrupts states where
    /// leaving the ground makes sense — mid-attack and floored robots keep
    /// their choreography.
    /// </summary>
    public void LaunchUp(float velocity)
    {
        if (Phase == State.Neutral || Phase == State.HitStun || Phase == State.Blocking)
        {
            SetBlock(false);
            Phase = State.Air;
            _airVelocity = Vector3.zero;
            _verticalVelocity = velocity;
            // The geyser's own eruption already sounds; the body it throws
            // adds nothing.
        }
        else if (Phase == State.Air || Phase == State.AirAttack)
        {
            _verticalVelocity = Mathf.Max(_verticalVelocity, velocity);
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

    /// <summary>
    /// Grounded fighters follow the surface under them — a rising lift
    /// carries them up, a sinking one lowers them, a crate bursting
    /// underfoot drops them into a fall. False = now airborne.
    /// </summary>
    bool FollowGround()
    {
        float ground = Ground;
        float y = transform.position.y;
        if (y > ground + StepUp)
        {
            // The floor left (or never was): fall from here.
            Phase = State.Air;
            _verticalVelocity = 0f;
            _airVelocity = Vector3.zero;
            return false;
        }
        if (!Mathf.Approximately(y, ground))
            SetY(ground);
        return true;
    }

    /// <summary>
    /// The one horizontal mover everything routes through, any direction
    /// on the plane. Walls are found by HORIZONTAL rays toward the move at
    /// knee-plus and chest height — vertical probes are blind to any block
    /// taller than their start (a ray born inside a collider hits
    /// nothing). False = a wall stopped the move.
    /// </summary>
    bool TryMove(Vector3 delta)
    {
        delta.y = 0f;
        float length = delta.magnitude;
        if (length < 1e-6f)
            return true;
        Vector3 direction = delta / length;
        float reach = length + BrawlMoveSet.BodyHalfWidth;
        Vector3 feet = transform.position;
        if (Physics.Raycast(feet + Vector3.up * (StepUp + 0.15f), direction, reach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            || Physics.Raycast(feet + Vector3.up * 1.4f, direction, reach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        transform.position = feet + delta;
        return true;
    }

    /// <summary>
    /// Knockback travel over terrain: follows the ground down and over
    /// small steps, but a wall — an arena block, a cargo stack — stops the
    /// shove dead instead of embedding the robot. Hard stops ring the
    /// wall-slam bell.
    /// </summary>
    void SlideAlongGround(Vector3 delta)
    {
        if (delta.sqrMagnitude < 1e-8f)
            return;
        if (!TryMove(delta))
        {
            if (_knockback.magnitude > 2f)
                OnWallSlam?.Invoke(this, _knockback.magnitude);
            _knockback = Vector3.zero;
            return;
        }
        SetY(Ground);
    }

    /// <summary>
    /// Walking over terrain: small ledges are stepped onto, tall stacks
    /// are walls, and edges are walked off into a fall.
    /// </summary>
    void GroundWalk(Vector3 delta, float dt)
    {
        if (delta.sqrMagnitude < 1e-8f)
            return;
        float y = transform.position.y;
        if (!TryMove(delta))
            return;   // a wall at the shoulder — jump it instead
        float floor = Ground;   // what's under the CENTRE decides footing
        if (floor >= y - StepUp)
            SetY(floor);
        else
        {
            // Walked off the edge: keep the stride as air momentum.
            Phase = State.Air;
            _verticalVelocity = 0f;
            _airVelocity = delta / Mathf.Max(dt, 1e-4f);
        }
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
        Vector3 gap = transform.position - Opponent.transform.position;
        gap.y = 0f;
        float distance = gap.magnitude;
        float overlap = BrawlMoveSet.MinSeparation - distance;
        if (overlap <= 0f)
            return;
        Vector3 direction = distance > 1e-4f ? gap / distance : Vector3.right;
        // Wall-checked: never squeezed into a face — the opponent's own
        // Separate carries the spacing at cargo walls.
        TryMove(direction * (overlap * 0.5f));
    }

    void ClampToLane()
    {
        var p = transform.position;
        var half = BrawlStage.BoundsHalf;
        float x = Mathf.Clamp(p.x, -half.x, half.x);
        float z = Mathf.Clamp(p.z, -half.y, half.y);
        if (x != p.x || z != p.z)
        {
            // Driven into the ring's edge by a shove — the crystal corners
            // (and anything else watching) get to react.
            if (_knockback.magnitude > 2f)
            {
                OnWallSlam?.Invoke(this, _knockback.magnitude);
                _knockback = Vector3.zero;
            }
            transform.position = new Vector3(x, p.y, z);
        }
    }

    /// <summary>Extra stagger from stage hazards — only stretches an existing stun.</summary>
    public void AddStun(float seconds)
    {
        if (Phase == State.HitStun)
            _stunTime += seconds;
        else if (Phase == State.Knockdown)
            _floorTime += seconds * 0.5f;
    }

    void FaceOpponent()
    {
        if (Opponent == null)
            return;
        Vector3 toFoe = Opponent.transform.position - transform.position;
        toFoe.y = 0f;
        if (toFoe.sqrMagnitude < 1e-4f)
            return;
        FacingDir = toFoe.normalized;
        // Robots model +Z as forward: look straight at the opponent.
        transform.rotation = Quaternion.LookRotation(FacingDir, Vector3.up);
    }

    // ------------------------------------------------------------ animator

    void DriveAnimator(float dt)
    {
        if (_animator == null)
            return;
        // The playback rate keeps step with whichever clock the current
        // state integrates against — the Tempo contract, extended to the
        // attack multiplier.
        _animator.speed = _tempo * (Striking ? ActionTempo : 1f);
        float target = Phase == State.Neutral
            ? Mathf.Min(1f, Live.move.magnitude) * BrawlMoveSet.WalkSpeed : 0f;
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
