using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Reference-style hint overlay: the screen dims, only the reason cells and the result cells
// stay lit (cells to cross show a pulsing white preview X), one short sentence sits on a card
// above the board and a big orange button below applies it. Tapping the dim closes it.
// Presentation only: GameController applies the crosses; hints never place puppies.
public class HintPanelView : MonoBehaviour
{
    private static readonly Color Card = new Color(1f, .98f, .95f), CardEdge = new Color(.918f, .859f, .776f);
    private static readonly Color Brown = new Color(.47f, .31f, .235f);
    private static readonly Color Orange = new Color(1f, .604f, .122f), OrangeShade = new Color(.83f, .36f, 0f);

    private RectTransform root, card, button, marker;
    private TextMeshProUGUI message, buttonLabel;
    private BoardSpotlight spotlight;
    private readonly List<CrossStrokeGraphic> previews = new List<CrossStrokeGraphic>();
    private readonly List<int> previewCells = new List<int>();
    private int placeCell = -1;
    private HintStep step;
    [NonSerialized] private Action onApply, onClose;
    private bool busy;

    public static HintPanelView Create(PropBarView bar)
    {
        Canvas canvas = bar != null ? bar.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return null;
        RectTransform root = PropBarView.MakeRect("HintOverlay", canvas.rootCanvas.transform);
        PropBarView.Stretch(root);
        var panel = root.gameObject.AddComponent<HintPanelView>();
        panel.root = root;
        TMP_FontAsset font = GameUiStyle.RuntimeFont("ArialRoundedBold", bar.Font);

        // Invisible full-screen catcher: blocks the board and closes the hint when tapped.
        var catcher = root.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f); catcher.raycastTarget = true;
        var outside = root.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.navigation = new Navigation { mode = Navigation.Mode.None };
        outside.onClick.AddListener(() => panel.Invoke(panel.onClose));

        panel.spotlight = BoardSpotlight.Create(root);

        panel.marker = PropBarView.MakeRect("PlaceMarker", root);
        panel.marker.anchorMin = panel.marker.anchorMax = new Vector2(.5f, .5f);
        var ring = panel.marker.gameObject.AddComponent<GameUiGraphic>();
        ring.Shape = GameUiGraphic.Kind.Ring; ring.color = Color.white; ring.raycastTarget = false;

        panel.card = GameUiStyle.Panel("HintCardEdge", root, CardEdge, .22f);
        panel.card.anchorMin = panel.card.anchorMax = new Vector2(.5f, .5f);
        panel.card.GetComponent<GameUiGraphic>().raycastTarget = true; // taps on the card don't close it
        var swallow = panel.card.gameObject.AddComponent<Button>();
        swallow.transition = Selectable.Transition.None;
        swallow.navigation = new Navigation { mode = Navigation.Mode.None };
        RectTransform face = GameUiStyle.Panel("HintCard", panel.card, Card, .2f);
        face.anchorMin = Vector2.zero; face.anchorMax = Vector2.one;
        face.offsetMin = new Vector2(5f, 5f); face.offsetMax = new Vector2(-5f, -5f);
        panel.message = Text("Message", face, font, 40f);
        panel.message.alignment = TextAlignmentOptions.MidlineLeft;
        panel.message.rectTransform.anchorMin = Vector2.zero; panel.message.rectTransform.anchorMax = Vector2.one;
        panel.message.rectTransform.offsetMin = new Vector2(34f, 16f); panel.message.rectTransform.offsetMax = new Vector2(-34f, -16f);

