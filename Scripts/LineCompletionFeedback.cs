using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 纯表现订阅者：稳定行列完成事件 -> Good文字 + 整格缩放波纹 + 同步格缝金光。
// 不生成叉，不修改棋盘/生命，不读取答案，不保存动画进度。
public class LineCompletionFeedback : MonoBehaviour
{
    public const float CellWaveDuration = .20f;
    public const float CellWaveStagger = .065f;
    public const float CellWaveMaxScale = 1.06f;
    public const float MinimumFeedbackDuration = .68f;
    private RectTransform board, overlay, good;
    private CanvasGroup goodGroup;
    private CellView[] waveCells;
    private LineCompletionGoldGraphic seamGlow;
    private TextMeshProUGUI label;
    private CellView[] cells;
    private int size;
    private Func<LineCompletionInfo, bool> canDisplay;
    private Func<bool> suppressLabel;
    private Func<bool> canStartWave;
    private readonly Queue<LineCompletionInfo> waiting = new Queue<LineCompletionInfo>();
    private readonly HashSet<int> seen = new HashSet<int>();
    private readonly Vector3[] corners = new Vector3[4];
    private LineCompletionInfo current;
    private bool built, playing;
    private float elapsed, feedbackDuration, cellSide;
    private Vector2 goodOrigin;
    private Rect lastBoardRect;
    private Matrix4x4 lastBoardMatrix;

    // Yield the last line's fading tail to the colour wave, but never overlap
    // their main scaling. Pending row/column waves retain priority.
    public float RegionStartDelay
    {
        get
        {
            float end = (size - 1) * CellWaveStagger + CellWaveDuration;
            float currentDelay = playing ? Mathf.Max(0f, end - .04f - elapsed) : 0f;
            float duration = Mathf.Max(MinimumFeedbackDuration, end + .16f);
            return currentDelay + waiting.Count * duration;
        }
    }

    public void BindRegionSequence(Func<bool> mayStart) => canStartWave = mayStart;

    public static LineCompletionFeedback Create(RectTransform board, CellView[] cells,
        int size, TMP_FontAsset preferredFont, Func<LineCompletionInfo, bool> canDisplay,
        Func<bool> suppressLabel = null)
    {
        if (!Application.isPlaying || board == null || !(board.parent is RectTransform) ||
            size < 4 || size > 10 || cells == null || cells.Length != size * size ||
            canDisplay == null) return null;
        TMP_Text prototype = board.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = preferredFont != null ? preferredFont :
            prototype != null ? prototype.font : TMP_Settings.defaultFontAsset;
        if (font == null) return null;

        RectTransform root = MakeRect("LineCompletionFeedback", board.parent);
        root.gameObject.SetActive(false);
        try
        {
            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            root.gameObject.AddComponent<RectMask2D>(); // 限制文字和金光在棋盘内；格子波纹由视觉子容器负责。
            var view = root.gameObject.AddComponent<LineCompletionFeedback>();
            view.board = board;
            view.overlay = root;
            view.cells = (CellView[])cells.Clone();
            view.size = size;
            view.canDisplay = canDisplay;
            view.suppressLabel = suppressLabel;
            RectTransform glowRect = MakeRect("CellSeamGold", root);
            Stretch(glowRect);
            view.seamGlow = glowRect.gameObject.AddComponent<LineCompletionGoldGraphic>();
            view.seamGlow.raycastTarget = false;
            view.good = MakeRect("Good", root);
            view.goodGroup = view.good.gameObject.AddComponent<CanvasGroup>();
            view.goodGroup.blocksRaycasts = false;
            view.goodGroup.interactable = false;
            // No dark plate behind "Good!" any more: the word itself carries a white outline.
            RectTransform textRect = MakeRect("Text", view.good);
            Stretch(textRect);
            view.label = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset rounded = GameUiStyle.RuntimeFont("SFRounded-Heavy", font);
            view.label.font = rounded;
            view.label.fontSharedMaterial = GameUiStyle.PopTextMaterial(rounded);
            view.label.extraPadding = true;
            view.label.text = "Good!";
            GameUiStyle.PopGradient(view.label);
            view.label.alignment = TextAlignmentOptions.Center;
            view.label.enableAutoSizing = false;
            view.label.raycastTarget = false;
            view.built = true;
            view.RefreshLayout(true);
            view.ResetFeedback();
            root.gameObject.SetActive(true);
            return view;
        }
        catch (Exception exception)
        {
            root.gameObject.SetActive(false);
            Destroy(root.gameObject);
            Debug.LogWarning("LineCompletionFeedback：创建失败，跳过波纹，不改变玩法：" +
                exception.Message, board);
            return null;
        }
    }

