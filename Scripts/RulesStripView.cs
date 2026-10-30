using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// A short, click-through rule tooltip. No gameplay pause or answer information.
public sealed class RulesStripView : MonoBehaviour
{
    public const float HeightRatio = .19f;
    private readonly RectTransform[] cards = new RectTransform[3];
    private readonly RectTransform[] diagrams = new RectTransform[3];
    private readonly RectTransform[] first = new RectTransform[3], second = new RectTransform[3];
    // English two-line captions in the same rounded face as LEVEL / SCORE.
    private readonly string[] firstText = { "One pet", "One pet per", "Pets can't" };
    private readonly string[] secondText = { "per color", "row & column", "touch" };
    private float lastWidth = -1f;
    private RectTransform board, panel, reservedSpace, launcherAnchor, tail;
    private CanvasGroup group;
    private readonly Vector3[] corners = new Vector3[4];
    private Vector2 restPosition;
    private float progress, autoHideAt = -1f, showAt = -1f;
    private bool expanded, autoHideRequested, dismissLauncherClick;
    private int openedFrame = -1;

    public static RulesStripView Create(RectTransform parent, TMP_FontAsset font)
    {
        RectTransform root = PropBarView.MakeRect("RulesHelpOverlay", parent);
        PropBarView.Stretch(root);
        var view = root.gameObject.AddComponent<RulesStripView>();
        view.panel = GameUiStyle.Panel("AnimatedRules", root, new Color(1f, .99f, .97f), .15f);
        view.panel.pivot = new Vector2(.5f, 1f);
        view.group = view.panel.gameObject.AddComponent<CanvasGroup>();
        view.group.interactable = false;
        view.group.blocksRaycasts = false;
        view.tail = PropBarView.MakeRect("PointerToRulesButton", view.panel);
        view.tail.anchorMin = view.tail.anchorMax = new Vector2(.5f, 1f);
        view.tail.pivot = new Vector2(.5f, 0f);
        var arrow = view.tail.gameObject.AddComponent<GameUiGraphic>();
        arrow.Shape = GameUiGraphic.Kind.TooltipTail;
        arrow.color = new Color(1f, .99f, .97f);
        arrow.raycastTarget = false;
        for (int i = 0; i < 3; i++)
        {
            view.cards[i] = GameUiStyle.Panel("Rule_" + i, view.panel, new Color(.98f, .95f, .91f), .14f);
            view.diagrams[i] = PropBarView.MakeRect("Diagram", view.cards[i]);
            var picture = view.diagrams[i].gameObject.AddComponent<GameUiGraphic>();
            picture.Shape = (GameUiGraphic.Kind)((int)GameUiGraphic.Kind.RuleColor + i);
            picture.raycastTarget = false;
            TMP_FontAsset rounded = GameUiStyle.RuntimeFont("ArialRoundedBold", font);
            view.first[i] = RuleText("RuleTop", view.cards[i], rounded, view.firstText[i]);
            view.second[i] = RuleText("RuleBottom", view.cards[i], rounded, view.secondText[i]);
        }
        view.Hide(true);
        return view;
    }

    public void BindBoard(RectTransform value)
    {
        board = value;
        lastWidth = -1f;
        RefreshLayout();
    }

    public void BindHud(HudView hud)
    {
        Hide(true);
        reservedSpace = hud != null ? hud.RulesPanelAnchor : null;
        launcherAnchor = hud != null ? hud.RulesAnchor : null;
        lastWidth = -1f;
        RefreshLayout();
    }

    // Fired when the scheduled opening (not a ! toggle) actually shows the strip.
    public event System.Action AutoOpened;

    // First ten levels: the strip stays up until the player taps "!" (no 3 s timer, board touches
    // and app switches leave it alone). closedByPlayer keeps it shut after settings closes.
    private bool persistent, closedByPlayer;
    public void SetPersistent(bool value)
    {
        persistent = value;
        if (value) { autoHideRequested = false; autoHideAt = -1f; }
    }
    // After the settings card closes, bring a persistent strip back unless the player closed it.
    public void RestorePersistent()
    {
        if (persistent && !closedByPlayer && !expanded) Show();
    }

    public void ScheduleOpening()
    {
        closedByPlayer = false;
        Hide(true);
        if (reservedSpace != null && isActiveAndEnabled) showAt = Time.unscaledTime + .5f;
    }

    public void Show(bool autoHide = true)
    {
        if (panel == null || board == null || reservedSpace == null || !isActiveAndEnabled) return;
        showAt = -1f;
        expanded = true;
        autoHideRequested = autoHide && !persistent;
        autoHideAt = -1f; // Count 3 seconds after the opening animation is fully visible.
        openedFrame = Time.frameCount;
        panel.gameObject.SetActive(true);
        RefreshLayout();
        ApplyVisual();
    }

    public void Toggle()
    {
        // A press on ! while already visible dismisses it. Its following Click
        // must not reopen it after the global pointer-down dismissal.
        if (dismissLauncherClick) { dismissLauncherClick = false; closedByPlayer = true; Hide(); return; }
        if (expanded) { closedByPlayer = true; Hide(); }
        else { closedByPlayer = false; Show(); }
    }

    // Board touches dismiss a visible strip but keep a pending automatic opening.
    public void Dismiss() { if (!persistent && (expanded || progress > 0f)) Hide(); }

    public void Hide(bool immediately = false)
    {
        expanded = false;
        showAt = -1f;
        autoHideRequested = false;
        autoHideAt = -1f;
        if (!immediately) return;
        dismissLauncherClick = false;
        openedFrame = -1;
        progress = 0f;
        if (group != null) group.alpha = 0f;
        if (panel != null) { panel.localScale = Vector3.one; panel.gameObject.SetActive(false); }
    }

