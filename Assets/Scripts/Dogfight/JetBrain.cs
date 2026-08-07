using UnityEngine;

/// <summary>
/// The CPU pilot. It writes exactly what a player writes — <c>Steer</c>,
/// <c>Throttle</c>, <c>Firing</c>, and now the ordnance calls — and nothing
/// else, so the pawn cannot tell who is flying it.
///
/// The core is still TankBrain's rule lifted into the air — GET BEHIND AND
/// STAY THERE — but three things keep two of these from flying the same
/// fight forever, which is exactly what the first AI WAR did (a perfect
/// carousel: both pilots in each other's saddle point, circling until the
/// heat death of the battery):
///
/// 1. PERSONALITY. Every knob that shapes the flying — think cadence, saddle
///    distance, aim smear, helix phase — is rolled once at wake-up, so no
///    two pilots are the same pilot in two paint jobs.
/// 2. THE CUT. A pilot that notices the range hasn't changed in six seconds
///    stops following the circle and flies the CHORD — a pure-lead intercept
///    across it at full boost. That is the move that ends a carousel in a
///    real dogfight, and it ends this one.
/// 3. THE SKY FIGHTS BACK. Turret missiles force breaks and flares on a
///    clock nobody controls, and a fight that keeps getting interrupted
///    cannot settle into a loop.
///
/// Missiles get the same honesty rules as the guns: the brain has to hold
/// the cone for most of a second before it fires, waits a rolled reaction
/// beat before its hand finds the flare button, and never dumps the whole
/// pocket at one threat.
/// </summary>
/// <remarks>Runs at the drivers' order, before the pawns, exactly as the
/// mode itself does.</remarks>
[DefaultExecutionOrder(-50)]
public class JetBrain : MonoBehaviour
{
    // ------------------------------------------------------------ fixed tuning

    /// <summary>Inside this, track the lead instead of the saddle point.</summary>
    const float GunRange = 34f;

    /// <summary>An enemy this close, behind me and pointed at me, is a tail.</summary>
    const float TailedRange = 38f;
    const float BreakSeconds = 1.6f;

    /// <summary>
    /// Fire guns this close to on-target. WIDE on purpose, and measured
    /// against the true lead every frame rather than the think-beat's
    /// jittered goal: the first cut of this brain gated its trigger behind
    /// both, and two AI pilots flew a whole war without firing a shot. A
    /// pilot who is roughly behind someone should be HOSING — the pawn's own
    /// assist converts the last few degrees into hits, the rest is tracer,
    /// and tracer is what a dogfight looks like from the couch.
    /// </summary>
    const float FireCone = 18f;
    const float FireRange = 85f;

    /// <summary>
    /// Missile discipline: the cone must be HELD, not visited — but briefly,
    /// and from wherever the fight actually happens. The near limit sat at
    /// 20 m for a pilot whose own pursuit parks 9-14 m behind its target:
    /// geometry went permanently false the moment the chase succeeded, and
    /// no missile ever flew. It now reaches inside the saddle (the proximity
    /// fuse and the ten-metre gap keep the shooter clear of its own splash),
    /// and the cone is wide enough to mature against a target that jinks.
    /// </summary>
    const float MissileCone = 20f;
    const float MissileHoldSeconds = 0.3f;
    const float MissileRangeNear = 10f;
    const float MissileRangeFar = 80f;

    /// <summary>
    /// A missile chasing me is TRACKED from here — but the full defensive
    /// break waits until it closes to <see cref="EvadeRange"/>. The first
    /// cut of this brain broke the moment anything launched, and with a
    /// four-turret battery cycling against two jets that meant a missile
    /// was "inbound" almost always: every pilot spent the whole war
    /// breaking and flaring, and the guns never spoke. Distant missiles
    /// are now something you keep fighting through; the flare hand still
    /// moves at <see cref="FlareRange"/>.
    /// </summary>
    const float ThreatRange = 60f;
    const float EvadeRange = 32f;
    const float FlareRange = 30f;

    /// <summary>Batteries are worth guns from here — hunted deliberately
    /// between kills, strafed opportunistically whenever one drifts
    /// through the pipper mid-duel.</summary>
    const float GroundGunRange = 100f;

