using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating damage numbers: "-6" jumping off a robot as its shield drops.
///
/// Shields drain silently. The only reading of a landed hit was the bar over
/// the robot's head getting slightly shorter, which at arena range is no
/// reading at all — you could empty a magazine into a Titan and not know
/// whether the bolts were landing, whether it was armoured, or whether the two
/// shots that mattered were the two that missed. The number answers all three.
///
/// This half is the listener: one per shielded robot, so the victim of a hit is
/// known by construction. EnergyShield.OnDamaged carries the damage and where
/// it landed but not whose shield moved, and the alternative — finding the
/// nearest shield to the hit point — guesses wrong exactly when it matters, on
/// two robots fighting at arm's length.
///
/// Self-bootstraps and rescans, like FloatingShieldBar next door: robots arrive
/// mid-match (gold reinforcements, decoys) and none of this is scene-wired.
/// <see cref="DamageNumberBoard"/> does the drawing.
/// </summary>
[RequireComponent(typeof(EnergyShield))]
public class DamageNumbers : MonoBehaviour
{
    static readonly Color DealtColor = new Color(1f, 0.95f, 0.7f);
    static readonly Color TakenColor = new Color(1f, 0.42f, 0.38f);

    EnergyShield _shield;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        new GameObject("DamageNumberSpawner").AddComponent<Spawner>();
    }

    /// <summary>Tags every shielded robot; rescans so late arrivals get one too.</summary>
    class Spawner : MonoBehaviour
    {
        float _nextScan;

        void Update()
        {
            if (Time.time < _nextScan)
                return;
            _nextScan = Time.time + 2f;
            foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
                if (shield.GetComponent<DamageNumbers>() == null)
                    shield.gameObject.AddComponent<DamageNumbers>();
        }
    }

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
        _shield.OnDamaged += HandleDamaged;
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDamaged -= HandleDamaged;
    }

    /// <summary>Numbers belong to a gunfight; the other modes have their own language.</summary>
    static bool InGunfight()
    {
        var gmc = GameModeController.Instance;
        return gmc != null && (gmc.Mode == GameMode.PlayerVsAI
            || gmc.Mode == GameMode.AIvAI
            || gmc.Mode == GameMode.OnlinePvP);
    }

    void HandleDamaged(float damage, Vector3 hitPoint)
    {
        if (damage <= 0f || !InGunfight())
            return;
        if (!ShouldShow(out Color color))
            return;
        DamageNumberBoard.Report(transform, hitPoint, damage, color);
    }

    /// <summary>
    /// Whether this hit is worth a number.
    ///
    /// With somebody playing it is THEIR fight: what they dealt, and what they
    /// took. Numbers for two bots trading shots across the arena would bury the
    /// one number the player is actually looking for. With nobody playing —
    /// AI v AI, which exists to be watched — every hit counts, tinted by who
    /// threw it; the board's per-victim merge keeps that to one number a robot.
    /// </summary>
    bool ShouldShow(out Color color)
    {
        color = DealtColor;
        var player = PlayerBrain.Local;
        bool playing = player != null && player.gameObject.activeInHierarchy;

        if (!playing)
        {
            // Tinted by the attacker, falling back to "whoever is not the
            // victim" when nobody owned the hit — a geyser, a mine, the floor.
            var attacker = _shield.LastAttacker;
            var source = attacker != null ? attacker.GetComponent<EnergyShield>() : null;
            color = MatchAnnouncer.TeamColor(source != null ? source.teamId : 1 - _shield.teamId);
            return true;
        }

        if (transform == player.transform)
        {
            color = TakenColor;
            return true;
        }
        return _shield.LastAttacker == player.transform;
    }
}

/// <summary>
/// Draws the numbers. One overlay canvas and a fixed pool of labels, shared by
/// every robot on the field.
///
/// SCREEN SPACE, NOT WORLD SPACE. The obvious build is a TextMesh parented to
/// the victim, and it fails at the thing this exists for: it shrinks with
/// distance, so the hits that most need confirming — the far ones — produce
/// the least readable numbers. These project onto a flat overlay and stay
/// legible across the arena, with only a gentle size falloff so depth still
/// reads.
///
/// NUMBERS MERGE PER VICTIM. A beam ticking twenty times a second would
/// otherwise stack twenty "-3"s into an unreadable pile. A second hit on the
/// same robot inside <see cref="MergeSeconds"/> adds to the number already
/// floating, so a burst reads as one number climbing — which is also the
/// number the player cares about.
/// </summary>
public class DamageNumberBoard : MonoBehaviour
{
    const float MergeSeconds = 0.4f;
    const float LifeSeconds = 0.95f;
    const float RiseMetres = 0.75f;

