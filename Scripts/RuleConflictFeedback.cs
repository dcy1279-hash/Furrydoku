using UnityEngine;
using UnityEngine.UI;

// New effective errors only. No input, answer access, marks, lives or save writes.
public sealed class RuleConflictFeedback : MonoBehaviour
{
    public const float FlashDuration = .52f;
    private RectTransform board, overlay;
    private CellView[] cells;
    private LevelData level;
    private GameScreenLayout layout;
    private RuleConflictGraphic graphic;
    private float elapsed, duration;
    private bool playing;
    private Rect boardBounds;
    public bool IsPlaying => playing;
    public float Remaining => playing ? Mathf.Max(0f, duration - elapsed) : 0f;
    public static RuleConflictFeedback Create(RectTransform target, CellView[] views, LevelData data, GameScreenLayout screen)
    {
        if (target == null || !(target.parent is RectTransform) || views == null || data == null) return null;
        RectTransform root = null;
        try
        {
            root = PropBarView.MakeRect("RuleConflictFeedback", target.parent);
            var group = root.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            var feedback = root.gameObject.AddComponent<RuleConflictFeedback>();
            feedback.overlay = root; feedback.board = target; feedback.cells = (CellView[])views.Clone();
            feedback.level = data; feedback.layout = screen;
            feedback.graphic = root.gameObject.AddComponent<RuleConflictGraphic>(); feedback.graphic.raycastTarget = false;
            return feedback;
        }
        catch (System.Exception error)
        {
            if (root != null) { root.gameObject.SetActive(false); Destroy(root.gameObject); }
            Debug.LogWarning("规则红框创建失败，保留红叉、扣命和正常结算：" + error.Message, target);
            return null;
        }
    }
    public void Play(int index, BoardState state)
    {
        Cancel();
        if (!isActiveAndEnabled || board == null || level == null || state == null || cells == null ||
            cells.Length != state.CellCount || index < 0 || index >= cells.Length ||
            state.GetCellState(index) != CellState.Rejected) return;
        RuleConflictInfo info = RuleConflictInfo.Find(level, state, index);
        var rects = new Rect[cells.Length]; var corners = new Vector3[4];
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null || !(cells[i].transform is RectTransform rect)) return;
            rect.GetWorldCorners(corners); Vector2 min = board.InverseTransformPoint(corners[0]), max = min;
            for (int k = 1; k < 4; k++)
            {
                Vector2 p = board.InverseTransformPoint(corners[k]); min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            rects[i] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        overlay.anchorMin = board.anchorMin; overlay.anchorMax = board.anchorMax; overlay.pivot = board.pivot;
        overlay.sizeDelta = board.sizeDelta; overlay.anchoredPosition3D = board.anchoredPosition3D;
        overlay.localScale = board.localScale; overlay.localRotation = board.localRotation;
        overlay.SetSiblingIndex(board.GetSiblingIndex() + 1);
        graphic.Configure(rects, state.Size, info, board.rect.width);
        boardBounds = board.rect; elapsed = 0f; duration = info.HasConflicts ? FlashDuration : .24f; playing = true;
        if (layout != null) layout.PlayErrorShake();
    }
    private void Update()
    {
        if (!playing) return;
        if (board == null || !board.gameObject.activeInHierarchy || board.rect != boardBounds) { Cancel(); return; }
        elapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
        if (elapsed >= duration) { Cancel(); return; }
        float p = elapsed < .23f ? elapsed / .23f : elapsed >= .28f ? (elapsed - .28f) / .23f : -1f;
        float alpha = p <= 0f || p >= 1f ? 0f : Mathf.Pow(Mathf.Sin(p * Mathf.PI), .65f);
        graphic.SetFlash(alpha);
    }
    public void Cancel()
    {
        playing = false; elapsed = 0f;
        if (graphic != null) graphic.SetFlash(0f);
        if (layout != null) layout.CancelErrorShake();
    }
    private void OnDisable() => Cancel();
    private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
    private void OnApplicationPause(bool paused) { if (paused) Cancel(); }
    private void OnDestroy() => Cancel();
}