    /// <summary>The carousel detector: this many think-beats of remembered
    /// range, and the spread below which a circle is declared. Six-ish
    /// seconds of "nothing is changing" at the rolled cadences.</summary>
    const int RangeMemory = 24;
    const float CarouselSpread = 5f;
    const float CutSeconds = 2.4f;

    public JetPawn pawn;
    public JetPawn quarry;

    /// <summary>+1 or -1, rolled once at spawn: which way this pilot always
    /// breaks. Two jets that break mirrored ways make a shape; two that share
    /// one make a queue.</summary>
    public float breakSign = 1f;

    /// <summary>The CPU level this pilot flies (public: survives a mid-Play
    /// recompile with the other seat assignments). Defaults to the baseline,
    /// so the AI war and any brain nobody configured fly the shipped tuning.</summary>
    public int skillLevel = DogfightDifficulty.Baseline;

    DogfightDifficulty.Level Dials => DogfightDifficulty.Get(skillLevel);

    /// <summary>Take a level: re-roll every personality knob the level owns.
    /// Called right after AddComponent — Awake has already rolled baseline
    /// values by then, so this simply rolls again inside the level's bounds.</summary>
    public void ApplySkill(int level)
    {
        skillLevel = level;
        var dials = Dials;
        _think = Random.Range(dials.thinkMin, dials.thinkMax);
        _jitterDegrees = Random.Range(dials.jitterMin, dials.jitterMax);
        _missileAt = Time.time
            + Random.Range(dials.missileEveryMin, dials.missileEveryMax) * 0.7f;
    }

    // -------------------------------------------------------- rolled at wake-up

    float _think;           // seconds between decisions
    float _saddle;          // metres behind the quarry the pursuit aims for
    float _jitterDegrees;   // honest aim smear
    float _helixPhase;      // where this pilot is in its climb-dive weave
    float _flareDelay;      // hand-to-button time under threat
    float _groundAffinity;  // how much this pilot likes fighting off the deck

    // ------------------------------------------------------------------- state

    float _nextThink;
    float _breakingUntil;
    Vector3 _goal;
    bool _goalIsLead;

    DogfightMissile _threat;
    float _flareAt = -1f;
    float _nextFlareAllowed;
    // Rolled per THREAT, not per pilot: a low-level squadron where the same
    // jet always eats every missile reads as one broken pilot, not a level.
    bool _evadesThreat;
    bool _flaresThreat;

    readonly float[] _ranges = new float[RangeMemory];
    int _rangeCount;
    int _rangeHead;
    float _cutUntil;
    float _cutCooldownUntil;

    float _coneHeldSince = -1f;
    float _missileAt;

    float _phaseCheckAt;
    float _phaseUntil;

    void Awake()
    {
        // The personality roll. Ranges chosen so the worst draw is still a
        // fair fight and the best draw is still beatable.
        _think = Random.Range(0.22f, 0.34f);
        _saddle = Random.Range(9f, 14f);
        _jitterDegrees = Random.Range(2f, 3.2f);
        _helixPhase = Random.Range(0f, Mathf.PI * 2f);
        _flareDelay = Random.Range(0.25f, 0.45f);
        _groundAffinity = Random.Range(0.4f, 1f);
        _missileAt = Time.time + Random.Range(4f, 9f);
        _cutCooldownUntil = Time.time + Random.Range(3f, 8f);
        _phaseCheckAt = Time.time + Random.Range(8f, 16f);
    }

    void Update()
    {
        if (pawn == null || pawn.IsDown || !pawn.FlightOn || pawn.Morphing)
        {
            _threat = null;
            _coneHeldSince = -1f;
            return;
        }

        if (quarry == null || quarry.IsDown)
            quarry = JetPawn.NearestEnemy(pawn.transform.position, pawn.Team);

        FindThreat();
        ThinkForms();

        if (pawn.CurrentForm == JetPawn.Form.Tank)
        {
            FightAsTank();
            // Grounded, the only answer to a missile is the flare pocket —
            // the fighting continues around it.
            if (_threat != null)
                TickFlares(Vector3.Distance(_threat.transform.position,
                    pawn.transform.position));
            return;
        }

        if (Time.time >= _nextThink)
        {
            _nextThink = Time.time + _think;
            Think();
        }

        // Break only for a missile that is actually ARRIVING; a distant
        // launch is tracked (and flared if it gets close) while the fight
        // goes on. Whether THIS pilot breaks at all was rolled when the
        // threat appeared — low levels mostly fly on and eat it.
        if (_threat != null && _evadesThreat
            && Vector3.Distance(_threat.transform.position, pawn.transform.position)
               < EvadeRange)
            EvadeMissile();
        else
            Fly();
    }