    // 由GameController.LineCompleted事件调用，记录一次后不接受同线重复队列。
    public void Enqueue(LineCompletionInfo completion)
    {
        if (!built || !isActiveAndEnabled || !Valid(completion) || !MayDisplay(completion)) return;
        int key = (completion.Axis == PuzzleLineAxis.Row ? 0 : size) + completion.LineIndex;
        if (!seen.Add(key)) return;
        waiting.Enqueue(completion); // 最多2*N条，由已通知标志和这里的去重共同约束。
        if (!playing) BeginNext();
    }

    private void Update()
    {
        if (!built) return;
        if (playing && !MayDisplay(current)) StopCurrent();
        if (!playing) BeginNext();
        if (!playing) return;
        elapsed += Time.unscaledDeltaTime;
        ApplyFrame();
        if (elapsed >= feedbackDuration) StopCurrent();
    }

    private void BeginNext()
    {
        if (canStartWave != null && !canStartWave()) return;
        while (waiting.Count > 0)
        {
            LineCompletionInfo completion = waiting.Dequeue();
            if (!MayDisplay(completion) || !TryBegin(completion)) continue;
            return;
        }
    }

    private bool TryBegin(LineCompletionInfo completion)
    {
        if (!RefreshLayout(false)) return false;
        bool row = completion.Axis == PuzzleLineAxis.Row;
        int first = row ? completion.LineIndex * size : completion.LineIndex;
        int last = row ? first + size - 1 : (size - 1) * size + completion.LineIndex;
        if (!CellRect(first, out Rect firstRect) || !CellRect(last, out Rect lastRect) ||
            !CellRect(completion.AnimalCellIndex, out Rect animalRect))
            return false;

        cellSide = Mathf.Min(firstRect.width, firstRect.height);
        if (cellSide <= 0f) return false;
        int offset = row ? completion.ChangedCellIndex % size : completion.ChangedCellIndex / size;
        bool fromFirst = offset * 2 <= size - 1; // 保持原方向；正中左到右/上到下。
        var ordered = new CellView[size];
        var orderedRects = new Rect[size];
        var orderedColors = new Color[size];
        for (int step = 0; step < size; step++)
        {
            int position = fromFirst ? step : size - 1 - step;
            int index = row ? first + position : position * size + completion.LineIndex;
            if (cells[index] == null || !cells[index].isActiveAndEnabled) return false;
            if (!CellRect(index, out orderedRects[step])) return false;
            ordered[step] = cells[index];
            orderedColors[step] = cells[index].RegionColor;
        }
        waveCells = ordered;
        seamGlow?.Begin(orderedRects, orderedColors);
        // Let the last cell's light finish fading instead of being cut off.
        feedbackDuration = Mathf.Max(MinimumFeedbackDuration,
            (size - 1) * CellWaveStagger + Mathf.Max(CellWaveDuration + .16f, CellLightWash.Duration));

        float width = overlay.rect.width;
        good.sizeDelta = new Vector2(width * .25f, width * .080f);
        label.fontSize = width * .053f;
        // Good靠近已确认动物，限制在棋盘内，不遮住顶部HUD/下方道具。
        goodOrigin = new Vector2(
            Mathf.Clamp(animalRect.center.x, overlay.rect.xMin + good.rect.width * .5f,
                overlay.rect.xMax - good.rect.width * .5f),
            Mathf.Clamp(animalRect.yMax + good.rect.height * .2f,
                overlay.rect.yMin + good.rect.height * .5f,
                overlay.rect.yMax - good.rect.height * .5f - cellSide * .08f));
        current = completion;
        elapsed = 0f;
        playing = true;
        good.gameObject.SetActive(true);
        // Row/column completion is silent (reference has no sound here); the
        // light wave carries it, and the placement announcer is never cut off.
        ApplyFrame();
        return true;
    }

