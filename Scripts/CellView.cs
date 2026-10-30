using UnityEngine;
using TMPro;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class CellView : MonoBehaviour
{
    // 保留字段名与类型，沿用原预制体引用。
    [SerializeField] private GameObject mark;
    [SerializeField] private GameObject symbol;
    [SerializeField] private Color normalMarkColor = Color.white;
    [SerializeField] private Color rejectedMarkColor = new Color(1f, .2f, .2f, 1f);
    [Header("Cell Layout（格子内容布局）")]
    [SerializeField, Range(.2f, 1f)] private float markSizeRatio = .9f;
    [SerializeField, Range(.2f, 1f)] private float symbolSizeRatio = .55f;
    [SerializeField, Range(.15f, .9f)] private float markFontSizeRatio = .55f;

    private static Sprite circleSprite;
    private CellFeedback placementFeedback;
    private PuppyAvatarView puppyAvatar;
    private CrossStrokeGraphic drawnMark;
    private CellPressFeedback pressFeedback;
    private float currentCellSide;
    private CellPatternGraphic regionPattern;
    private Graphic MarkGraphic => drawnMark != null ? drawnMark : mark != null ? mark.GetComponent<Graphic>() : null;
    public float AnimalArrivalRemaining => puppyAvatar != null ? puppyAvatar.ArrivalRemaining : 0f;
    public void SetAnimalIdleAllowed(bool value) { if (puppyAvatar != null) puppyAvatar.SetIdleAllowed(value); }
    public static Sprite GetPropButtonSprite() => GetCircleSprite();
    private CellState visualState = CellState.Empty;
    private RectTransform lineWaveVisual;
    private Image lineWaveBackground;
    private float lineScale = 1f, regionScale = 1f, pressScale = 1f;
    // While the visual group is squashed below 1, the root fill is hidden (alpha 0)
    // and drawn by the wave background copy instead; the root keeps its full hit area.
    private bool fillHidden;
    private Color hiddenFill;
    public int Index { get; private set; } = -1;

    public void Initialize(int index) => Index = index;

    // HUD 复用该格子当前动物的显示素材，不提前读取答案。
    public bool TryGetAnimalAppearance(out Sprite sprite, out Color color)
    {
        Image image = symbol != null ? symbol.GetComponent<Image>() : null;
        sprite = image != null ? image.sprite : null;
        color = image != null ? image.color : Color.white;
        return sprite != null;
    }

    // Presentation-only access to a currently confirmed avatar, never an answer lookup.
    public bool TryGetPlacedAnimalRect(out RectTransform rect)
    {
        rect = symbol != null ? symbol.transform as RectTransform : null;
        return visualState == CellState.Placed && rect != null && symbol.activeInHierarchy;
    }

    public bool TryGetVictoryMarkGraphic(out Graphic graphic)
    {
        graphic = MarkGraphic;
        return (visualState == CellState.Marked || visualState == CellState.Rejected) &&
            graphic != null && mark.activeInHierarchy;
    }

    // 图片使用父格子的相对锚点，TMP 字号按实际格子边长同步缩放。
    // 不修改 Mark/Symbol 的 active 状态，所以排版不会取消动物或红叉。
    public bool ApplyCellSize(float cellSide)
    {
        if (float.IsNaN(cellSide) || float.IsInfinity(cellSide) || cellSide <= 0f ||
            mark == null || symbol == null ||
            !(mark.transform is RectTransform markRect) ||
            !(symbol.transform is RectTransform symbolRect))
            return false;

        ResetFeedback(); // 尺寸重排不沿用旧缩放。
        BoardArtStyle.ApplyRounded(GetComponent<Image>(), cellSide * BoardArtStyle.CellCornerRatio);
        if (lineWaveBackground != null)
            BoardArtStyle.ApplyRounded(lineWaveBackground, cellSide * BoardArtStyle.CellCornerRatio);
        currentCellSide = cellSide;
        if (drawnMark == null) drawnMark = BoardArtStyle.EnsureCross(mark);
        if (pressFeedback != null && lineWaveVisual != null) pressFeedback.Bind(lineWaveVisual,cellSide);
        FitChild(markRect, Mathf.Clamp(markSizeRatio, .2f, 1f));
        bool hasPuppy = PuppyAvatarAssets.TryGet(out _, out _, out _, out _);
        FitChild(symbolRect, hasPuppy ? .86f : Mathf.Clamp(symbolSizeRatio, .2f, 1f));

        TMP_Text text = mark.GetComponent<TMP_Text>();
        if (text != null)
        {
            text.enableAutoSizing = false;
            text.fontSize = Mathf.Max(1f, cellSide * Mathf.Clamp(markFontSizeRatio, .15f, .9f));
        }
        Image markImage = mark.GetComponent<Image>();
        if (markImage != null) markImage.preserveAspect = true;
        Image symbolImage = symbol.GetComponent<Image>();
        if (symbolImage != null) symbolImage.preserveAspect = true;
        return true;
    }

    private static void FitChild(RectTransform child, float ratio)
    {
        float inset = (1f - ratio) * .5f;
        child.anchorMin = new Vector2(inset, inset);
        child.anchorMax = new Vector2(1f - inset, 1f - inset);
        child.pivot = new Vector2(.5f, .5f);
        child.anchoredPosition = Vector2.zero;
        child.sizeDelta = Vector2.zero;
        child.localScale = Vector3.one;
    }

    public bool PrepareForLevel(Color regionColor)
    {
        if (mark == null || symbol == null || mark.GetComponent<Graphic>() == null ||
            symbol.GetComponent<Image>() == null)
        {
            Debug.LogError("CellView：请保留 Mark 文字与 Symbol Image 子对象的关联。", this);
            return false;
        }
        SetRegionColor(regionColor);
        // 参考样式使用粗白叉；错误红叉的规则和颜色继续保留。
        normalMarkColor = Color.white;
        if (MarkGraphic != null) MarkGraphic.raycastTarget = false;
        Image image = symbol.GetComponent<Image>();
        if (PuppyAvatarAssets.TryGet(out Sprite face, out _, out _, out _))
        {
            image.sprite = face; image.color = Color.white;
            puppyAvatar = PuppyAvatarView.Attach(image, Index);
        }
        else if (image.sprite == null)
        {
            image.sprite = GetCircleSprite();
            image.color = new Color(.16f, .19f, .23f, 1f);
        }
        image.preserveAspect = true;
        image.raycastTarget = false;
        return true;
    }

    public void Render(CellState state)
    {
        if (mark == null || symbol == null || mark.GetComponent<Graphic>() == null)
        {
            Debug.LogError("CellView：请关联 Mark 与 Symbol，并保留 Mark 的文字或 Image。", this);
            return;
        }
        if (state != CellState.Placed || visualState != state) ResetFeedback();
        visualState = state;
        bool rejected = state == CellState.Rejected;
        MarkGraphic.color = rejected ? rejectedMarkColor : normalMarkColor;
        mark.SetActive(state == CellState.Marked || rejected);
        symbol.SetActive(state == CellState.Placed);
    }

    // 即时普通叉显示反馈；数据提交、读档、双击回退仍由控制器决定。
    public void RenderAnimated(CellState state)
    {
        if ((state != CellState.Empty && state != CellState.Marked) ||
            mark == null || symbol == null || !(mark.transform is RectTransform rect) ||
            mark.GetComponent<Graphic>() == null)
        { Render(state); return; }
        ResetFeedback();
        visualState = state;
        symbol.SetActive(false);
        Graphic graphic = MarkGraphic;
        graphic.color = normalMarkColor;
        mark.SetActive(true);
        // Begin the first stroke now, without waiting for the double-click
        // window. A second pointer-down can cancel this partial drawing.
        if (state == CellState.Marked && drawnMark != null)
        {
            drawnMark.PlayDraw();
            SetPressFeedback(true);SetPressFeedback(false);
            return;
        }
        if (placementFeedback == null) placementFeedback = GetComponent<CellFeedback>();
        if (placementFeedback == null) placementFeedback = gameObject.AddComponent<CellFeedback>();
        placementFeedback.PlayMark(rect, graphic, state == CellState.Marked, () => {
            if (mark != null && visualState == CellState.Empty) mark.SetActive(false);
        });
    }

    // 只由新的正确放置调用；普通Render/单击预览/读档不会自动重播。
    public void PlayPlacementFeedback()
    {
        if (symbol == null || !symbol.activeInHierarchy ||
            !(symbol.transform is RectTransform symbolRect)) return;
        if (puppyAvatar != null)
        {
            if (placementFeedback != null) placementFeedback.ResetFeedback();
            puppyAvatar.PlayArrival();
            return;
        }
        if (placementFeedback == null)
            placementFeedback = GetComponent<CellFeedback>();
        if (placementFeedback == null)
            placementFeedback = gameObject.AddComponent<CellFeedback>();
        placementFeedback.PlayPlace(symbolRect);
    }

    // 控制器仅在一次新错误已经落定后调用；Render/读档不自动触发。
    public void PlayErrorFeedback()
    {
        if (visualState != CellState.Rejected || mark == null || !mark.activeInHierarchy ||
            !(mark.transform is RectTransform rect)) return;
        if (drawnMark != null) drawnMark.PlayDraw();
        if (placementFeedback == null) placementFeedback = GetComponent<CellFeedback>();
        if (placementFeedback == null) placementFeedback = gameObject.AddComponent<CellFeedback>();
        placementFeedback.PlayError(rect);
    }

    public void ResetFeedback(bool preserveAvatarArrival = false)
    {
        if (drawnMark != null) drawnMark.Complete();
        if (pressFeedback != null) pressFeedback.ResetFeedback();
        if (preserveAvatarArrival) ResetLineWave();
        else { lineScale = regionScale = 1f; pressScale = 1f; ApplyWaveScale(); }
        if (placementFeedback != null) placementFeedback.ResetFeedback();
        if (puppyAvatar != null && (!preserveAvatarArrival || puppyAvatar.ArrivalRemaining <= 0f))
            puppyAvatar.ResetVisual();
        if (mark != null) mark.SetActive(visualState == CellState.Marked || visualState == CellState.Rejected);
    }

    // 色块、Mark与Symbol同组缩放；格子根保持原位/原尺寸，射线仍命中原根Image。
    public bool TrySetLineWaveScale(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale) || !isActiveAndEnabled)
            return false;
        lineScale = Mathf.Clamp(scale, 1f, 1.10f);
        return ApplyWaveScale();
    }

    public bool TrySetRegionPulseScale(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale)) return false;
        regionScale = Mathf.Clamp(scale, 1f, 1.06f);
        return ApplyWaveScale();
    }

    // Current on-screen scale of the tile visual (press squash / line or region pulse).
    public float VisualScale => Mathf.Max(lineScale, regionScale) * pressScale;

    private bool ApplyWaveScale()
    {
        float scale = Mathf.Max(lineScale, regionScale) * pressScale;
        Image original = GetComponent<Image>();
        if (Mathf.Abs(scale - 1f) < .0005f)
        {
            if (lineWaveVisual != null) lineWaveVisual.localScale = Vector3.one;
            if (lineWaveBackground != null) lineWaveBackground.gameObject.SetActive(false);
            ShowRootFill(original);
            return true;
        }
        if (!isActiveAndEnabled) { ShowRootFill(original); return false; }
        if (!EnsureLineWaveVisual() || original == null) { ShowRootFill(original); return false; }
        SyncWaveBackground(original);
        lineWaveBackground.color = fillHidden ? hiddenFill : original.color;
        lineWaveBackground.gameObject.SetActive(true);
        // Growing: the root stays visible behind the bigger copy (original behaviour).
        // Squashing: hide the root fill so only the tile itself shrinks, with nothing behind it.
        if (scale < 1f) HideRootFill(original); else ShowRootFill(original);
        lineWaveVisual.localScale = Vector3.one * Mathf.Clamp(scale, .85f, 1.10f);
        return true;
    }

    private void HideRootFill(Image image)
    {
        if (fillHidden || image == null) return;
        hiddenFill = image.color;
        image.color = new Color(hiddenFill.r, hiddenFill.g, hiddenFill.b, 0f);
        fillHidden = true;
    }

    // The copy was taken once; the cell may since have been resized and re-rounded (new level,
    // safe-area refit). Re-sync so the scaled copy keeps the same rounded corners, never square ones.
    private void SyncWaveBackground(Image original)
    {
        Image copy = lineWaveBackground;
        if (copy.sprite != original.overrideSprite) copy.sprite = original.overrideSprite;
        copy.type = original.type;
        copy.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
        if (copy.material != original.material) copy.material = original.material;
    }

    private void ShowRootFill(Image image)
    {
        if (!fillHidden) return;
        fillHidden = false;
        if (image != null) image.color = hiddenFill;
    }

    public void ResetLineWave()
    {
        lineScale = 1f;
        ApplyWaveScale();
    }

    public void SetPressFeedback(bool pressed)
    {
        if(!pressed){if(pressFeedback!=null)pressFeedback.SetPressed(false);return;}
        if(!isActiveAndEnabled||!EnsureLineWaveVisual())return;
        if(pressFeedback==null)pressFeedback=gameObject.AddComponent<CellPressFeedback>();
        float side=currentCellSide>0f?currentCellSide:Mathf.Min(lineWaveVisual.rect.width,lineWaveVisual.rect.height);
        pressFeedback.Bind(lineWaveVisual,side);pressFeedback.SetPressed(true);
    }

    private bool EnsureLineWaveVisual()
    {
        if (lineWaveVisual != null && lineWaveBackground != null) return true;
        if (!(transform is RectTransform cellRect) || mark == null || symbol == null ||
            !(mark.transform is RectTransform markRect) ||
            !(symbol.transform is RectTransform symbolRect)) return false;
        Image original = GetComponent<Image>();
        if (original == null) return false;
        var markLayout = new ChildLayout(markRect);
        var symbolLayout = new ChildLayout(symbolRect);
        ChildLayout? patternLayout = regionPattern != null ? new ChildLayout(regionPattern.rectTransform) : (ChildLayout?)null;
        GameObject visual = null;
        try
        {
            visual = new GameObject("CellLineWaveVisual", typeof(RectTransform));
            visual.layer = gameObject.layer;
            visual.SetActive(false);
            RectTransform rect = visual.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = cellRect.pivot;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.SetSiblingIndex(0);

            var background = new GameObject("WaveBackground", typeof(RectTransform), typeof(Image));
            background.layer = gameObject.layer;
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.SetParent(rect, false);
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.sizeDelta = Vector2.zero;
            backgroundRect.anchoredPosition = Vector2.zero;
            Image image = background.GetComponent<Image>();
            image.sprite = original.overrideSprite;
            image.type = original.type;
            image.material = original.material;
            image.color = original.color;
            image.preserveAspect = original.preserveAspect;
            image.fillCenter = original.fillCenter;
            image.fillMethod = original.fillMethod;
            image.fillAmount = original.fillAmount;
            image.fillOrigin = original.fillOrigin;
            image.fillClockwise = original.fillClockwise;
            image.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
            image.maskable = original.maskable;
            image.raycastTarget = false;
            background.SetActive(false);

            // Reparenting an active puppy into an inactive container invokes
            // PuppyAvatarView.OnDisable -> ResetVisual, cancelling its reveal
            // on the first region-pulse frame. Activate BEFORE moving children.
            visual.SetActive(true);
            // 原根色块仍可见且可命中；临时背景只在放大时覆盖它，休止时隐藏。
            // 保留原Mark/Symbol引用、锚点和状态，不复制叉/动物，不更改预制体。
            patternLayout?.Attach(rect);
            markLayout.Attach(rect);
            symbolLayout.Attach(rect);
            lineWaveVisual = rect;
            lineWaveBackground = image;
            return true;
        }
        catch (System.Exception exception)
        {
            markLayout.RestoreParent();
            symbolLayout.RestoreParent();
            patternLayout?.RestoreParent();
            if (visual != null) { visual.SetActive(false); Destroy(visual); }
            lineWaveVisual = null;
            lineWaveBackground = null;
            Debug.LogWarning("CellView：格子波纹显示创建失败，保持原显示：" + exception.Message, this);
            return false;
        }
    }

    // 换到等大的视觉容器时完整保存UI局部布局；失败可恢复原父对象。
    private readonly struct ChildLayout
    {
        private readonly RectTransform child;
        private readonly Transform parent;
        private readonly int sibling;
        private readonly Vector2 anchorMin, anchorMax, pivot, sizeDelta;
        private readonly Vector3 position, scale;
        private readonly Quaternion rotation;

        public ChildLayout(RectTransform child)
        {
            this.child = child;
            parent = child.parent;
            sibling = child.GetSiblingIndex();
            anchorMin = child.anchorMin;
            anchorMax = child.anchorMax;
            pivot = child.pivot;
            sizeDelta = child.sizeDelta;
            position = child.anchoredPosition3D;
            scale = child.localScale;
            rotation = child.localRotation;
        }

        public void Attach(Transform targetParent)
        {
            if (child == null || targetParent == null) return;
            child.SetParent(targetParent, false);
            child.anchorMin = anchorMin;
            child.anchorMax = anchorMax;
            child.pivot = pivot;
            child.sizeDelta = sizeDelta;
            child.anchoredPosition3D = position;
            child.localScale = scale;
            child.localRotation = rotation;
        }

        public void RestoreParent()
        {
            Attach(parent);
            if (child != null && parent != null) child.SetSiblingIndex(sibling);
        }
    }

    private void OnDisable() => ResetFeedback();

    private int decorationRegion;
    private bool decorationVisible;
    // visible = Settings > Clarity: one soft per-colour pet icon in the centre of the cell.
    public void ApplyRegionDecoration(int regionId, bool visible = false)
    {
        decorationRegion = regionId;
        decorationVisible = visible;
        // Keep patterns available to a future colour-assistance option. Plain
        // coloured cells are the default, not permanently patterned artwork.
        if(!visible){if(regionPattern!=null)regionPattern.gameObject.SetActive(false);return;}
        if (regionPattern == null)
        {
            Transform parent = lineWaveVisual != null ? lineWaveVisual : transform;
            RectTransform rect = PropBarView.MakeRect("RegionSoftPattern", parent);
            PropBarView.Stretch(rect);
            rect.SetSiblingIndex(lineWaveVisual != null ? 1 : 0);
            regionPattern = rect.gameObject.AddComponent<CellPatternGraphic>();
        }
        regionPattern.gameObject.SetActive(true);
        regionPattern.Configure(regionId, RegionColor, true);
    }
    // Read-only: current region fill colour, for presentation layers (completion light).
    public Color RegionColor => fillHidden ? hiddenFill : GetComponent<Image>().color;
    // Press 'squash' (driven by CellPressFeedback): visual group only, never the root.
    public void SetPressScale(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale)) return;
        pressScale = Mathf.Clamp(scale, .85f, 1.05f);
        ApplyWaveScale();
    }
    public void SetRegionColor(Color color)
    {
        if (fillHidden)
        {
            hiddenFill = color;
            GetComponent<Image>().color = new Color(color.r, color.g, color.b, 0f);
            if (lineWaveBackground != null) lineWaveBackground.color = color;
        }
        else GetComponent<Image>().color = color;
        if (regionPattern != null) regionPattern.Configure(decorationRegion, color, decorationVisible);
    }

    // 共用高清透明圆形占位：棋盘、槽位、结算都复用一张，不逐格生成。
    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null)
            return circleSprite;
        const int side = 512;
        // 保留原圆形的相对大小；1 个源像素抗锯齿边缘，避免放大后边缘过软。
        float radius = side * (28f / 64f);
        var texture = new Texture2D(side, side, TextureFormat.RGBA32, true);
        texture.name = "RuntimeCirclePlaceholder";
        // 大展示区保留细节，小棋盘/槽位通过 mipmaps 平滑缩小，减少锯齿。
        texture.filterMode = FilterMode.Trilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[side * side];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                float dx = x - (side - 1) * .5f, dy = y - (side - 1) * .5f;
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * side + x] = new Color(1f, 1f, 1f, alpha);
            }
        texture.SetPixels(pixels);
        texture.Apply(true, true);
        circleSprite = Sprite.Create(texture, new Rect(0, 0, side, side), new Vector2(.5f, .5f), side);
        circleSprite.name = "RuntimeCirclePlaceholder";
        return circleSprite;
    }
}