    // ---------------------------------------------------------------- the forms

    /// <summary>
    /// When to stop being a jet. Landing as a tank answers an enemy that has
    /// already landed (a duel the tank's turret and armour want) and offers a
    /// hurt pilot a steadier gun; the timer — and any real emergency — sends
    /// it back into the sky. Checked on a slow clock so form changes read as
    /// decisions, not twitches.
    /// </summary>
    void ThinkForms()
    {
        if (pawn.CurrentForm == JetPawn.Form.Jet)
        {
            if (Time.time < _phaseCheckAt)
                return;
            _phaseCheckAt = Time.time + 6f;

            float urge = 0.12f;
            if (quarry != null && quarry.CurrentForm == JetPawn.Form.Tank)
                urge += 0.4f;
            if (pawn.Shield != null && pawn.Shield.Normalized < 0.55f)
                urge += 0.22f;
            // Never fold up over the void — a tank parachuted onto the
            // station's energy net has opted out of the fight.
            if (!DogfightSky.OverVoid(pawn.transform.position)
                && Random.value < urge * _groundAffinity
                && pawn.RequestForm(JetPawn.Form.Tank))
                _phaseUntil = Time.time + Random.Range(12f, 20f);
            return;
        }

        if (pawn.CurrentForm == JetPawn.Form.Tank)
        {
            bool bleedingOut = pawn.Shield != null && pawn.Shield.Normalized < 0.25f;
            if (Time.time >= _phaseUntil || bleedingOut)
                pawn.RequestForm(JetPawn.Form.Jet);
        }
        // Robot is a doorway, not a destination — the chained folds pass
        // through it on their own.
    }

    /// <summary>
    /// Deck fighting: hull faces the work, turret and lead assist do the
    /// aiming, missiles go up at whatever is locked, and with no jet worth
    /// shooting the nearest battery gets the barrel instead — a tank that
    /// clears turrets is pulling the same weight as one that guards the sky.
    /// </summary>
    void FightAsTank()
    {
        Vector3 me = pawn.transform.position;

        Vector3 aimAt;
        float distance;
        bool aimingAtJet = false;
        if (quarry != null)
        {
            Vector3 gap = quarry.Center - me;
            distance = gap.magnitude;
            aimAt = quarry.Center + quarry.Velocity * (distance / 90f);
            aimingAtJet = true;
        }
        else
        {
            var battery = DogfightTurret.Nearest(me, 110f);
            if (battery == null)
            {
                pawn.Steer = Vector2.zero;
                pawn.Firing = false;
                return;
            }
            aimAt = battery.Center;
            distance = (battery.Center - me).magnitude;
        }

        pawn.AimAt(aimAt);

        // The hull: face the fight, jockey a little so the silhouette never
        // sits still. The turret does the fine aiming on its own.
        Vector3 flat = aimAt - me;
        flat.y = 0f;
        float off = Vector3.SignedAngle(pawn.transform.forward,
            flat.sqrMagnitude > 1e-4f ? flat.normalized : pawn.transform.forward, Vector3.up);
        pawn.Steer = new Vector2(Mathf.Clamp(off / 45f, -1f, 1f),
            Mathf.Sin(Time.time * 0.7f + _helixPhase) * 0.35f);

        pawn.Firing = distance < 95f;

        if (aimingAtJet && quarry != null)
        {
            float coneOff = Vector3.Angle(pawn.AimDirection, quarry.Center - me);
            bool geometry = coneOff < MissileCone
                            && distance > MissileRangeNear && distance < MissileRangeFar;
            if (!geometry)
                _coneHeldSince = -1f;
            else if (_coneHeldSince < 0f)
                _coneHeldSince = Time.time;
            else if (Time.time - _coneHeldSince >= MissileHoldSeconds
                     && Time.time >= _missileAt
                     && pawn.TryFireMissile(quarry.transform))
                _missileAt = Time.time
                    + Random.Range(Dials.missileEveryMin, Dials.missileEveryMax);
        }

        // Tails and carousels are sky problems; forget them down here.
        _breakingUntil = 0f;
        _rangeCount = 0;
    }

