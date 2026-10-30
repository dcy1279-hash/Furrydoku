using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// First-run rules tutorial on a fixed 4×4 board (same teaching chain as the reference,
// own layout): place the lone green → tap-cross its row/column → last yellow →
// swipe-cross the yellow's neighbours → last pink → find the last puppy alone.
// Presentation + input gating only; GameController still owns BoardState and saving.
public sealed class TutorialGuide : MonoBehaviour
{
    public const string TutorialId = "tutorial";
    private const string DoneKey = "PetPuzzle.TutorialDone";
    // 0 Pink, 1 Purple, 2 Green, 3 Yellow (FirstLevelFactory palette order).
    private static readonly int[] Regions =
    {
        0, 0, 0, 0,
        3, 0, 1, 0,
        3, 3, 1, 2,
        1, 1, 1, 1
    };
    private static readonly int[] Solution = { 2, 4, 11, 13 };
    private const int Green = 11, Yellow = 4, Pink = 2, Purple = 13;
    private static readonly int[] CrossTargets = { 8, 9, 10, 3, 7, 15 };
    private static readonly int[] SwipePath = { 0, 1, 5 };
    private static readonly int[] YellowCells = { 4, 8, 9 };
    private static readonly int[] PinkCells = { 0, 1, 2, 3, 5, 7 };

    private static readonly Color Dim = new Color(.11f, .09f, .08f, .74f);
    private static readonly Color Card = new Color(1f, .98f, .95f), CardEdge = new Color(.918f, .859f, .776f);
    private static readonly Color Brown = new Color(.47f, .31f, .235f);
    private static readonly Color Orange = new Color(1f, .604f, .122f), OrangeShade = new Color(.83f, .36f, 0f);

    private enum Step { PlaceGreen, RuleColor, CrossLines, PlaceYellow, SwipeNeighbours, PlacePink, FindLast, Done }
    private enum HandMode { Hidden, DoubleTap, Tap, Swipe }

    public static bool Completed => PlayerPrefs.GetInt(DoneKey, 0) == 1;
    public static void MarkCompleted() { PlayerPrefs.SetInt(DoneKey, 1); PlayerPrefs.Save(); }
    public static bool IsTutorialLevel(LevelData level) => level != null && level.Id == TutorialId;

    public static LevelData CreateLevel()
    {
        LevelData level = ScriptableObject.CreateInstance<LevelData>();
        level.name = "Tutorial_Runtime";
        level.hideFlags = HideFlags.DontSave;
        level.InitializeRuntime(TutorialId, 4, Regions, Solution);
        return level;
    }

    private RectTransform root, card, cardFace, hand, ring, button, hint;
    private TextMeshProUGUI message, buttonLabel;
    private BoardSpotlight spotlight;
    private CellView[] views;
    private Action finished;
    private Step step;
    private HandMode handMode;
    private int handTarget = -1;
    private float handClock, pendingAt = -1f;
    private Step pendingStep;
    private BoardState board;
    private Rect boardRect => spotlight.BoardRect;
    private float cellSide => spotlight.CellSide;

    public static TutorialGuide Create(RectTransform boardRoot, CellView[] cells, TMP_FontAsset font, Action onFinished)
    {
        Canvas canvas = boardRoot != null ? boardRoot.GetComponentInParent<Canvas>() : null;
        if (canvas == null || cells == null || cells.Length != 16) return null;
        RectTransform root = PropBarView.MakeRect("TutorialGuide", canvas.rootCanvas.transform);
        PropBarView.Stretch(root);
        root.SetAsLastSibling();
        var guide = root.gameObject.AddComponent<TutorialGuide>();
        guide.root = root; guide.views = cells; guide.finished = onFinished;
        guide.Build(GameUiStyle.RuntimeFont("ArialRoundedBold", font));
        guide.Enter(Step.PlaceGreen);
        return guide;
    }

