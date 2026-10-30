using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Two lines around a newly confirmed avatar: +score above, praise below.
// Bounded reusable pool; the flying text never owns or awards gameplay points.
public sealed class AnimalScorePopupView : MonoBehaviour
{
    private const int PoolSize = 4;
    private const float FlyStart = .94f;
    private const float FlyDuration = .34f;
    private const float Duration = FlyStart + FlyDuration;
    private readonly Popup[] popups = new Popup[PoolSize];
    private readonly Vector3[] corners = new Vector3[4];
    private RectTransform board, overlay, destination;
    private TMP_FontAsset font;
    private Material scoreMaterial, praiseMaterial;
    private Rect previousRect;
    private Matrix4x4 previousMatrix;
    private int nextSlot;
    private bool built, focused = true, paused;
    public bool IsPlaying
    {
        get { foreach (Popup popup in popups) if (popup != null && popup.Active) return true; return false; }
    }

    public static AnimalScorePopupView Create(RectTransform board, TMP_FontAsset font, RectTransform destination)
    {
        if (!Application.isPlaying || board == null || !(board.parent is RectTransform) ||
            font == null || destination == null) return null;
        var go = new GameObject("AnimalScoreFeedback", typeof(RectTransform), typeof(CanvasGroup));
        go.layer = board.gameObject.layer;
        go.SetActive(false);
        AnimalScorePopupView view = null;
        try
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(board.parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            view = go.AddComponent<AnimalScorePopupView>();
            font = GameUiStyle.RuntimeFont("SFRounded-Heavy", font); // rounded face like the rest of the UI
            view.board = board; view.overlay = rect; view.font = font; view.destination = destination;
            view.scoreMaterial = GameUiStyle.PopTextMaterial(font);
            view.praiseMaterial = GameUiStyle.PopTextMaterial(font);
            for (int i = 0; i < PoolSize; i++) view.popups[i] = view.BuildPopup(i);
            view.built = true;
            view.focused = Application.isFocused;
            view.RefreshLayout(true);
            go.SetActive(true);
            // Own sorting layer above every board overlay (tutorial/hint dimmers live on the canvas
            // root, so sibling order alone could not lift the score text above them).
            Canvas own = go.GetComponent<Canvas>() ?? go.AddComponent<Canvas>();
            Canvas root = board.GetComponentInParent<Canvas>();
            if (root != null) own.additionalShaderChannels = root.rootCanvas.additionalShaderChannels;
            own.overrideSorting = true; own.sortingOrder = 30;
            return view;
        }
        catch (Exception error)
        {
            go.SetActive(false); Destroy(go);
            Debug.LogWarning("AnimalScorePopupView：弹字创建失败，计分不受影响：" + error.Message, board);
            return null;
        }
    }