    // ------------------------------------------------------------- the threats

    /// <summary>Is anything chasing ME? The registry answer, every frame —
    /// steering away can start instantly; only the flare hand is delayed.</summary>
    void FindThreat()
    {
        if (_threat != null && (_threat.Quarry != pawn.transform
                                || (_threat.transform.position - pawn.transform.position)
                                    .sqrMagnitude > ThreatRange * ThreatRange * 1.4f))
        {
            _threat = null;
            _flareAt = -1f;
        }
        if (_threat != null)
            return;

        foreach (var missile in DogfightMissile.All)
        {
            if (missile == null || missile.Quarry != pawn.transform)
                continue;
            if ((missile.transform.position - pawn.transform.position).sqrMagnitude
                > ThreatRange * ThreatRange)
                continue;
            _threat = missile;
            _flareAt = -1f;
            // The level's dice, thrown once per threat: does this pilot
            // notice in time to break, and does the flare hand move?
            _evadesThreat = Random.value < Dials.evadeChance;
            _flaresThreat = Random.value < Dials.flareChance;
            break;
        }
    }

    /// <summary>
    /// The break that beats a missile: turn hard across its nose the way this
    /// pilot always turns, weave in pitch, run the burner — and when it
    /// closes, flares, one rolled reaction beat late, never the whole pocket.
    /// The geometry plus the flares is the honest counter the missiles were
    /// tuned against.
    /// </summary>
    void EvadeMissile()
    {
        float distance = Vector3.Distance(_threat.transform.position,
            pawn.transform.position);

        pawn.Steer = new Vector2(breakSign, Mathf.Sin(Time.time * 6f) * 0.8f);
        pawn.Throttle = Dials.throttleCap;

        TickFlares(distance);

        // The break owns the stick, not the trigger: a snapshot through the
        // turn whenever the geometry allows is half of what a dogfight
        // looks like.
        FightGuns(pawn.transform.position);
    }

    /// <summary>The flare hand, one rolled reaction beat late and never the
    /// whole pocket at one threat. Shared by the break and the deck.</summary>
    void TickFlares(float threatDistance)
    {
        if (!_flaresThreat || threatDistance >= FlareRange
            || Time.time < _nextFlareAllowed)
            return;
        if (_flareAt < 0f)
        {
            _flareAt = Time.time + _flareDelay;
        }
        else if (Time.time >= _flareAt)
        {
            pawn.TryPopFlares();
            _flareAt = -1f;
            // One burst per pass: a missile that ate the flare is gone, and
            // one that didn't will still be here in a second.
            _nextFlareAllowed = Time.time + 1.2f;
        }
    }

    // ------------------------------------------------------------- the pursuit