    private void Build(TMP_FontAsset font)
    {
        spotlight = BoardSpotlight.Create(root);
        spotlight.Bind(views);

        card = GameUiStyle.Panel("TutorialCardEdge", root, CardEdge, .22f);
        cardFace = GameUiStyle.Panel("TutorialCard", card, Card, .2f);
        cardFace.anchorMin = Vector2.zero; cardFace.anchorMax = Vector2.one;
        cardFace.offsetMin = new Vector2(5f, 5f); cardFace.offsetMax = new Vector2(-5f, -5f);
        message = Text("Message", cardFace, font, 40f);
        message.rectTransform.anchorMin = Vector2.zero; message.rectTransform.anchorMax = Vector2.one;
        message.rectTransform.offsetMin = new Vector2(28f, 14f); message.rectTransform.offsetMax = new Vector2(-28f, -14f);

        button = PillButton("TutorialButton", font, Orange, OrangeShade, Color.white, out buttonLabel, OnButton);
        hint = PillButton("TutorialHint", font, Card, CardEdge, Brown, out TextMeshProUGUI hintLabel, ShowHint);
        hintLabel.text = "Need a hint? Tap here";

        ring = PropBarView.MakeRect("TapRing", root);
        var ringGraphic = ring.gameObject.AddComponent<GameUiGraphic>();
        ringGraphic.Shape = GameUiGraphic.Kind.Ring; ringGraphic.color = new Color(1f, 1f, 1f, 0f);
        ringGraphic.raycastTarget = false;
        hand = PropBarView.MakeRect("TutorialHand", root);
        var handImage = hand.gameObject.AddComponent<RawImage>();
        handImage.texture = Resources.Load<Texture2D>("UiIcons/tutorial_hand");
        handImage.raycastTarget = false;
        hand.pivot = new Vector2(.17f, .91f); // fingertip of the generated glove (512² square)
    }