    public void Play(CellView cell, int ordinal, bool finalAnimal)
    {
        if (!built || !isActiveAndEnabled || cell == null || !cell.isActiveAndEnabled ||
            AnimalScoreRules.RewardFor(ordinal) <= 0 || !RefreshLayout(false) ||
            !(cell.transform is RectTransform cellRect)) return;
        cellRect.GetWorldCorners(corners);
        Vector3 a = overlay.InverseTransformPoint(corners[0]);
        Vector3 b = overlay.InverseTransformPoint(corners[2]);
        float side = Mathf.Min(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        if (side <= 0f) return;
        Popup popup = null;
        for (int i = 0; i < PoolSize; i++)
        {
            int slot = (nextSlot + i) % PoolSize;
            if (popups[slot].Active) continue;
            popup = popups[slot]; nextSlot = (slot + 1) % PoolSize; break;
        }
        if (popup == null) { popup = popups[nextSlot]; nextSlot = (nextSlot + 1) % PoolSize; }
        float width = board.rect.width;
        popup.Root.sizeDelta = new Vector2(width * .44f, side * 1.4f);
        popup.ScoreOrigin = new Vector2(0f, side * .45f);
        popup.PraiseOrigin = new Vector2(0f, -side * .45f);
        popup.Score.rectTransform.sizeDelta = new Vector2(width * .44f, width * .11f);
        popup.Praise.rectTransform.sizeDelta = new Vector2(width * .44f, width * .10f);
        popup.Score.fontSize = Mathf.Clamp(side * .48f, width * .045f, width * .060f);
        popup.Praise.fontSize = popup.Score.fontSize * .80f;
        // ASCII matches the reference and the bundled SDF font without adding
        // a missing Chinese glyph or copying a system font into the project.
        popup.Score.text = "+" + AnimalScoreRules.RewardFor(ordinal);
        string[] words = { "Good!", "Great!", "Brilliant!", "Clear!" };
        popup.Praise.text = finalAnimal ? "Brilliant!" : words[(ordinal - 1) % words.Length];
        // Clamp the visible glyphs, not the much wider text container, so a
        // score near the board edge still belongs to the selected avatar.
        float halfWidth = Mathf.Min(width * .22f, Mathf.Max(popup.Score.GetPreferredValues().x,
            popup.Praise.GetPreferredValues().x) * .58f + width * .01f);
        float halfHeight = side * .45f + popup.Score.fontSize * .62f;
        popup.Root.anchoredPosition = new Vector2(
            Mathf.Clamp((a.x + b.x) * .5f, overlay.rect.xMin + halfWidth, overlay.rect.xMax - halfWidth),
            Mathf.Clamp((a.y + b.y) * .5f, overlay.rect.yMin + halfHeight, overlay.rect.yMax - halfHeight));
        popup.Time = 0f; popup.Active = true;
        // Overlays created later (tutorial/hint dimmer, line effects) would otherwise cover and
        // cut the score text; bring the popup layer to the front each time one plays.
        overlay.SetAsLastSibling();
        popup.Root.SetAsLastSibling(); popup.Root.gameObject.SetActive(true);
        Apply(popup);
    }

    private Popup BuildPopup(int index)
    {
        RectTransform root = MakeRect("ScorePopup_" + index, overlay);
        var popup = new Popup { Root = root };
        popup.Score = MakeText("Score", root, scoreMaterial);
        GameUiStyle.PopGradient(popup.Score);
        popup.Praise = MakeText("Praise", root, praiseMaterial);
        GameUiStyle.PopGradient(popup.Praise);
        popup.ScoreGroup = popup.Score.gameObject.AddComponent<CanvasGroup>();
        popup.PraiseGroup = popup.Praise.gameObject.AddComponent<CanvasGroup>();
        root.gameObject.SetActive(false);
        return popup;
    }

    private TextMeshProUGUI MakeText(string name, Transform parent, Material material)
    {
        var text = MakeRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSharedMaterial = material;
        text.fontStyle = FontStyles.Normal; text.alignment = TextAlignmentOptions.Center;
        text.richText = false; text.enableAutoSizing = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false; text.extraPadding = true;
        text.UpdateMeshPadding();
        return text;
    }


    private void LateUpdate()
    {
        if (!built || !RefreshLayout(false) || !focused || paused) return;
        foreach (Popup popup in popups)
        {
            if (!popup.Active) continue;
            popup.Time += Mathf.Min(Time.unscaledDeltaTime, .06f);
            if (popup.Time >= Duration) { Hide(popup); continue; }
            Apply(popup);
        }
    }

    private void Apply(Popup popup)
    {
        float time = popup.Time;
        RectTransform score = popup.Score.rectTransform, praise = popup.Praise.rectTransform;
        score.localRotation = praise.localRotation = Quaternion.identity;
        float fly = Mathf.Clamp01((time - FlyStart) / FlyDuration);
        float flyEase = Smooth(fly);
        Vector2 target = destination != null
            ? (Vector2)popup.Root.InverseTransformPoint(destination.position) : popup.ScoreOrigin;
        Vector2 control = (popup.ScoreOrigin + target) * .5f + Vector2.up * (board.rect.width * .08f);
        score.anchoredPosition = (1f - flyEase) * (1f - flyEase) * popup.ScoreOrigin +
            2f * (1f - flyEase) * flyEase * control + flyEase * flyEase * target;
        score.localScale = Vector3.one * PopScale(time) * Mathf.Lerp(1f, .24f, flyEase);
        popup.ScoreGroup.alpha = Smooth(time / .065f) * (1f - Smooth((fly - .68f) / .32f));
        float praiseTime = time - .065f;
        praise.anchoredPosition = popup.PraiseOrigin + Vector2.up * (board.rect.width * .010f * Smooth(time / .90f));
        praise.localScale = Vector3.one * PopScale(praiseTime);
        popup.PraiseGroup.alpha = Smooth(praiseTime / .065f) * (1f - Smooth((time - .76f) / .18f));
    }

    private bool RefreshLayout(bool force)
    {
        if (board == null || overlay == null || board.parent != overlay.parent) { ResetFeedback(); return false; }
        if (!force && board.rect == previousRect && board.localToWorldMatrix == previousMatrix) return true;
        ResetFeedback();
        overlay.anchorMin = board.anchorMin; overlay.anchorMax = board.anchorMax;
        overlay.pivot = board.pivot; overlay.sizeDelta = board.sizeDelta;
        overlay.anchoredPosition3D = board.anchoredPosition3D;
        overlay.localScale = board.localScale; overlay.localRotation = board.localRotation;
        previousRect = board.rect; previousMatrix = board.localToWorldMatrix;
        return true;
    }

    public void ResetFeedback() { foreach (Popup popup in popups) if (popup != null) Hide(popup); }
    private static void Hide(Popup popup)
    { popup.Active = false; popup.Time = 0f; if (popup.Root != null) popup.Root.gameObject.SetActive(false); }
    private void OnDisable() => ResetFeedback();
    private void OnApplicationFocus(bool value) { focused = value; if (!value) ResetFeedback(); }
    private void OnApplicationPause(bool value) { paused = value; if (value) ResetFeedback(); }
    private void OnDestroy()
    {
        ResetFeedback();
        if (scoreMaterial != null) Destroy(scoreMaterial);
        if (praiseMaterial != null) Destroy(praiseMaterial);
    }

    private static float Smooth(float p) { p = Mathf.Clamp01(p); return p * p * (3f - 2f * p); }
    private static float PopScale(float time)
    {
        if (time <= .13f) return Mathf.Lerp(.45f, 1.16f, Smooth(time / .13f));
        return Mathf.Lerp(1.16f, 1f, Smooth((time - .13f) / .11f));
    }
    private static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
        return rect;
    }
    private sealed class Popup
    {
        public RectTransform Root;
        public TextMeshProUGUI Score, Praise;
        public CanvasGroup ScoreGroup, PraiseGroup;
        public Vector2 ScoreOrigin, PraiseOrigin;
        public float Time;
        public bool Active;
    }
}