    /// <summary>Pick this beat's goal point, remember the range, and declare
    /// a carousel when six seconds of memory all say the same number.</summary>
    void Think()
    {
        if (quarry == null)
        {
            // Nobody in the sky to fight: spend the respawn beat on the
            // batteries — the same gun-run shape flown against a grounded
            // enemy, aimed at whatever the ring still has standing. Only an
            // empty battery leaves a lap of the spawn ring.
            var battery = DogfightTurret.Nearest(pawn.transform.position);
            if (battery != null)
            {
                Vector3 toBattery = battery.Center - pawn.transform.position;
                if (Vector3.Angle(pawn.transform.forward, toBattery) < 25f
                    && toBattery.magnitude < 90f)
                {
                    _goal = battery.Center;
                    _goalIsLead = true;
                }
                else
                {
                    Vector3 ring = Quaternion.Euler(0f, Time.time * 24f * breakSign, 0f)
                                   * Vector3.forward * 30f;
                    _goal = battery.Center + ring + Vector3.up * 42f;
                    _goalIsLead = false;
                }
                return;
            }

            Vector3 flat = new Vector3(pawn.transform.position.x, 0f, pawn.transform.position.z);
            if (flat.sqrMagnitude < 1f)
                flat = Vector3.forward;
            _goal = Quaternion.Euler(0f, 40f, 0f) * flat.normalized * DogfightSky.SpawnRing;
            _goal.y = DogfightSky.SpawnAltitude;
            _goalIsLead = false;
            return;
        }

        Vector3 me = pawn.transform.position;
        Vector3 them = quarry.transform.position;
        Vector3 gap = them - me;
        float distance = gap.magnitude;

        // A grounded enemy is not a dogfight, it is a gun run: orbit above
        // it (the floor assist owns the deck below ~38 m, so the run stays a
        // slant rather than a dive into the assist's argument), roll into
        // the lead when the nose comes around, off again with the orbit.
        if (quarry.CurrentForm != JetPawn.Form.Jet && quarry.Grounded)
        {
            bool onThePass = Vector3.Angle(pawn.transform.forward, gap) < 25f
                             && distance < 80f;
            if (onThePass)
            {
                Vector3 lead = quarry.Center + quarry.Velocity * (distance / 90f);
                _goal = lead + Random.insideUnitSphere
                        * (distance * Mathf.Tan(_jitterDegrees * Mathf.Deg2Rad));
                _goalIsLead = true;
            }
            else
            {
                Vector3 ring = Quaternion.Euler(0f, Time.time * 24f * breakSign, 0f)
                               * Vector3.forward * 26f;
                _goal = them + ring + Vector3.up * 40f;
                _goalIsLead = false;
            }
            _rangeCount = 0;        // no carousel bookkeeping against the deck
            return;
        }

        RememberRange(distance);

        bool tailed = distance < TailedRange
                      && Vector3.Dot(quarry.transform.forward, -gap.normalized) > 0.75f
                      && Vector3.Dot(pawn.transform.forward, gap.normalized) < 0.1f;
        if (tailed && Time.time >= _breakingUntil)
            _breakingUntil = Time.time + BreakSeconds;

        bool headOn = distance < 26f
                      && Vector3.Dot(pawn.transform.forward, quarry.transform.forward) < -0.6f
                      && Vector3.Dot(pawn.transform.forward, gap.normalized) > 0.6f;

        if (headOn)
        {
            _goal = me + pawn.transform.right * (breakSign * 30f) + Vector3.up * 6f;
            _goalIsLead = false;
        }
        else if (Time.time < _cutUntil || distance <= GunRange)
        {
            // In guns — or cutting the carousel's chord, which flies the same
            // math at more boost: the lead, smeared by this pilot's honest
            // couple of degrees.
            Vector3 lead = them + quarry.Velocity * (distance / 90f);
            _goal = lead + Random.insideUnitSphere
                    * (distance * Mathf.Tan(_jitterDegrees * Mathf.Deg2Rad));
            _goalIsLead = true;
        }
        else
        {
            // The saddle — with this pilot's own helix on it, so a long chase
            // corkscrews instead of drawing a flat circle.
            _goal = them - quarry.transform.forward * _saddle;
            _goal.y += Mathf.Sin(Time.time * 0.45f + _helixPhase) * 5.5f;
            _goalIsLead = false;
        }
    }

    /// <summary>Six seconds of range in a ring buffer. All the same number →
    /// this is a carousel → fly the chord for a beat (once per rolled
    /// cooldown, so two pilots rarely cut together and the fight resolves).</summary>
    void RememberRange(float distance)
    {
        if (Time.time < _cutUntil)
            return;
        _ranges[_rangeHead] = distance;
        _rangeHead = (_rangeHead + 1) % RangeMemory;
        _rangeCount = Mathf.Min(_rangeCount + 1, RangeMemory);
        if (_rangeCount < RangeMemory || Time.time < _cutCooldownUntil)
            return;

        float min = float.MaxValue, max = float.MinValue;
        foreach (var range in _ranges)
        {
            min = Mathf.Min(min, range);
            max = Mathf.Max(max, range);
        }
        if (max - min < CarouselSpread)
        {
            _cutUntil = Time.time + CutSeconds;
            _cutCooldownUntil = Time.time + Random.Range(6f, 10f);
            _rangeCount = 0;
        }
    }