    /// <summary>Most numbers at once. Past this the oldest label is reused.</summary>
    const int PoolSize = 24;

    static DamageNumberBoard _instance;

    class Popup
    {
        public Text text;
        public Transform victim;
        public Vector3 world;
        public float amount;
        public float bornAt;
        public float mergeUntil;
        public Color color;
        public bool live;
    }

    readonly List<Popup> _pool = new List<Popup>();
    RectTransform _canvasRect;

    static DamageNumberBoard Ensure()
    {
        if (_instance == null)
            _instance = new GameObject("DamageNumberBoard").AddComponent<DamageNumberBoard>();
        return _instance;
    }

    void Awake()
    {
        _instance = this;
        BuildUi();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    public static void Report(Transform victim, Vector3 world, float amount, Color color)
    {
        Ensure().Push(victim, world, amount, color);
    }

    void Push(Transform victim, Vector3 world, float amount, Color color)
    {
        // Already a number climbing over this robot: add to it. This is what
        // turns a burst into "-18" instead of six separate "-3"s.
        foreach (var popup in _pool)
        {
            if (!popup.live || popup.victim != victim || Time.time > popup.mergeUntil)
                continue;
            popup.amount += amount;
            popup.mergeUntil = Time.time + MergeSeconds;
            popup.world = world;
            // Nudged back so a sustained beam's number does not fade mid-burst,
            // but never reset to newborn: a number pinned by continuous fire
            // would hang on screen for as long as the trigger was held.
            popup.bornAt = Mathf.Max(popup.bornAt, Time.time - LifeSeconds * 0.5f);
            return;
        }

        var slot = Free();
        slot.victim = victim;
        slot.world = world;
        slot.amount = amount;
        slot.bornAt = Time.time;
        slot.mergeUntil = Time.time + MergeSeconds;
        slot.color = color;
        slot.live = true;
    }

    /// <summary>A spare label, or the oldest one when the screen is already full.</summary>
    Popup Free()
    {
        Popup oldest = null;
        foreach (var popup in _pool)
        {
            if (!popup.live)
                return popup;
            if (oldest == null || popup.bornAt < oldest.bornAt)
                oldest = popup;
        }
        return oldest;
    }

    void LateUpdate()
    {
        // LateUpdate, so the projection uses where the camera ended this frame.
        // Done in Update the numbers lag the view by a frame and swim during a
        // turn — the same reason HeldWeaponMount reads its bone late.
        var cam = Camera.main;
        foreach (var popup in _pool)
        {
            if (!popup.live)
                continue;

            float age = Time.time - popup.bornAt;
            if (cam == null || age >= LifeSeconds)
            {
                popup.live = false;
                popup.text.enabled = false;
                continue;
            }

            // Under half a point rounds to "-0", which reads as a miss. Keep it
            // hidden and keep accumulating; it either grows into a real number
            // or times out unseen.
            int shown = Mathf.RoundToInt(popup.amount);
            if (shown <= 0)
            {
                popup.text.enabled = false;
                continue;
            }

            float t = age / LifeSeconds;
            Vector3 world = popup.world + Vector3.up * (RiseMetres * t);
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f)
            {
                popup.text.enabled = false;   // behind the camera
                continue;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screen, null, out Vector2 local);
            popup.text.rectTransform.anchoredPosition = local;

            // Bigger for a bigger hit, smaller with distance — enough that
            // depth reads, not so much that a far number becomes unreadable.
            float distance = Vector3.Distance(cam.transform.position, world);
            float falloff = Mathf.Lerp(1f, 0.62f, Mathf.InverseLerp(6f, 45f, distance));
            float punch = Mathf.Lerp(1f, 1.5f, Mathf.InverseLerp(5f, 40f, popup.amount));
            popup.text.fontSize = Mathf.RoundToInt(34f * falloff * punch);

            popup.text.text = "-" + shown;
            // Full strength, then gone in the last third: a number that starts
            // fading immediately reads as already over.
            var color = popup.color;
            color.a = 1f - Mathf.InverseLerp(0.66f, 1f, t);
            popup.text.color = color;
            popup.text.enabled = true;
        }
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("DamageNumberCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 39;   // under the announcer toast and the round bar
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _canvasRect = canvasGo.GetComponent<RectTransform>();

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Damage" + i);
            go.transform.SetParent(canvasGo.transform, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = 34;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(220, 64);
            text.enabled = false;

            // The arena is bright and the numbers are thin — without an outline
            // a pale one over a muzzle flash is invisible at the moment it
            // matters most.
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            _pool.Add(new Popup { text = text });
        }
    }
}