    private static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size)
    {
        RectTransform r = PropBarView.MakeRect(name, parent);
        var text = r.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.color = Brown; text.richText = true;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private RectTransform PillButton(string name, TMP_FontAsset font, Color face, Color shade, Color ink,
        out TextMeshProUGUI label, Action click)
    {
        RectTransform r = PropBarView.MakeRect(name, root);
        r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
        RectTransform shadeRect = GameUiStyle.Panel("Shade", r, shade, .5f);
        shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one;
        shadeRect.offsetMin = Vector2.zero; shadeRect.offsetMax = new Vector2(0f, -8f);
        RectTransform faceRect = GameUiStyle.Panel("Face", r, face, .5f);
        faceRect.anchorMin = Vector2.zero; faceRect.anchorMax = Vector2.one;
        faceRect.offsetMin = new Vector2(0f, 8f); faceRect.offsetMax = Vector2.zero;
        var hit = faceRect.GetComponent<GameUiGraphic>(); hit.raycastTarget = true;
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = hit; b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => click());
        label = Text("Label", faceRect, font, 42f);
        label.color = ink;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        r.gameObject.SetActive(false);
        return r;
    }

    // ---- gating (asked by GameController before it changes the board) ----

    public bool AllowTap(int index, CellState state)
    {
        if (step == Step.FindLast) return true;
        if (state != CellState.Empty) return false;
        if (step == Step.CrossLines) return Array.IndexOf(CrossTargets, index) >= 0;
        if (step == Step.SwipeNeighbours) return Array.IndexOf(SwipePath, index) >= 0;
        return false;
    }

    public bool AllowDrag(int index) =>
        step == Step.FindLast || (step == Step.SwipeNeighbours && Array.IndexOf(SwipePath, index) >= 0);

    public bool AllowPlace(int index) =>
        (step == Step.PlaceGreen && index == Green) || (step == Step.PlaceYellow && index == Yellow) ||
        (step == Step.PlacePink && index == Pink) || (step == Step.FindLast && index == Purple);

    // A wrong double-tap in the tutorial costs nothing; just point at the answer.
    public void OnRejectedPlace(int index) { if (step == Step.FindLast) ShowHint(); }

    public void OnBoardChanged(BoardState state)
    {
        board = state;
        if (state == null || pendingAt >= 0f) return;
        if (step == Step.PlaceGreen && Placed(Green)) Queue(Step.RuleColor, .7f);
        else if (step == Step.CrossLines && AllMarked(CrossTargets)) Queue(Step.PlaceYellow, .45f);
        else if (step == Step.PlaceYellow && Placed(Yellow)) Queue(Step.SwipeNeighbours, .7f);
        else if (step == Step.SwipeNeighbours && AllMarked(SwipePath)) Queue(Step.PlacePink, .45f);
        else if (step == Step.PlacePink && Placed(Pink)) Queue(Step.FindLast, .7f);
        else if (step == Step.FindLast && state.IsWon) Queue(Step.Done, 1.1f);
        else if (step == Step.CrossLines || step == Step.SwipeNeighbours) handClock = 0f; // next target
    }

    private bool Placed(int index) => board != null && board.GetCellState(index) == CellState.Placed;
    private bool AllMarked(int[] cells)
    {
        if (board == null) return false;
        foreach (int i in cells) if (board.GetCellState(i) != CellState.Marked) return false;
        return true;
    }
    private void Queue(Step next, float delay) { pendingStep = next; pendingAt = Time.unscaledTime + delay; SetHand(HandMode.Hidden, -1); }

    private void Enter(Step next)
    {
        step = next; pendingAt = -1f;
        button.gameObject.SetActive(false); hint.gameObject.SetActive(false);
        switch (next)
        {
            case Step.PlaceGreen:
                Say("Double-tap to place a puppy on this cell.");
                Spot(Green); SetHand(HandMode.DoubleTap, Green); break;
            case Step.RuleColor:
                Say("Well done!\nOnly one puppy per color.");
                SpotAll(); SetHand(HandMode.Hidden, -1); ShowButton("Got it!"); break;
            case Step.CrossLines:
                Say("Puppies can't share a row or column.\nTap the empty cells to exclude them.");
                Spot(Green, 8, 9, 10, 3, 7, 15); SetHand(HandMode.Tap, -1); break;
            case Step.PlaceYellow:
                Say("Only the last <color=#D99A12>Yellow</color> cell remains.\nDouble-tap to place a puppy.");
                Spot(YellowCells); SetHand(HandMode.DoubleTap, Yellow); break;
            case Step.SwipeNeighbours:
                Say("Puppies can't touch, not even diagonally.\nSwipe across these cells to exclude them.");
                Spot(Yellow, 0, 1, 5); SetHand(HandMode.Swipe, -1); break;
            case Step.PlacePink:
                Say("Only the last <color=#E2686F>Pink</color> cell remains.\nDouble-tap to place a puppy.");
                Spot(PinkCells); SetHand(HandMode.DoubleTap, Pink); break;
            case Step.FindLast:
                Say("Find the last puppy!");
                SpotAll(); SetHand(HandMode.Hidden, -1); hint.gameObject.SetActive(true); break;
            case Step.Done:
                Say("Excellent!\nYou've mastered the rules!");
                SpotAll(); SetHand(HandMode.Hidden, -1); ShowButton("Start Game"); break;
        }
    }

    private void OnButton()
    {
        if (step == Step.RuleColor) Enter(Step.CrossLines);
        else if (step == Step.Done) { button.gameObject.SetActive(false); finished?.Invoke(); }
    }

    private void ShowHint()
    {
        if (step != Step.FindLast) return;
        Say("Look at the purple cells: only one isn't in a used row,\ncolumn or next to a puppy. Double-tap it!");
        SetHand(HandMode.DoubleTap, Purple);
    }

    private void Say(string text) { message.text = text; }
    private void ShowButton(string text) { buttonLabel.text = text; button.gameObject.SetActive(true); }
    private void SpotAll() { spotlight.SpotOnly(System.Linq.Enumerable.Range(0, 16)); }
    private void Spot(params int[] cells) { spotlight.SpotOnly(cells); }
    private void SetHand(HandMode mode, int target) { handMode = mode; handTarget = target; handClock = 0f; }

    // ---- per-frame layout and animation ----

    private void LateUpdate()
    {
        if (pendingAt >= 0f && Time.unscaledTime >= pendingAt) Enter(pendingStep);
        if (!spotlight.Measure()) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        spotlight.SetDimmed(step != Step.RuleColor && step != Step.FindLast && step != Step.Done);
        // Card above the board, buttons below it.
        float width = Mathf.Max(boardRect.width, 640f);
        card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
        card.sizeDelta = new Vector2(width, 190f);
        card.anchoredPosition = new Vector2(boardRect.center.x, boardRect.yMax + 60f + 95f);
        Place(button, new Vector2(boardRect.center.x, boardRect.yMin - 120f), new Vector2(380f, 120f));
        Place(hint, new Vector2(boardRect.center.x, boardRect.yMin - 120f), new Vector2(560f, 110f));
        AnimateHand(dt);
    }

    private static void Place(RectTransform r, Vector2 position, Vector2 size)
    { r.anchoredPosition = position; r.sizeDelta = size; }

    private void AnimateHand(float dt)
    {
        var image = hand.GetComponent<RawImage>();
        var ringGraphic = ring.GetComponent<GameUiGraphic>();
        hand.sizeDelta = Vector2.one * cellSide * 1.05f;
        ring.sizeDelta = Vector2.one * cellSide * .7f;
        float alpha = 0f, press = 0f, ringAlpha = 0f, ringScale = 1f;
        Vector2 tip = Vector2.zero;
        handClock += dt;
        int target = handTarget;
        if (handMode == HandMode.Tap)
        {
            target = -1;
            int[] list = step == Step.SwipeNeighbours ? SwipePath : CrossTargets;
            foreach (int i in list) if (board == null || board.GetCellState(i) == CellState.Empty) { target = i; break; }
        }
        if ((handMode == HandMode.DoubleTap || handMode == HandMode.Tap) && target >= 0)
        {
            float cycle = handMode == HandMode.DoubleTap ? 1.7f : 1.15f, t = handClock % cycle;
            tip = spotlight.CellRect(target).center;
            float approach = Smooth(t / .3f);
            tip += new Vector2(1f, -1f) * cellSide * .45f * (1f - approach);
            alpha = approach * (1f - Smooth((t - (cycle - .2f)) / .2f));
            float[] taps = handMode == HandMode.DoubleTap ? new[] { .4f, .62f } : new[] { .45f };
            foreach (float at in taps)
            {
                float k = (t - at) / .16f;
                if (k > 0f && k < 1f) press = Mathf.Max(press, Mathf.Sin(Mathf.PI * k));
                float rk = (t - at) / .45f;
                if (rk > 0f && rk < 1f) { ringAlpha = Mathf.Max(ringAlpha, (1f - rk) * .85f); ringScale = .55f + .7f * rk; }
            }
        }
        else if (handMode == HandMode.Swipe)
        {
            const float cycle = 2.1f; float t = handClock % cycle;
            float move = Smooth((t - .35f) / 1.0f);
            float pos = move * (SwipePath.Length - 1);
            int seg = Mathf.Min(SwipePath.Length - 2, Mathf.FloorToInt(pos));
            tip = Vector2.Lerp(spotlight.CellRect(SwipePath[seg]).center, spotlight.CellRect(SwipePath[seg + 1]).center, pos - seg);
            alpha = Smooth(t / .25f) * (1f - Smooth((t - 1.65f) / .3f));
            press = t > .3f && t < 1.45f ? 1f : Smooth(t / .3f) * (t <= .3f ? 1f : 0f);
        }
        hand.anchorMin = hand.anchorMax = new Vector2(.5f, .5f);
        hand.anchoredPosition = tip + new Vector2(0f, -press * cellSide * .04f);
        hand.localScale = Vector3.one * (1f - .12f * press);
        image.color = new Color(1f, 1f, 1f, alpha);
        ring.anchorMin = ring.anchorMax = new Vector2(.5f, .5f);
        ring.anchoredPosition = tip; ring.localScale = Vector3.one * ringScale;
        ringGraphic.color = new Color(1f, 1f, 1f, ringAlpha * alpha);
        hand.SetAsLastSibling();
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
