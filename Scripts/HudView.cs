using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 棋盘上方的显示组件：不判断答案、不扣命、不保存另一套游戏状态。
public class HudView : MonoBehaviour
{
    // 与 GameScreenLayout 共用，顶部顺序/比例保持一致。
    public const float HeightRatio = .56f;
    public const float BoardGapRatio = .024f;
    public const float BodyLiftRatio = .0375f;
    public const float BoardAndCollectionDropRatio = .025f;
    private const float NumberSizeRatio = .05f; // LEVEL and SCORE numbers share one size
    private RectTransform board;
    private RectTransform header;
    private TextMeshProUGUI levelLabel;
    private TextMeshProUGUI scoreLabel;
    private RectTransform levelCaption, scoreCaption, collectionPanel, lifePanel;
    private CollectionTabGraphic collectionTab;
    public RectTransform BackAnchor { get; private set; }
    public RectTransform SettingsAnchor { get; private set; }
    public RectTransform RulesAnchor { get; private set; }
    public RectTransform RulesPanelAnchor { get; private set; }
    private int scoreTarget = -1, scoreStart, displayedScore;
    private float scoreElapsed;
    private bool scoreAnimating;
    // Other views borrow the HUD's original font; only the LEVEL/SCORE block uses the rounded face.
    private TMP_FontAsset baseFont;
    public TMP_FontAsset Font => baseFont != null ? baseFont : levelLabel != null ? levelLabel.font : null;
    private static readonly Color CaptionInk = new Color(.667f, .51f, .41f), NumberInk = new Color(.47f, .31f, .235f);
    public RectTransform ScoreDestination => scoreLabel != null ? scoreLabel.rectTransform : null;
    private Image[] lifeUnits;
    private RectTransform[] lifeRects;
    private Image[] lostLifeImages;
    private CellFeedback[] lifeFeedback;
    private int renderedLives = -1;
    private Sprite lifeBoneSprite, lifeLostSprite;
    private Sprite dogSilhouetteSprite;
    private bool victoryLifeActive;
    private const float BoneTiltDegrees = -18f; // matches the approved option-C mock
    private Image[] animalIcons;
    private PuppyAvatarView[] puppyIcons;
    private RectTransform[] slotRects;
    private int regionCount;
    private Image[] slotBackgrounds;
    private CellFeedback[] collectionFeedback;
    private bool[] filledSlots;

    private readonly Vector3[] corners = new Vector3[4];
    private Vector2 lastBoardMin;
    private Vector2 lastBoardMax;
    private float lastParentWidth = -1f;

    private static readonly Color LifeOn = Color.white;
    private static readonly Color LifeOff = new Color(.22f, .25f, .30f, .72f);

    public static HudView Create(RectTransform boardRect, LevelData level)
    {
        if (!Application.isPlaying || boardRect == null || level == null ||
            !(boardRect.parent is RectTransform) ||
            boardRect.GetComponentInParent<Canvas>() == null)
        {
            Debug.LogError("HudView：棋盘需要位于 Canvas（画布）中的 UI 容器下。");
            return null;
        }

        // 用格子已有的字体，不增加下载或字体资源关联步骤。
        TMP_Text prototype = boardRect.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = prototype != null ? prototype.font : TMP_Settings.defaultFontAsset;
        if (font == null)
        {
            Debug.LogError("HudView：没有可用的 TMP 字体，请保留格子 Mark 的字体资源。");
            return null;
        }

        var root = new GameObject("GameHud", typeof(RectTransform), typeof(CanvasGroup));
        root.layer = boardRect.gameObject.layer;
        root.transform.SetParent(boardRect.parent, false);
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        HudView view = root.AddComponent<HudView>();
        view.board = boardRect;
        view.header = root.GetComponent<RectTransform>();
        view.header.pivot = new Vector2(.5f, 0f);
        view.regionCount = level.Size;
        view.Build(level, font);
        view.RefreshLayout(true);
        return view;
    }