    private void ApplyFrame()
    {
        // 逐格稍微错开，1 -> 1.06 -> 1；背景和叉/动物同组，不缩放点击根对象。
        if (waveCells != null)
            for (int step = 0; step < waveCells.Length; step++)
            {
                CellView cell = waveCells[step];
                if (cell == null) { StopCurrent(); return; }
                float age = elapsed - step * CellWaveStagger;
                if (age <= 0f || age >= CellWaveDuration)
                {
                    cell.ResetLineWave();
                    continue;
                }
                float progress = age / CellWaveDuration;
                float scale = 1f + (CellWaveMaxScale - 1f) * Mathf.Sin(progress * Mathf.PI);
                if (!cell.TrySetLineWaveScale(scale)) { StopCurrent(); return; }
            }
        seamGlow?.SetTime(elapsed);
        float fadeIn = Mathf.Clamp01(elapsed / .10f);
        float fadeOut = Mathf.Clamp01((feedbackDuration - elapsed) / .20f);
        // Keep the existing row/column wave, but do not stack its Good label
        // onto the two-line animal score/praise text at the same moment.
        goodGroup.alpha = suppressLabel != null && suppressLabel() ? 0f : Mathf.Min(fadeIn, fadeOut);
        float pop = elapsed < .16f
            ? Mathf.SmoothStep(.78f, 1.06f, elapsed / .16f)
            : Mathf.SmoothStep(1.06f, 1f, Mathf.Clamp01((elapsed - .16f) / .16f));
        good.localScale = Vector3.one * pop;
        good.anchoredPosition = goodOrigin + Vector2.up * (cellSide * .08f *
            Mathf.Clamp01(elapsed / feedbackDuration));
    }

    private bool MayDisplay(LineCompletionInfo completion)
    {
        try { return canDisplay != null && canDisplay(completion); }
        catch (Exception exception)
        {
            Debug.LogWarning("LineCompletionFeedback：显示条件无效，跳过本条反馈：" + exception.Message, this);
            return false;
        }
    }

    private bool Valid(LineCompletionInfo completion) =>
        (completion.Axis == PuzzleLineAxis.Row || completion.Axis == PuzzleLineAxis.Column) &&
        completion.LineIndex >= 0 && completion.LineIndex < size &&
        completion.AnimalCellIndex >= 0 && completion.AnimalCellIndex < size * size &&
        completion.ChangedCellIndex >= 0 && completion.ChangedCellIndex < size * size;

    private bool CellRect(int index, out Rect rect)
    {
        rect = default;
        if (cells == null || index < 0 || index >= cells.Length || cells[index] == null ||
            !(cells[index].transform is RectTransform cell)) return false;
        cell.GetWorldCorners(corners);
        Vector3 first = overlay.InverseTransformPoint(corners[0]);
        Vector2 min = new Vector2(first.x, first.y), max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 local = overlay.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, new Vector2(local.x, local.y));
            max = Vector2.Max(max, new Vector2(local.x, local.y));
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return rect.width > 0f && rect.height > 0f;
    }

    private bool RefreshLayout(bool force)
    {
        if (!built || board == null || overlay == null ||
            !(overlay.parent is RectTransform parent) || board.parent != parent) return false;
        if (!force && lastBoardRect == board.rect && lastBoardMatrix == board.localToWorldMatrix)
            return true;
        ResetFeedback(); // 旋转/尺寸改变时不继续使用旧几何。
        overlay.anchorMin = overlay.anchorMax = board.anchorMin;
        overlay.pivot = board.pivot;
        overlay.sizeDelta = board.rect.size;
        overlay.anchoredPosition3D = board.anchoredPosition3D;
        overlay.localScale = board.localScale;
        overlay.localRotation = board.localRotation;
        if (seamGlow != null) seamGlow.rectTransform.pivot = overlay.pivot;
        lastBoardRect = board.rect;
        lastBoardMatrix = board.localToWorldMatrix;
        return true;
    }

    private void LateUpdate() { if (built) RefreshLayout(false); }

    private void StopCurrent()
    {
        playing = false;
        elapsed = 0f;
        if (waveCells != null)
            foreach (CellView cell in waveCells)
                if (cell != null) cell.ResetLineWave();
        waveCells = null;
        seamGlow?.Clear();
        if (good != null)
        {
            good.localScale = Vector3.one;
            good.gameObject.SetActive(false);
        }
        if (goodGroup != null) goodGroup.alpha = 0f;
    }

    public void ResetFeedback()
    {
        waiting.Clear();
        seen.Clear();
        StopCurrent();
    }

    private void OnDisable() => ResetFeedback();
    private void OnApplicationFocus(bool focused) { if (!focused) ResetFeedback(); }
    private void OnApplicationPause(bool paused) { if (paused) ResetFeedback(); }

    private void OnDestroy()
    {
        ResetFeedback();
        canDisplay = null;
        suppressLabel = null;
        canStartWave = null;
    }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }
}
