using System;
using UnityEngine;

// 只排版已有 Board + HUD + 下方道具区，不生成格子、不读取答案、不重置进度。
[DefaultExecutionOrder(-200)]
public class GameScreenLayout : MonoBehaviour
{
    private RectTransform board;
    private RectTransform safeRect;
    private RectTransform presentationRoot;
    private float shakeElapsed = -1f;
    private SafeAreaFitter safeArea;
    private float preferredBoardSide;
    private Rect lastSafeRect;
    private Matrix4x4 lastSafeTransform;
    private bool hasLayout;
    private bool warningShown;
    private bool released;
    private GameObject background;

    // 保留运行时接管前的布局；移除控制器时不连带删除原棋盘。
    private Transform originalParent;
    private int originalSibling;
    private Vector2 originalAnchorMin, originalAnchorMax, originalPivot, originalSize;
    private Vector3 originalPosition, originalScale;
    private Quaternion originalRotation;

    public event Action LayoutChanging;

    public static GameScreenLayout Create(RectTransform boardRect)
    {
        Canvas canvas = boardRect != null ? boardRect.GetComponentInParent<Canvas>() : null;
        if (!Application.isPlaying || boardRect == null || canvas == null ||
            !(canvas.rootCanvas.transform is RectTransform canvasRect))
            return null;

        Canvas.ForceUpdateCanvases();
        SafeAreaFitter fitter = SafeAreaFitter.Create(canvasRect, "GameSafeArea");
        if (fitter == null) return null;
        GameScreenLayout layout = fitter.gameObject.AddComponent<GameScreenLayout>();
        layout.safeArea = fitter;
        layout.safeRect = fitter.GetComponent<RectTransform>();
        if (!fitter.RefreshNow())
        {
            Destroy(fitter.gameObject);
            return null;
        }

        layout.board = boardRect;
        layout.originalParent = boardRect.parent;
        layout.originalSibling = boardRect.GetSiblingIndex();
        layout.originalAnchorMin = boardRect.anchorMin;
        layout.originalAnchorMax = boardRect.anchorMax;
        layout.originalPivot = boardRect.pivot;
        layout.originalSize = boardRect.sizeDelta;
        layout.originalPosition = boardRect.anchoredPosition3D;
        layout.originalScale = boardRect.localScale;
        layout.originalRotation = boardRect.localRotation;
        layout.preferredBoardSide = Mathf.Min(boardRect.rect.width, boardRect.rect.height);
        if (layout.preferredBoardSide <= 0f || float.IsNaN(layout.preferredBoardSide) ||
            float.IsInfinity(layout.preferredBoardSide))
            layout.preferredBoardSide = 1000f;

        // Shake this visual-content child, never the safety/layout root. All
        // subsequent board siblings (HUD/props/feedback) share the same offset.
        layout.presentationRoot = PropBarView.MakeRect("GamePresentation", layout.safeRect);
        PropBarView.Stretch(layout.presentationRoot);
        boardRect.SetParent(layout.presentationRoot, false);
        boardRect.anchorMin = boardRect.anchorMax = new Vector2(.5f, .5f);
        boardRect.pivot = new Vector2(.5f, .5f);
        boardRect.localScale = Vector3.one;
        boardRect.localRotation = Quaternion.identity;
        if (!layout.RefreshNow())
        {
            layout.Release();
            return null;
        }
        // A native background behind the live board, not a screenshot or copied
        // game screen. Board region colours and camera settings stay untouched.
        RectTransform backdrop = PropBarView.MakeRect("GameCreamBackground", canvasRect);
        PropBarView.Stretch(backdrop);
        backdrop.SetAsFirstSibling();
        var fill = backdrop.gameObject.AddComponent<UnityEngine.UI.Image>();
        fill.color = GameUiStyle.PageBackground; fill.raycastTarget = false;
        layout.background = backdrop.gameObject;
        return layout;
    }