        panel.button = PillButton(root, font, out panel.buttonLabel, () => panel.OnButton());
        root.gameObject.SetActive(false);
        return panel;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size)
    {
        RectTransform r = PropBarView.MakeRect(name, parent);
        var text = r.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.color = Brown; text.richText = true;
        text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static RectTransform PillButton(RectTransform parent, TMP_FontAsset font, out TextMeshProUGUI label, Action click)
    {
        RectTransform r = PropBarView.MakeRect("HintApply", parent);
        r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
        RectTransform shade = GameUiStyle.Panel("Shade", r, OrangeShade, .5f);
        shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one;
        shade.offsetMin = Vector2.zero; shade.offsetMax = new Vector2(0f, -8f);
        RectTransform face = GameUiStyle.Panel("Face", r, Orange, .5f);
        face.anchorMin = Vector2.zero; face.anchorMax = Vector2.one;
        face.offsetMin = new Vector2(0f, 8f); face.offsetMax = Vector2.zero;
        var hit = face.GetComponent<GameUiGraphic>(); hit.raycastTarget = true;
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = hit; b.transition = Selectable.Transition.None;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        b.onClick.AddListener(() => click());
        label = Text("Label", face, font, 54f);
        label.color = Color.white; label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        return r;
    }

    public void Show(HintStep hint, CellView[] cells, Action apply, Action close)
    {
        Hide();
        if (hint == null || cells == null) return;
        step = hint; onApply = apply; onClose = close; busy = false;
        message.text = hint.English;
        buttonLabel.text = hint.CanApply ? "Apply" : "Got it";
        var lit = new HashSet<int>(hint.Evidence);
        foreach (int i in hint.Targets) lit.Add(i);
        spotlight.Bind(cells);
        spotlight.SpotOnly(lit);
        spotlight.SetDimmed(true);
        spotlight.Snap();
        previewCells.Clear();
        previewCells.AddRange(hint.Crosses);
        placeCell = hint.IsPlacement && hint.Targets.Length > 0 ? hint.Targets[0] : -1;
        while (previews.Count < previewCells.Count)
        {
            RectTransform r = PropBarView.MakeRect("PreviewCross", root);
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.SetSiblingIndex(spotlight.transform.GetSiblingIndex() + 1);
            var x = r.gameObject.AddComponent<CrossStrokeGraphic>();
            x.raycastTarget = false;
            previews.Add(x);
        }
        for (int i = 0; i < previews.Count; i++) previews[i].gameObject.SetActive(i < previewCells.Count);
        marker.gameObject.SetActive(placeCell >= 0);
        gameObject.SetActive(true);
        root.SetAsLastSibling();
        LateUpdate();
    }

    private void OnButton()
    {
        if (step != null && step.CanApply) Invoke(onApply);
        else Invoke(onClose);
    }

    private void Invoke(Action callback)
    {
        if (busy || callback == null) return;
        busy = true;
        try { callback(); }
        finally { busy = false; }
    }

    public void Hide()
    {
        onApply = onClose = null;
        step = null;
        if (spotlight != null) spotlight.SetDimmed(false);
        gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (step == null || spotlight == null || !spotlight.Measure()) return;
        Rect board = spotlight.BoardRect;
        float side = spotlight.CellSide;
        // Card above the board (height follows the text), button below it.
        float width = Mathf.Max(board.width, 640f);
        float textHeight = message.GetPreferredValues(message.text, width - 68f, Mathf.Infinity).y;
        float height = Mathf.Max(150f, textHeight + 44f);
        card.sizeDelta = new Vector2(width, height);
        card.anchoredPosition = new Vector2(board.center.x, board.yMax + 50f + height * .5f);
        button.sizeDelta = new Vector2(Mathf.Min(board.width * .62f, 560f), 130f);
        button.anchoredPosition = new Vector2(board.center.x, board.yMin - 150f);
        // Pulsing white preview crosses on the cells that Apply will cross.
        float pulse = .55f + .35f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 5f));
        for (int i = 0; i < previewCells.Count; i++)
        {
            Rect cell = spotlight.CellRect(previewCells[i]);
            RectTransform r = previews[i].rectTransform;
            r.anchoredPosition = cell.center; r.sizeDelta = cell.size;
            previews[i].color = new Color(1f, 1f, 1f, pulse);
        }
        if (placeCell >= 0)
        {
            Rect cell = spotlight.CellRect(placeCell);
            float beat = .5f + .5f * Mathf.Sin(Time.unscaledTime * 4f);
            marker.anchoredPosition = cell.center;
            marker.sizeDelta = Vector2.one * side * (.72f + .12f * beat);
            marker.GetComponent<GameUiGraphic>().color = new Color(1f, 1f, 1f, .55f + .4f * beat);
        }
    }
}