    private void Build(LevelData level, TMP_FontAsset font)
    {
        baseFont = font;
        TMP_FontAsset rounded = GameUiStyle.RuntimeFont("ArialRoundedBold", font);
        bool hasRounded = rounded != font;
        RectTransform title = MakeRect("LevelTitle", header);
        levelLabel = title.gameObject.AddComponent<TextMeshProUGUI>();
        levelLabel.font = rounded;
        levelLabel.text = FormatTitle(level.Id);
        levelLabel.color = NumberInk;
        levelLabel.fontStyle = hasRounded ? FontStyles.Normal : FontStyles.Bold;
        levelLabel.alignment = TextAlignmentOptions.Center;
        levelLabel.raycastTarget = false;
        levelLabel.enableAutoSizing = false; // same fixed size as the score so both read alike

        scoreLabel = MakeRect("RoundScore", header).gameObject.AddComponent<TextMeshProUGUI>();
        scoreLabel.font = rounded;
        scoreLabel.text = "0";
        scoreLabel.color = NumberInk;
        scoreLabel.fontStyle = hasRounded ? FontStyles.Normal : FontStyles.Bold;
        scoreLabel.alignment = TextAlignmentOptions.Center;
        scoreLabel.raycastTarget = false;
        scoreLabel.enableAutoSizing = false;
        levelCaption = Caption("LevelCaption", "LEVEL", rounded);
        scoreCaption = Caption("ScoreCaption", "SCORE", rounded);
        BackAnchor = MakeRect("BackButtonAnchor", header);
        SettingsAnchor = MakeRect("SettingsButtonAnchor", header);
        RulesAnchor = MakeRect("RulesButtonAnchor", header);
        RulesPanelAnchor = MakeRect("ReservedRulesSpace", header);
        // Same height, full-round endcaps and white surface; only their length differs.
        // The collection tab grows out of the board plate (reference look), not a floating capsule.
        collectionPanel = MakeRect("CollectionTab", header);
        collectionTab = collectionPanel.gameObject.AddComponent<CollectionTabGraphic>();
        collectionTab.color = Color.white; collectionTab.raycastTarget = false;
        lifePanel = GameUiStyle.Panel("LifeCapsule", header, Color.white, .5f);

        // Share one owned cartoon sprite across the life slots/loss overlays.
        // Tilting the UI keeps the icon's direction consistent with the old HUD.
        lifeBoneSprite = LifeBoneArt.CreateSprite();
        lifeLostSprite = LifeBoneArt.CreateLostSprite();
        dogSilhouetteSprite = HudDogSilhouetteArt.CreateSprite();
        lifeUnits = new Image[3];
        lifeRects = new RectTransform[3];
        lostLifeImages = new Image[3];
        lifeFeedback = new CellFeedback[3];
        for (int i = 0; i < 3; i++)
        {
            lifeRects[i] = MakeRect("Life_" + (i + 1), header);
            lifeUnits[i] = MakeImage(lifeRects[i], LifeOn);
            lifeUnits[i].sprite = lifeBoneSprite;
            lifeUnits[i].preserveAspect = true;
            lifeRects[i].localRotation = Quaternion.Euler(0f, 0f, BoneTiltDegrees);
            // 单一临时视觉覆盖旧生命颜色；父槽负责稳定暗色显示及排版。
            RectTransform lost = MakeRect("LostLifeVisual", lifeRects[i]);
            lost.anchorMin = Vector2.zero;
            lost.anchorMax = Vector2.one;
            lost.sizeDelta = Vector2.zero;
            lost.anchoredPosition = Vector2.zero;
            lostLifeImages[i] = MakeImage(lost, LifeOn);
            lostLifeImages[i].sprite = lifeBoneSprite;
            lostLifeImages[i].preserveAspect = true;
            lost.gameObject.SetActive(false);
        }

        slotRects = new RectTransform[regionCount];
        animalIcons = new Image[regionCount];
        puppyIcons = new PuppyAvatarView[regionCount];
        slotBackgrounds = new Image[regionCount];
        collectionFeedback = new CellFeedback[regionCount];
        filledSlots = new bool[regionCount];
        for (int region = 0; region < regionCount; region++)
        {
            // 顺序固定按区域编号，不随找到动物的先后顺序改变。
            RectTransform slot = MakeRect("AnimalSlot_Region_" + region, header);
            slotRects[region] = slot;
            slotBackgrounds[region] = MakeImage(slot, FirstLevelFactory.GetRegionColor(region));
            slotBackgrounds[region].sprite = dogSilhouetteSprite;
            slotBackgrounds[region].preserveAspect = true;

            RectTransform iconRect = MakeRect("Animal", slot);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.sizeDelta = Vector2.zero;
            iconRect.anchoredPosition = Vector2.zero;
            Image icon = MakeImage(iconRect, Color.white);
            icon.preserveAspect = true;
            puppyIcons[region] = PuppyAvatarView.Attach(icon, 1000 + region);
            icon.gameObject.SetActive(false);
            animalIcons[region] = icon;
        }
    }

