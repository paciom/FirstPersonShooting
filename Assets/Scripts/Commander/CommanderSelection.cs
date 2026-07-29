using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The player's hands: click and box selection, control groups, and the
/// right-click order language every RTS speaks — move on ground, attack on an
/// enemy, A-then-click for attack-move.
///
/// Lives on the Commander object, so a session's selection dies with the
/// session. Only team 0 (cyan) takes orders; this component IS the cyan
/// commander, and Phase 5's CommanderAI is the magenta one.
/// </summary>
public class CommanderSelection : MonoBehaviour
{
    public static CommanderSelection Instance { get; private set; }

    /// <summary>
    /// True while A means "attack-move" rather than "pan west" — i.e. while
    /// the commander has an army selected. CommanderCamera checks this to
    /// drop the A key from its pan axis; the Left arrow still pans.
    /// </summary>
    public static bool AttackKeyReserved =>
        Instance != null && Instance._selected.Count > 0;

    const int PlayerTeam = 0;
    /// <summary>Below this many pixels of travel, a drag is just a click.</summary>
    const float ClickSlop = 8f;
    const float FormationSpacing = 1.8f;

    // Not readonly — survives a recompile-during-Play reload as a serialized
    // field, so selection rings don't outlive a wiped list. The groups
    // dictionary can't be serialized either way; losing groups to a mid-play
    // recompile is acceptable, stale rings are not.
    List<CommanderUnit> _selected = new List<CommanderUnit>();
    readonly Dictionary<int, List<CommanderUnit>> _groups = new Dictionary<int, List<CommanderUnit>>();

    Camera _camera;
    bool _dragging;
    Vector2 _dragStart;
    bool _attackMoveArmed;

    // Drag rectangle UI. Constant-pixel canvas on purpose: the rect is drawn
    // from Input.mousePosition values, and a scaled canvas would need every
    // coordinate converted.
    Canvas _canvas;
    RectTransform _rect;

    /// <summary>
    /// Disarm a pending attack-move, reporting whether there was one. Called
    /// by GameModeController's Escape handling — exactly one component reads
    /// that key, so "cancel the order" can never race "leave the mode".
    /// </summary>
    public bool CancelPendingOrder()
    {
        if (!_attackMoveArmed)
            return false;
        _attackMoveArmed = false;
        return true;
    }

    void Awake()
    {
        Instance = this;
        var canvasGo = new GameObject("SelectionCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 12;

        var rectGo = new GameObject("DragRect");
        rectGo.transform.SetParent(canvasGo.transform, false);
        var image = rectGo.AddComponent<Image>();
        image.color = new Color(0.2f, 0.9f, 1f, 0.13f);
        image.raycastTarget = false;
        _rect = image.rectTransform;
        _rect.anchorMin = _rect.anchorMax = Vector2.zero;
        _rect.pivot = Vector2.zero;
        _rect.gameObject.SetActive(false);
    }

    void Update()
    {
        // Statics don't survive a recompile during Play; the instance does.
        Instance = this;

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null)
                return;
        }

        Prune(_selected);

        // While the cursor is on a UI control the world should not also hear
        // the click. Two checks because the touch MENU button is NOT a uGUI
        // Button — TouchControls hit-tests its rects by hand, so the
        // EventSystem alone would wave every tap on MENU straight through
        // into a selection click. The EventSystem half covers real raycast
        // canvases (the Phase 3+ build bar).
        bool onUi = TouchControls.PointOver(Input.mousePosition)
            || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

        if (Input.GetKeyDown(KeyCode.A) && _selected.Count > 0)
            _attackMoveArmed = true;
        // Escape is GameModeController's key. It asks us first (see
        // CancelPendingOrder) — reading it here too would race the mode exit.

        HandleGroups();

        if (Input.GetMouseButtonDown(0) && !onUi)
        {
            _dragStart = Input.mousePosition;
            _dragging = true;
        }

        if (_dragging && Input.GetMouseButton(0))
            UpdateDragRect();

        if (_dragging && Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            _rect.gameObject.SetActive(false);

            Vector2 end = Input.mousePosition;
            bool isClick = (end - _dragStart).magnitude < ClickSlop;
            if (isClick && _attackMoveArmed)
            {
                _attackMoveArmed = false;
                // A-click on an enemy is a proper attack order, not an
                // attack-move to the ground under them.
                var target = UnitUnder(end);
                if (target != null && target.TeamId != PlayerTeam && target.IsAlive)
                    foreach (var unit in _selected)
                        unit.IssueAttack(target);
                else
                    OrderAttackMove(end);
            }
            else if (isClick)
            {
                ClickSelect(end, additive: Shift());
            }
            else
            {
                // A drag does what its rectangle promised — box select — and
                // consumes the armed order, per RTS convention that any
                // completed left action ends targeting mode.
                _attackMoveArmed = false;
                BoxSelect(_dragStart, end, additive: Shift());
            }
        }