    private void Update()
    {
        if (panel == null) return;
        if (TryPointerDown(out Vector2 point) && openedFrame != Time.frameCount)
        {
            bool overLauncher = false;
            if (launcherAnchor != null)
            {
                Canvas canvas = launcherAnchor.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera : null;
                overLauncher = RectTransformUtility.RectangleContainsScreenPoint(launcherAnchor, point, camera);
            }
            dismissLauncherClick = overLauncher && (expanded || progress > 0f);
            // Only a visible strip is dismissed by a tap; a quick first tap on a new level must
            // not cancel its pending automatic opening (every level shows the rules for 3 s).
            // In the first ten levels only a press on "!" itself closes it.
            if ((expanded || progress > 0f) && (!persistent || overLauncher)) Hide();
        }
        if (showAt >= 0f && Time.unscaledTime >= showAt)
        {
            Show();
            if (expanded) AutoOpened?.Invoke();
        }
        if (expanded && autoHideAt >= 0f && Time.unscaledTime >= autoHideAt) Hide();
        if (expanded && progress >= 1f && autoHideRequested && autoHideAt < 0f)
            autoHideAt = Time.unscaledTime + 3f;
        float target = expanded ? 1f : 0f;
        if (Mathf.Approximately(progress, target)) return;
        progress = Mathf.MoveTowards(progress, target,
            Mathf.Min(Time.unscaledDeltaTime, .05f) / (expanded ? .22f : .16f));
        ApplyVisual();
        if (!expanded && progress <= 0f) panel.gameObject.SetActive(false);
    }

    private void ApplyVisual()
    {
        if (panel == null || group == null) return;
        float eased = progress * progress * (3f - 2f * progress);
        group.alpha = eased;
        panel.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, eased);
        panel.anchoredPosition = restPosition + Vector2.up * panel.rect.width * .018f * (1f - eased);
    }

    private void LateUpdate() => RefreshLayout();

    private void RefreshLayout()
    {
        if (board == null || panel == null || reservedSpace == null) return;
        var root = (RectTransform)transform;
        reservedSpace.GetWorldCorners(corners);
        Vector2 min = root.InverseTransformPoint(corners[0]), max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 local = root.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, local); max = Vector2.Max(max, local);
        }
        float width = Mathf.Min(max.x - min.x, root.rect.width * .96f);
        if (width <= 0f) return;
        // Fixed slot between title/score and bones; opening never reflows the board.
        // All graphics remain click-through; global dismissal never consumes input.
        panel.anchorMin = panel.anchorMax = root.pivot;
        panel.sizeDelta = new Vector2(width, max.y - min.y);
        restPosition = new Vector2((min.x + max.x) * .5f, max.y);
        if (tail != null && launcherAnchor != null)
        {
            launcherAnchor.GetWorldCorners(corners);
            Vector2 bottomLeft = root.InverseTransformPoint(corners[0]);
            Vector2 bottomRight = root.InverseTransformPoint(corners[3]);
            float tipX = (bottomLeft.x + bottomRight.x) * .5f - restPosition.x;
            float height = Mathf.Max(width * .012f, bottomLeft.y - restPosition.y - width * .004f);
            tail.sizeDelta = new Vector2(width * .045f, height + width * .002f);
            tail.anchoredPosition = new Vector2(Mathf.Clamp(tipX, -width * .45f, width * .45f), -width * .002f);
        }
        ApplyVisual();
        if (Mathf.Approximately(width, lastWidth)) return;
        lastWidth = width;
        float cardWidth = width * .316f;
        for (int i = 0; i < 3; i++)
        {
            GameUiStyle.Place(cards[i], (i - 1) * width * .326f, width * .095f, cardWidth, width * .15f);
            GameUiStyle.Place(diagrams[i], -cardWidth * .31f, width * .075f, width * .098f, width * .098f);
            GameUiStyle.Place(first[i], cardWidth * .18f, width * .103f, cardWidth * .60f, width * .043f);
            GameUiStyle.Place(second[i], cardWidth * .18f, width * .050f, cardWidth * .60f, width * .043f);
            GameUiStyle.LabelSize(first[i], firstText[i], width * .025f);
            GameUiStyle.LabelSize(second[i], secondText[i], width * .025f);
        }
    }

    private static RectTransform RuleText(string name, Transform parent, TMP_FontAsset face, string text)
    {
        TextMeshProUGUI t = PropBarView.MakeText(name, parent, face);
        t.text = text; t.color = GameUiStyle.Ink;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
        return t.rectTransform;
    }

    private static bool TryPointerDown(out Vector2 position)
    {
        position = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        if (Touchscreen.current != null)
            foreach (var touch in Touchscreen.current.touches)
                if (touch.press.wasPressedThisFrame) { position = touch.position.ReadValue(); return true; }
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        { position = Mouse.current.position.ReadValue(); return true; }
#elif ENABLE_LEGACY_INPUT_MANAGER
        for (int i = 0; i < Input.touchCount; i++)
            if (Input.GetTouch(i).phase == TouchPhase.Began)
            { position = Input.GetTouch(i).position; return true; }
        if (Input.GetMouseButtonDown(0)) { position = Input.mousePosition; return true; }
#endif
        return false;
    }

    private void OnDisable() => Hide(true);
    private void OnApplicationFocus(bool value) { if (!value && !persistent) Hide(true); }
    private void OnApplicationPause(bool value) { if (value && !persistent) Hide(true); }
}