    public void Render(LevelData level, BoardState state, CellView[] cells, bool animateScore = false)
    {
        if (level == null || state == null || cells == null ||
            lifeUnits == null || animalIcons == null || filledSlots == null ||
            level.Size != regionCount || state.CellCount != cells.Length ||
            level.RegionIds.Count != cells.Length)
            return;

        SetScore(AnimalScoreRules.TotalFor(Mathf.Max(0, state.PlacedCount - level.GivenCount)), animateScore);

        // 重试/恢复生命时取消旧损失视觉，普通重绘不会重复播放。
        if (state.RemainingLives > renderedLives) ResetLifeFeedback();
        renderedLives = state.RemainingLives;
        for (int i = 0; i < lifeUnits.Length; i++)
            SetLifeLook(i, i < state.RemainingLives);

        // 不读 SolutionIndices 来提前展示答案；只读已经正确放置的稳定状态。
        System.Array.Clear(filledSlots, 0, filledSlots.Length);

        for (int index = 0; index < cells.Length; index++)
        {
            if (state.GetCellState(index) != CellState.Placed || cells[index] == null)
                continue;

            int region = level.RegionIds[index];
            if (region < 0 || region >= animalIcons.Length ||
                !cells[index].TryGetAnimalAppearance(out Sprite sprite, out Color color))
                continue;

            Image icon = animalIcons[region];
            icon.sprite = sprite;
            icon.color = color;
            filledSlots[region] = true;
        }

        // 不把已填槽位反复关开，以免新落子打断别的槽位正在播放的反馈。
        for (int region = 0; region < animalIcons.Length; region++)
        {
            if (!filledSlots[region] && collectionFeedback[region] != null)
                collectionFeedback[region].ResetFeedback();
            animalIcons[region].gameObject.SetActive(filledSlots[region]);
            // A found portrait replaces the coloured silhouette; never a coloured square behind it.
            if (slotBackgrounds[region] != null) slotBackgrounds[region].enabled = !filledSlots[region];
        }
    }

    public void PlayCollectionFeedback(int region)
    {
        if (!isActiveAndEnabled || animalIcons == null || collectionFeedback == null ||
            slotBackgrounds == null || region < 0 || region >= animalIcons.Length ||
            animalIcons[region] == null || !animalIcons[region].gameObject.activeInHierarchy ||
            slotBackgrounds[region] == null) return;
        if (collectionFeedback[region] == null)
            collectionFeedback[region] = slotRects[region].gameObject.AddComponent<CellFeedback>();
        collectionFeedback[region].PlayPlace(animalIcons[region].rectTransform, slotBackgrounds[region]);
        if (puppyIcons[region] != null) puppyIcons[region].PlayArrival(false);
    }

    public void SetAnimalIdleAllowed(bool value)
    {
        if (puppyIcons == null) return;
        foreach (PuppyAvatarView puppy in puppyIcons) if (puppy != null) puppy.SetIdleAllowed(value);
    }