    /// <summary>Every frame: steer at the goal, set the throttle by the
    /// geometry, squeeze guns when close, and put a missile up only after the
    /// cone has been HELD.</summary>
    void Fly()
    {
        Vector3 me = pawn.transform.position;
        bool breaking = Time.time < _breakingUntil;

        if (breaking)
        {
            pawn.Steer = new Vector2(breakSign, Mathf.Sin(Time.time * 5f) * 0.6f);
            pawn.Throttle = Dials.throttleCap;
            pawn.Firing = false;
            _coneHeldSince = -1f;
            return;
        }

        Vector3 toGoal = _goal - me;
        Vector3 local = pawn.transform.InverseTransformDirection(toGoal.normalized);
        var steer = new Vector2(
            Mathf.Clamp(local.x * 2.2f, -1f, 1f),
            Mathf.Clamp(local.y * 2.2f, -1f, 1f));
        if (local.z < -0.2f)
            steer.x = breakSign;
        pawn.Steer = steer;

        float distance = quarry != null
            ? Vector3.Distance(me, quarry.transform.position)
            : toGoal.magnitude;
        bool cutting = Time.time < _cutUntil;
        // The burner is a level privilege: a ROOKIE trundles at near-cruise,
        // which is what makes it a target a first-timer can track.
        pawn.Throttle = cutting || distance > 45f
            ? Dials.throttleCap : distance < 16f ? -0.5f : 0f;

        FightGuns(me);
    }

    /// <summary>
    /// The trigger, every frame, against the TARGET — not against whatever
    /// goal the last think-beat left behind. The target is the enemy jet
    /// while one is up, the nearest battery while none is (a wrecked enemy's
    /// respawn beat is turret-hunting time, not patrol time) — and even
    /// mid-duel, a battery drifting through the pipper gets the guns for
    /// free. Missiles follow any held half-second of alignment on whichever
    /// target is primary. What the fight looks like is made here as much as
    /// in the steering.
    /// </summary>
    void FightGuns(Vector3 me)
    {
        Vector3 lead;
        Vector3 toTarget;
        Transform lockRoot;
        bool jetTarget = quarry != null && !quarry.IsDown;
        if (jetTarget)
        {
            toTarget = quarry.Center - me;
            lead = quarry.Center + quarry.Velocity * (toTarget.magnitude / 90f) - me;
            lockRoot = quarry.transform;
        }
        else
        {
            var battery = DogfightTurret.Nearest(me, 130f);
            if (battery == null)
            {
                pawn.Firing = false;
                _coneHeldSince = -1f;
                return;
            }
            toTarget = battery.Center - me;
            lead = toTarget;                     // buildings hold still
            lockRoot = battery.transform;
        }

        float distance = toTarget.magnitude;
        float offLead = Vector3.Angle(pawn.transform.forward, lead);
        bool facing = Vector3.Dot(pawn.transform.forward, toTarget.normalized) > 0.15f;

        pawn.Firing = facing && distance < FireRange && offLead < FireCone;

        // The opportunist's rule: a battery lined up mid-duel is spectacle
        // nobody has to steer for.
        if (!pawn.Firing && jetTarget)
        {
            var battery = DogfightTurret.Nearest(me, GroundGunRange);
            if (battery != null)
            {
                Vector3 toBattery = battery.Center - me;
                pawn.Firing = Vector3.Dot(pawn.transform.forward, toBattery.normalized) > 0.15f
                              && Vector3.Angle(pawn.transform.forward, toBattery) < FireCone;
            }
        }

        bool missileGeometry = facing
                               && offLead < MissileCone
                               && distance > MissileRangeNear
                               && distance < MissileRangeFar;
        if (!missileGeometry)
        {
            _coneHeldSince = -1f;
        }
        else if (_coneHeldSince < 0f)
        {
            _coneHeldSince = Time.time;
        }
        else if (Time.time - _coneHeldSince >= MissileHoldSeconds
                 && Time.time >= _missileAt
                 && pawn.TryFireMissile(lockRoot))
        {
            _missileAt = Time.time
                + Random.Range(Dials.missileEveryMin, Dials.missileEveryMax);
        }
    }
}