    public bool RefreshNow()
    {
        if (released || board == null || safeRect == null || safeArea == null ||
            !safeArea.RefreshNow())
            return false;

        Rect available = safeRect.rect;
        // 短边的 3% 留作呼吸边距；HUD 与棋盘共用比例，避免两处各猜一套。
        float margin = Mathf.Min(available.width, available.height) * .03f;
        float width = available.width - margin * 2f;
        float height = available.height - margin * 2f;
        float headerExtra = HudView.HeightRatio + HudView.BoardGapRatio;
        float footerExtra = PropBarView.HeightRatio + PropBarView.BoardGapRatio;
        float side = Mathf.Min(preferredBoardSide, width, height / (1f + headerExtra + footerExtra));
        if (side <= 0f || float.IsNaN(side) || float.IsInfinity(side)) return false;

        Vector2 targetSize = new Vector2(side, side);
        // Tighten the tutorial-to-lives gap by lifting the board/body together.
        // HudView counter-offsets its upper labels/tutorial, not its life/collection rows.
        Vector3 targetPosition = new Vector3(0f,
            side * ((footerExtra - headerExtra) * .5f + HudView.BodyLiftRatio - HudView.BoardAndCollectionDropRatio), 0f);
        Matrix4x4 safeTransform = safeRect.localToWorldMatrix;
        bool geometryChanged = !hasLayout || available != lastSafeRect ||
            safeTransform != lastSafeTransform ||
            board.rect.size != targetSize || board.anchoredPosition3D != targetPosition;
        if (!geometryChanged) return true;

        CancelErrorShake();
        LayoutChanging?.Invoke();
        board.anchorMin = board.anchorMax = new Vector2(.5f, .5f);
        board.pivot = new Vector2(.5f, .5f);
        board.sizeDelta = targetSize;
        board.anchoredPosition3D = targetPosition;
        lastSafeRect = available;
        lastSafeTransform = safeTransform;
        hasLayout = true;
        warningShown = false;
        return true;
    }

    private void LateUpdate()
    {
        if (!RefreshNow() && !released && !warningShown)
        {
            Debug.LogWarning("GameScreenLayout：暂时无法读取有效安全区，保留上一布局。", this);
            warningShown = true;
        }
        ApplyErrorShake();
    }

    public void PlayErrorShake()
    {
        if (!released && isActiveAndEnabled && presentationRoot != null) shakeElapsed = 0f;
    }
    public void CancelErrorShake()
    {
        shakeElapsed = -1f;
        if (presentationRoot != null) presentationRoot.anchoredPosition = Vector2.zero;
    }
    private void ApplyErrorShake()
    {
        if (shakeElapsed < 0f || presentationRoot == null || board == null) return;
        shakeElapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
        const float duration = .22f;
        if (shakeElapsed >= duration) { CancelErrorShake(); return; }
        float p = shakeElapsed / duration, envelope = Mathf.Sin(Mathf.PI * p) * (1f - p);
        float amount = Mathf.Min(board.rect.width, board.rect.height) * .006f;
        presentationRoot.anchoredPosition = new Vector2(
            Mathf.Sin(p * Mathf.PI * 8f) * amount * envelope,
            Mathf.Sin(p * Mathf.PI * 6f + .3f) * amount * envelope * .30f);
    }
    private void OnDisable() => CancelErrorShake();
    private void OnApplicationFocus(bool focused) { if (!focused) CancelErrorShake(); }
    private void OnApplicationPause(bool paused) { if (paused) CancelErrorShake(); }

    public void Release()
    {
        if (released) return;
        released = true;
        RestoreBoard();
        LayoutChanging = null;
        Destroy(gameObject);
    }

    private void RestoreBoard()
    {
        if (board == null || originalParent == null ||
            (board.parent != presentationRoot && board.parent != safeRect)) return;
        board.SetParent(originalParent, false);
        board.SetSiblingIndex(originalSibling);
        board.anchorMin = originalAnchorMin;
        board.anchorMax = originalAnchorMax;
        board.pivot = originalPivot;
        board.sizeDelta = originalSize;
        board.anchoredPosition3D = originalPosition;
        board.localScale = originalScale;
        board.localRotation = originalRotation;
    }

    // 场景/Canvas正在销毁时绝不能SetParent。只放弃借用引用。
    // 单独删除控制器时保留现有容器与Board，不连带销毁原场景棋盘。
    public void AbandonForTeardown()
    {
        released = true;
        LayoutChanging = null;
        shakeElapsed = -1f;
        presentationRoot = null;
        board = null;
        originalParent = null;
    }

    private void OnDestroy()
    {
        AbandonForTeardown();
        if (background != null) Destroy(background);
    }
}