    // 只响应一个已经扣掉的生命；索引按原HUD右->左消耗顺序。
    public void PlayLifeLossFeedback(int previousLives, int remainingLives)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || lostLifeImages == null ||
            lifeFeedback == null || previousLives < 1 || previousLives > lostLifeImages.Length ||
            remainingLives != previousLives - 1 || remainingLives != renderedLives) return;
        int index = remainingLives;
        Image visual = lostLifeImages[index];
        if (visual == null) return;
        if (lifeFeedback[index] == null)
            lifeFeedback[index] = lifeRects[index].gameObject.AddComponent<CellFeedback>();
        lifeFeedback[index].ResetFeedback();
        visual.color = LifeOn;
        visual.rectTransform.localScale = Vector3.one;
        visual.gameObject.SetActive(true);
        lifeFeedback[index].PlayLifeLoss(visual.rectTransform, visual, () => {
            if (visual != null) visual.gameObject.SetActive(false);
        });
    }

    private void ResetLifeFeedback()
    {
        ResetVictoryLifeFeedback();
        if (lifeFeedback != null)
            foreach (CellFeedback feedback in lifeFeedback)
                if (feedback != null) feedback.ResetFeedback();
        if (lostLifeImages != null)
            foreach (Image image in lostLifeImages)
                if (image != null) image.gameObject.SetActive(false);
    }

    // Driven by the existing one-shot board celebration clock. No coroutine,
    // second award or saved state; restoring a finished round never calls it.
    public void ApplyVictoryLifeFeedback(float celebrationTime, float startDelay = .84f)
    {
        if (!isActiveAndEnabled || lifeUnits == null || lifeRects == null) return;
        int count = Mathf.Clamp(renderedLives, 0, lifeUnits.Length);
        if (count == 0) return;
        victoryLifeActive = true;
        float time = celebrationTime - startDelay;
        float togetherStart = (count - 1) * .085f + .25f + .025f;
        float together = LifePulse((time - togetherStart) / .30f);
        for (int i = 0; i < count; i++)
        {
            // Deliberately never touch lost slots, including the final group pop.
            float pulse = Mathf.Max(LifePulse((time - i * .085f) / .25f), together);
            lifeRects[i].localScale = Vector3.one * (1f + .20f * pulse);
            lifeUnits[i].color = Color.Lerp(LifeOn, Color.white, pulse);
        }
    }

    private static float LifePulse(float p)
    {
        if (p <= 0f || p >= 1f) return 0f;
        return p < .38f ? Mathf.SmoothStep(0f, 1f, p / .38f) :
            1f - Mathf.SmoothStep(0f, 1f, (p - .38f) / .62f);
    }

    public void ResetVictoryLifeFeedback()
    {
        if (!victoryLifeActive) return;
        victoryLifeActive = false;
        if (lifeRects == null || lifeUnits == null) return;
        for (int i = 0; i < lifeRects.Length; i++)
        {
            if (lifeRects[i] != null) lifeRects[i].localScale = Vector3.one;
            if (lifeUnits[i] != null) SetLifeLook(i, i < renderedLives);
        }
    }

    public void ResetFeedback()
    {
        FinishScoreFeedback();
        if (puppyIcons != null)
            foreach (PuppyAvatarView puppy in puppyIcons) if (puppy != null) puppy.ResetVisual();
        ResetLifeFeedback();
        if (collectionFeedback != null)
            foreach (CellFeedback feedback in collectionFeedback)
                if (feedback != null) feedback.ResetFeedback();
    }

    private void OnDisable() => ResetFeedback();
    private void OnApplicationFocus(bool focused) { if (!focused) ResetFeedback(); }
    private void OnApplicationPause(bool paused) { if (paused) ResetFeedback(); }

    private void OnDestroy()
    {
        // Shared Resources sprites stay alive for the next HUD; only generated fallbacks are freed.
        SharedUiSprites.ReleaseOwned(lifeLostSprite);
        SharedUiSprites.ReleaseOwned(lifeBoneSprite);
        SharedUiSprites.ReleaseOwned(dogSilhouetteSprite);
    }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        return rect;
    }

    private static Image MakeImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private RectTransform Caption(string name, string text, TMP_FontAsset face)
    {
        TextMeshProUGUI t = MakeRect(name, header).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = face; t.text = text; t.color = CaptionInk;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        t.enableAutoSizing = false; t.characterSpacing = 2f;
        return t.rectTransform;
    }

    // Alive = cream bone; lost = the grey bone art (or the old dark tint if it is missing).
    private void SetLifeLook(int i, bool alive)
    {
        if (lifeUnits == null || i < 0 || i >= lifeUnits.Length || lifeUnits[i] == null) return;
        bool art = lifeLostSprite != null;
        lifeUnits[i].sprite = alive || !art ? lifeBoneSprite : lifeLostSprite;
        lifeUnits[i].color = alive || art ? LifeOn : LifeOff;
    }

    private static string FormatTitle(string id)
    {
        if (string.IsNullOrEmpty(id)) return "1";
        int split = id.LastIndexOf('_');
        string suffix = split < 0 ? id : id.Substring(split + 1);
        return int.TryParse(suffix, out int number) && number > 0
            ? number.ToString()
            : id;
    }

    private void SetScore(int total, bool animate)
    {
        if (scoreLabel == null || total == scoreTarget) return;
        int previous = Mathf.Max(0, scoreTarget);
        scoreTarget = total;
        if (!animate || total < previous) { FinishScoreFeedback(); return; }
        // Keep the previous authoritative total while its newly-earned number
        // pops up, then roll to the new total as that number flies home.
        scoreStart = displayedScore; scoreElapsed = 0f; scoreAnimating = true;
        scoreLabel.text = scoreStart.ToString();
    }

    private void FinishScoreFeedback()
    {
        scoreAnimating = false;
        displayedScore = Mathf.Max(0, scoreTarget);
        if (scoreLabel != null) scoreLabel.text = displayedScore.ToString();
    }

    private void LateUpdate()
    {
        RefreshLayout(false);
        if (!scoreAnimating || scoreLabel == null) return;
        scoreElapsed += Mathf.Min(Time.unscaledDeltaTime, .06f);
        float p = Mathf.Clamp01((scoreElapsed - .94f) / .34f);
        float eased = p * p * (3f - 2f * p);
        displayedScore = Mathf.RoundToInt(Mathf.Lerp(scoreStart, scoreTarget, eased));
        scoreLabel.text = displayedScore.ToString();
        if (p >= 1f) FinishScoreFeedback();
    }

    private void RefreshLayout(bool force)
    {
        if (board == null || header == null ||
            !(header.parent is RectTransform parent) || levelLabel == null)
            return;

        board.GetWorldCorners(corners);
        Vector3 first = parent.InverseTransformPoint(corners[0]);
        Vector2 min = new Vector2(first.x, first.y);
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 local = parent.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, new Vector2(local.x, local.y));
            max = Vector2.Max(max, new Vector2(local.x, local.y));
        }

        if (!force && (min - lastBoardMin).sqrMagnitude < .0001f && (max - lastBoardMax).sqrMagnitude < .0001f &&
            Mathf.Approximately(parent.rect.width, lastParentWidth))
            return;

        float width = Mathf.Min(max.x - min.x, max.y - min.y, parent.rect.width * .96f);
        if (width <= 0f || float.IsNaN(width) || float.IsInfinity(width)) return;
        ResetFeedback(); // 新几何使用稳定原尺度，不继承失去生命或槽位的临时缩放。
        float height = width * HeightRatio;
        float gap = width * BoardGapRatio;

        // HUD 是 Board 的兄弟对象，不能成为 Grid Layout Group 的格子子对象。
        // 锚在安全区父容器局部原点，再摆到棋盘顶边上方；棋盘由 GameScreenLayout 排版。
        header.anchorMin = header.anchorMax = parent.pivot;
        header.anchoredPosition = new Vector2((min.x + max.x) * .5f, max.y + gap);
        header.sizeDelta = new Vector2(width, height);

        RectTransform title = levelLabel.rectTransform;
        title.anchorMin = title.anchorMax = new Vector2(.5f, 0f);
        title.anchoredPosition = new Vector2(-width * .17f, width * (.475f - BodyLiftRatio + BoardAndCollectionDropRatio));
        title.sizeDelta = new Vector2(width * .22f, width * .07f);
        levelLabel.fontSize = width * NumberSizeRatio;

        RectTransform scoreRect = scoreLabel.rectTransform;
        scoreRect.anchorMin = scoreRect.anchorMax = new Vector2(.5f, 0f);
        scoreRect.anchoredPosition = new Vector2(width * .12f, width * (.475f - BodyLiftRatio + BoardAndCollectionDropRatio));
        scoreRect.sizeDelta = new Vector2(width * .22f, width * .07f);
        scoreLabel.fontSize = width * NumberSizeRatio;
        scoreLabel.textWrappingMode = TextWrappingModes.NoWrap;

        GameUiStyle.Place(levelCaption, -width * .17f, width * (.535f - BodyLiftRatio + BoardAndCollectionDropRatio), width * .22f, width * .042f);
        GameUiStyle.Place(scoreCaption, width * .12f, width * (.535f - BodyLiftRatio + BoardAndCollectionDropRatio), width * .22f, width * .042f);
        GameUiStyle.LabelSize(levelCaption, "关卡", width * .035f);
        GameUiStyle.LabelSize(scoreCaption, "分数", width * .035f);
        // The board/lives/collection move up as a group. Counter-offset only
        // navigation, title/score and tutorial so their screen positions stay put.
        GameUiStyle.Place(BackAnchor, -width * .45f, width * (.510f - BodyLiftRatio + BoardAndCollectionDropRatio), width * .09f, width * .09f);
        GameUiStyle.Place(SettingsAnchor, width * .45f, width * (.510f - BodyLiftRatio + BoardAndCollectionDropRatio), width * .09f, width * .09f);
        GameUiStyle.Place(RulesAnchor, width * .34f, width * (.510f - BodyLiftRatio + BoardAndCollectionDropRatio), width * .08f, width * .08f);
        // Reserved even when hidden: no board jump on tooltip open/close.
        GameUiStyle.Place(RulesPanelAnchor, 0f, width * (.335f - BodyLiftRatio + BoardAndCollectionDropRatio), width * .98f, width * RulesStripView.HeightRatio);
        // Restore the previous centred two-row layout; artwork changes do not
        // move the board, tutorial or navigation. Both holders share their height/endcaps.
        float capsuleHeight = width * .095f;
        // Collection sits low, just above the board cells (reference: small gap); lives follow it down.
        float collectionY = width * .027f;
        float lifeY = width * (.135f + BoardAndCollectionDropRatio);
        float slotGap = width * .007f;
        float collectionWidth = width * .80f; // widest tab (10 colours); its fillets stay off the plate's corners
        float tabPadding = width * .05f;
        float lifePanelWidth = width * .36f;
        GameUiStyle.Place(lifePanel, 0f, lifeY, lifePanelWidth, capsuleHeight);

        float lifeWidth = width * .104f;
        float lifeGap = width * .018f;
        for (int i = 0; i < lifeRects.Length; i++)
        {
            RectTransform rect = lifeRects[i];
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2((i - 1) * (lifeWidth + lifeGap), lifeY);
            rect.sizeDelta = new Vector2(lifeWidth, width * .066f);
        }

        float slotSide = Mathf.Min(width * .090f,
            (collectionWidth - tabPadding - (regionCount - 1) * slotGap) / regionCount);
        float span = regionCount * slotSide + (regionCount - 1) * slotGap;
        // Tab hugs the slots and runs down into the board plate (slight overlap hides any seam).
        float tabTop = collectionY + capsuleHeight * .5f;
        float tabBottom = -gap - width * .004f;
        GameUiStyle.Place(collectionPanel, 0f, (tabTop + tabBottom) * .5f, span + tabPadding, tabTop - tabBottom);
        collectionTab.SetShape(capsuleHeight * .45f, width * .03f);
        for (int i = 0; i < slotRects.Length; i++)
        {
            RectTransform slot = slotRects[i];
            slot.anchorMin = slot.anchorMax = new Vector2(.5f, 0f);
            slot.anchoredPosition = new Vector2(
                -span * .5f + slotSide * .5f + i * (slotSide + slotGap), collectionY);
            slot.sizeDelta = new Vector2(slotSide, slotSide);
        }

        lastBoardMin = min;
        lastBoardMax = max;
        lastParentWidth = parent.rect.width;
    }
}