        if (Input.GetMouseButtonDown(1) && !onUi)
        {
            _attackMoveArmed = false;
            RightClickOrder(Input.mousePosition);
        }
    }

    static bool Shift() =>
        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    // ------------------------------------------------------------- selection

    void ClickSelect(Vector2 screenPoint, bool additive)
    {
        var unit = UnitUnder(screenPoint);
        if (!additive)
            ClearSelection();

        if (unit == null || unit.TeamId != PlayerTeam)
            return;

        // Shift-clicking a selected unit deselects it — standard RTS grammar.
        if (additive && _selected.Contains(unit))
        {
            unit.SetSelected(false);
            _selected.Remove(unit);
            return;
        }
        Select(unit);
    }

    void BoxSelect(Vector2 a, Vector2 b, bool additive)
    {
        if (!additive)
            ClearSelection();

        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || unit.TeamId != PlayerTeam || !unit.IsAlive)
                continue;
            Vector3 screen = _camera.WorldToScreenPoint(unit.transform.position);
            if (screen.z < 0f)
                continue;   // behind the camera
            if (screen.x >= min.x && screen.x <= max.x && screen.y >= min.y && screen.y <= max.y)
                Select(unit);
        }
    }

    void Select(CommanderUnit unit)
    {
        if (_selected.Contains(unit))
            return;
        _selected.Add(unit);
        unit.SetSelected(true);
    }

    void ClearSelection()
    {
        foreach (var unit in _selected)
            if (unit != null)
                unit.SetSelected(false);
        _selected.Clear();
    }

    void UpdateDragRect()
    {
        Vector2 now = Input.mousePosition;
        if ((now - _dragStart).magnitude < ClickSlop)
        {
            _rect.gameObject.SetActive(false);
            return;
        }
        _rect.gameObject.SetActive(true);
        var min = Vector2.Min(_dragStart, now);
        var max = Vector2.Max(_dragStart, now);
        _rect.anchoredPosition = min;
        _rect.sizeDelta = max - min;
    }

    // ------------------------------------------------------------- orders

    void RightClickOrder(Vector2 screenPoint)
    {
        if (_selected.Count == 0)
            return;

        var target = UnitUnder(screenPoint);
        if (target != null && target.TeamId != PlayerTeam && target.IsAlive)
        {
            foreach (var unit in _selected)
                unit.IssueAttack(target);
            return;
        }

        if (GroundPoint(screenPoint, out Vector3 ground))
        {
            var spots = Formation(ground, _selected.Count);
            for (int i = 0; i < _selected.Count; i++)
                _selected[i].IssueMove(spots[i]);
        }
    }

    void OrderAttackMove(Vector2 screenPoint)
    {
        if (_selected.Count == 0 || !GroundPoint(screenPoint, out Vector3 ground))
            return;
        var spots = Formation(ground, _selected.Count);
        for (int i = 0; i < _selected.Count; i++)
            _selected[i].IssueAttackMove(spots[i]);
    }

    /// <summary>
    /// A centred grid around the clicked point. Cheap and readable — proper
    /// position assignment (nearest unit to nearest slot) can come with the
    /// bigger armies that would make it visible.
    /// </summary>
    static Vector3[] Formation(Vector3 center, int count)
    {
        var spots = new Vector3[count];
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt(count / (float)cols);
        var origin = center - new Vector3((cols - 1) * 0.5f * FormationSpacing, 0f,
                                          (rows - 1) * 0.5f * FormationSpacing);
        for (int i = 0; i < count; i++)
            spots[i] = origin + new Vector3(i % cols * FormationSpacing, 0f,
                                            i / cols * FormationSpacing);
        return spots;
    }

    // ------------------------------------------------------------- groups

    void HandleGroups()
    {
        bool assign = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        for (int digit = 1; digit <= 9; digit++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha0 + digit))
                continue;

            if (assign)
            {
                _groups[digit] = new List<CommanderUnit>(_selected);
            }
            else if (_groups.TryGetValue(digit, out var group))
            {
                Prune(group);
                if (group.Count == 0)
                    continue;
                ClearSelection();
                foreach (var unit in group)
                    Select(unit);
            }
        }
    }

    static void Prune(List<CommanderUnit> units)
    {
        units.RemoveAll(u => u == null || !u.IsAlive);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------- picking

    CommanderUnit UnitUnder(Vector2 screenPoint)
    {
        var ray = _camera.ScreenPointToRay(screenPoint);
        if (Physics.Raycast(ray, out RaycastHit hit, 600f))
            return hit.collider.GetComponentInParent<CommanderUnit>();
        return null;
    }

    bool GroundPoint(Vector2 screenPoint, out Vector3 point)
    {
        var ray = _camera.ScreenPointToRay(screenPoint);
        if (Physics.Raycast(ray, out RaycastHit hit, 600f))
        {
            point = hit.point;
            return true;
        }
        point = default;
        return false;
    }
}
