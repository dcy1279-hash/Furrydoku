using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 只负责结算显示和下一关/重玩请求；胜负与重置仍由 GameController/BoardState 决定。
public class ResultPanelView : MonoBehaviour
{
    private RectTransform overlay;
    private RectTransform content;
    private RectTransform safeRect;
    private SafeAreaFitter safeArea;
    private TextMeshProUGUI title;
    private TextMeshProUGUI description;
    private TextMeshProUGUI buttonLabel;
    private TextMeshProUGUI placeholderLabel;
    private Image animalImage;
    private Button replayButton;
    private TMP_FontAsset font;
    private Vector2 lastSize = new Vector2(-1f, -1f);
    [NonSerialized] private Action onReplay;
    private bool isShown;
    private bool actionInProgress;
    private CanvasGroup overlayGroup;
    private CanvasGroup[] entryGroups;
    private bool entering;
    private float entryTime;
    // Fired when the result is actually presented: the puppy (or the fallback card) appears.
    public event Action<bool> ResultAppeared;
    private PuppyVictoryView puppyVictory;
    private PuppyFailureView puppyFailure;
    private bool showingPuppyFailure;
    private Image dimmerImage;
    private bool showingPuppyVictory;
    private RectTransform boardRect;
    private BoardVictoryFeedback boardVictory;
    private bool showingBoardVictory;
    private float victoryOverlayStartAlpha;
    private const float PreludeDimmer = .12f;
    // Translucent enough that the finished board stays visible behind the
    // puppy celebration (like the reference popup); failure keeps .92.
    private const float VictoryDimmer = .74f;
    private const float FailureDimmer = .92f;

    public static ResultPanelView Create(RectTransform board)
    {
        if (!Application.isPlaying || board == null)
            return null;
        Canvas canvas = board.GetComponentInParent<Canvas>();
        if (canvas == null || !(canvas.rootCanvas.transform is RectTransform parent))
        {
            Debug.LogError("ResultPanelView：棋盘需要位于 Canvas（画布）中。");
            return null;
        }

        TMP_Text prototype = board.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset availableFont = prototype != null ? prototype.font : TMP_Settings.defaultFontAsset;
        if (availableFont == null)
        {
            Debug.LogError("ResultPanelView：请保留格子 Mark 的 TMP 字体资源。");
            return null;
        }

        var root = new GameObject("ResultPanel", typeof(RectTransform), typeof(CanvasGroup));
        root.layer = canvas.gameObject.layer;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Stretch(rect);
        Image dimmer = root.AddComponent<Image>();
        dimmer.color = new Color(.025f, .03f, .07f, .80f);
        dimmer.raycastTarget = true; // 全屏遮罩接住空白处点击，不透到棋盘。
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        group.interactable = true;

        ResultPanelView view = root.AddComponent<ResultPanelView>();
        view.overlay = rect;
        view.boardRect = board;
        view.overlayGroup = group;
        view.dimmerImage = dimmer;
        view.font = availableFont;
        view.safeArea = SafeAreaFitter.Create(rect, "ResultSafeArea");
        if (view.safeArea == null || !view.safeArea.RefreshNow())
        {
            Destroy(root);
            Debug.LogError("ResultPanelView：结算安全区创建失败。");
            return null;
        }
        view.safeRect = view.safeArea.GetComponent<RectTransform>();
        view.Build();
        view.puppyVictory = PuppyVictoryView.TryCreate(view.safeRect);
        view.puppyFailure = PuppyFailureView.TryCreate(view.safeRect);
        view.RefreshLayout(true);
        root.SetActive(false);
        return view;
    }

    private void Build()
    {
        // 遮罩仍覆盖整个画布，只有内容缩进安全区，空白点击不能穿透。
        content = MakeRect("ResultContent", safeRect);
        title = MakeText("Title", content);
        description = MakeText("Description", content);

        RectTransform animal = MakeRect("AnimalDisplay", content);
        animalImage = animal.gameObject.AddComponent<Image>();
        animalImage.preserveAspect = true;
        animalImage.raycastTarget = false;
        placeholderLabel = MakeText("PlaceholderLabel", animal);
        placeholderLabel.rectTransform.anchorMin = new Vector2(.18f, .32f);
        placeholderLabel.rectTransform.anchorMax = new Vector2(.82f, .68f);
        placeholderLabel.rectTransform.sizeDelta = Vector2.zero;
        placeholderLabel.color = new Color(.12f, .15f, .20f, 1f);

        RectTransform buttonRect = MakeRect("LevelReplayButton", content);
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = Color.white;
        buttonImage.raycastTarget = true;
        replayButton = buttonRect.gameObject.AddComponent<Button>();
        replayButton.targetGraphic = buttonImage;
        Navigation navigation = replayButton.navigation;
        navigation.mode = Navigation.Mode.None;
        replayButton.navigation = navigation;
        replayButton.onClick.AddListener(HandleReplayPressed);
        buttonLabel = MakeText("Label", buttonRect);
        Stretch(buttonLabel.rectTransform);
        ButtonPressFeedback.Attach(replayButton);
        entryGroups = new[] {
            title.gameObject.AddComponent<CanvasGroup>(),
            animal.gameObject.AddComponent<CanvasGroup>(),
            description.gameObject.AddComponent<CanvasGroup>(),
            buttonRect.gameObject.AddComponent<CanvasGroup>()
        };
    }

    public void Show(
        bool won, LevelData level, Sprite animalSprite, Color animalColor, Action replayHandler,
        LevelData nextLevel = null, bool batchComplete = false, int remainingLives = 3,
        CellView[] victoryCells = null, int missingAnimals = -1, HudView victoryHud = null, float placementWarmup = 0f)
    {
        if (level == null || replayHandler == null) return;
        if (boardVictory != null) boardVictory.Cancel();
        showingBoardVictory = false;
        if (puppyFailure != null) puppyFailure.Hide();
        showingPuppyFailure = false;
        if (won && puppyVictory != null)
        {
            // Existing live board/HUD stay in place under this transparent UI.
            // Never rebuild/copy a board just to show a victory background.
            onReplay = replayHandler;
            actionInProgress = false;
            isShown = true;
            entering = false;
            showingPuppyVictory = false;
            puppyVictory.Hide();
            content.gameObject.SetActive(false);
            replayButton.interactable = false;
            overlayGroup.alpha = 1f;
            dimmerImage.color = new Color(18f / 255f, 20f / 255f, 37f / 255f, 0f);
            gameObject.SetActive(true);
            overlay.SetAsLastSibling();
            string nextId = nextLevel != null ? nextLevel.Id : null;
            if (victoryCells != null)
            {
                if (boardVictory == null) boardVictory = gameObject.AddComponent<BoardVictoryFeedback>();
                showingBoardVictory = true;
                if (boardVictory.TryPlay(boardRect, victoryCells,
                    () => BeginPuppyVictory(remainingLives, nextId, batchComplete, true), victoryHud, placementWarmup)) return;
                showingBoardVictory = false;
            }
            // Restore/reenable an already-won round directly, without replaying
            // the board celebration or dispatching a second gameplay event.
            BeginPuppyVictory(remainingLives, nextId, batchComplete, false);
            return;
        }
        showingPuppyVictory = false;
        if (puppyVictory != null) puppyVictory.Hide();
        int missing = Mathf.Clamp(missingAnimals >= 0 ? missingAnimals : level.Size, 0, level.Size);
        if (!won && puppyFailure != null)
        {
            onReplay = replayHandler;
            actionInProgress = false; isShown = true; entering = false;
            showingPuppyFailure = true;
            content.gameObject.SetActive(false);
            replayButton.interactable = false;
            overlayGroup.alpha = 0f;
            dimmerImage.color = new Color(18f / 255f, 20f / 255f, 37f / 255f, FailureDimmer);
            gameObject.SetActive(true); overlay.SetAsLastSibling();
            puppyFailure.Show(missing, HandleReplayPressed);
            ResultAppeared?.Invoke(false);
            return;
        }
        content.gameObject.SetActive(true);
        dimmerImage.color = new Color(.025f, .03f, .07f, .80f);
        bool chinese = font.HasCharacters(
            "搞定了！挑战失败小动物都已就位机会用完了，别灰心，再试一次。重玩重试第关动物占位下一本批关卡已完成还差只没找到",
            out uint[] missingCharacters, true, false);
        string levelName = GetLevelName(level.Id, chinese);

        title.text = chinese ? (batchComplete ? "本批关卡已完成！" : won ? "搞定了！" : "挑战失败")
            : (batchComplete ? "All Levels Complete!" : won ? "Well Done!" : "Try Again!");
        description.text = chinese
            ? (won ? "小动物都已就位！" : "还差 " + missing + " 只没找到")
            : (won ? "All animals are in place." : missing + " animals still to find.");
        if (won && nextLevel != null)
            buttonLabel.text = chinese ? "下一关：" + GetLevelName(nextLevel.Id, true)
                : "Next: " + GetLevelName(nextLevel.Id, false);
        else
            buttonLabel.text = chinese ? (won ? "重玩" : "重试") + levelName
                : (won ? "Replay " : "Retry ") + levelName;
        title.color = won ? new Color(1f, .82f, .32f, 1f) : new Color(1f, .65f, .70f, 1f);

        // 复用已有动物素材；当前圆形仅标明动物展示区，不伪装为跳舞动画。
        bool placeholder = animalSprite == null || animalSprite.name == "RuntimeCirclePlaceholder";
        animalImage.sprite = animalSprite;
        animalImage.color = placeholder
            ? (won ? new Color(1f, .86f, .60f, 1f) : new Color(.68f, .72f, .85f, 1f))
            : animalColor;
        placeholderLabel.text = chinese ? "动物占位" : "Animal\nplaceholder";
        placeholderLabel.gameObject.SetActive(placeholder);

        Color accent = won ? new Color(1f, .56f, .13f, 1f) : new Color(.79f, .35f, .48f, 1f);
        ColorBlock colors = replayButton.colors;
        colors.normalColor = accent;
        colors.highlightedColor = accent * 1.06f;
        colors.highlightedColor = new Color(colors.highlightedColor.r, colors.highlightedColor.g,
            colors.highlightedColor.b, 1f);
        colors.pressedColor = new Color(accent.r * .80f, accent.g * .80f, accent.b * .80f, 1f);
        colors.selectedColor = accent;
        colors.disabledColor = new Color(accent.r, accent.g, accent.b, .55f);
        replayButton.colors = colors;

        onReplay = replayHandler;
        actionInProgress = false;
        isShown = true;
        gameObject.SetActive(true);
        overlay.SetAsLastSibling();
        replayButton.interactable = false;
        entering = true;
        entryTime = 0f;
        ApplyEntry();
        RefreshLayout(true);
        ResultAppeared?.Invoke(won);
    }

    private void BeginPuppyVictory(int remainingLives, string nextId, bool batchComplete, bool fromBoard)
    {
        if (!isShown || !isActiveAndEnabled || puppyVictory == null) return;
        showingBoardVictory = false;
        showingPuppyVictory = true;
        // Preserve the current dimming when crossing into the deeper victory
        // overlay: never flash back to an undimmed board between the two phases.
        victoryOverlayStartAlpha = fromBoard ? PreludeDimmer / VictoryDimmer : 0f;
        overlayGroup.alpha = victoryOverlayStartAlpha;
        dimmerImage.color = new Color(18f / 255f, 20f / 255f, 37f / 255f, VictoryDimmer);
        puppyVictory.Show(remainingLives, nextId, batchComplete, HandleReplayPressed);
        ResultAppeared?.Invoke(true);
    }

    public void Hide()
    {
        if (puppyFailure != null) puppyFailure.Hide();
        showingPuppyFailure = false;
        if (boardVictory != null) boardVictory.Cancel();
        showingBoardVictory = false;
        if (puppyVictory != null) puppyVictory.Hide();
        showingPuppyVictory = false;
        entering = false;
        onReplay = null;
        isShown = false;
        actionInProgress = false;
        if (replayButton != null) replayButton.interactable = false;
        gameObject.SetActive(false);
    }

    private void HandleReplayPressed()
    {
        if (!isShown || !isActiveAndEnabled || actionInProgress || onReplay == null)
            return;
        actionInProgress = true;
        replayButton.interactable = false;
        if (showingPuppyVictory && puppyVictory != null) puppyVictory.SetInteractable(false);
        if (showingPuppyFailure && puppyFailure != null) puppyFailure.SetInteractable(false);
        Action callback = onReplay;
        try
        {
            // 由控制器切到下一关或重试本关；这里只发出一次请求。
            callback();
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            if (isShown)
            {
                actionInProgress = false;
                replayButton.interactable = true;
                if (showingPuppyVictory && puppyVictory != null) puppyVictory.SetInteractable(true);
                if (showingPuppyFailure && puppyFailure != null) puppyFailure.SetInteractable(true);
            }
        }
    }

    private void OnDisable()
    {
        if (puppyFailure != null) puppyFailure.Hide();
        showingPuppyFailure = false;
        if (boardVictory != null) boardVictory.Cancel();
        showingBoardVictory = false;
        if (puppyVictory != null) puppyVictory.Hide();
        showingPuppyVictory = false;
        entering = false;
        onReplay = null;
        isShown = false;
        actionInProgress = false;
    }

    private void LateUpdate()
    {
        if (showingPuppyFailure)
        {
            if (safeArea != null) safeArea.RefreshNow();
            if (puppyFailure != null) overlayGroup.alpha = puppyFailure.EntranceAlpha;
            return;
        }
        if (showingBoardVictory)
        {
            if (safeArea != null) safeArea.RefreshNow();
            float fade = Mathf.SmoothStep(0f, 1f, boardVictory.Progress / .15f);
            dimmerImage.color = new Color(18f / 255f, 20f / 255f, 37f / 255f, PreludeDimmer * fade);
            return;
        }
        if (showingPuppyVictory)
        {
            if (safeArea != null) safeArea.RefreshNow();
            if (puppyVictory != null) overlayGroup.alpha =
                Mathf.Lerp(victoryOverlayStartAlpha, 1f, puppyVictory.EntranceAlpha);
            return;
        }
        RefreshLayout(false);
        if (!entering || !isShown) return;
        entryTime += Time.unscaledDeltaTime;
        ApplyEntry();
        if (entryTime >= .48f) FinishEntry();
    }

    private void ApplyEntry()
    {
        overlayGroup.alpha = Mathf.Clamp01(entryTime / .16f);
        content.localScale = Vector3.one * Mathf.SmoothStep(.94f, 1f, Mathf.Clamp01(entryTime / .32f));
        for (int i = 0; i < entryGroups.Length; i++)
            entryGroups[i].alpha = Mathf.Clamp01((entryTime - i * .075f) / .18f);
    }
    private void FinishEntry()
    {
        if (showingPuppyVictory || showingBoardVictory || showingPuppyFailure) return;
        entering = false;
        if (overlayGroup != null) overlayGroup.alpha = 1f;
        if (content != null) content.localScale = Vector3.one;
        if (entryGroups != null) foreach (CanvasGroup group in entryGroups) group.alpha = 1f;
        if (replayButton != null) replayButton.interactable = isShown && !actionInProgress;
    }
    private void OnApplicationFocus(bool value) { if (!value) FinishEntry(); }
    private void OnApplicationPause(bool value) { if (value) FinishEntry(); }

    private void RefreshLayout(bool force)
    {
        if (overlay == null || content == null || title == null || safeArea == null ||
            safeRect == null || !safeArea.RefreshNow()) return;
        Vector2 size = safeRect.rect.size;
        if (!force && size == lastSize) return;
        if (size.x <= 0f || size.y <= 0f) return;
        float height = Mathf.Min(1400f, size.y * .82f);
        // 短屏限制内容宽高比，防止字体按宽屏放大而挤出文案/按钮。
        float width = Mathf.Min(900f, size.x * .86f, height * .90f);
        content.sizeDelta = new Vector2(width, height);

        Place(title.rectTransform, width, height * .12f, 0f, height * .37f);
        Place(animalImage.rectTransform,
            Mathf.Min(width * .50f, height * .33f),
            Mathf.Min(width * .50f, height * .33f), 0f, height * .10f);
        Place(description.rectTransform, width * .92f, height * .12f, 0f, -height * .22f);
        Place(replayButton.GetComponent<RectTransform>(), width * .86f,
            height * .08f, 0f, -height * .38f);
        SetFontSize(title, width * .085f);
        SetFontSize(description, width * .050f);
        SetFontSize(buttonLabel, width * .060f);
        SetFontSize(placeholderLabel, width * .043f);
        lastSize = size;
    }

    private TextMeshProUGUI MakeText(string name, Transform parent)
    {
        RectTransform rect = MakeRect(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.raycastTarget = false;
        return text;
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rect, float width, float height, float x, float y)
    {
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    private static void SetFontSize(TMP_Text text, float maximum)
    {
        text.fontSize = maximum;
        text.fontSizeMax = maximum;
        text.fontSizeMin = Mathf.Max(12f, maximum * .60f);
    }

    private static string GetLevelName(string id, bool chinese)
    {
        string value = string.IsNullOrEmpty(id) ? "1" : id;
        int split = value.LastIndexOf('_');
        string suffix = split < 0 ? value : value.Substring(split + 1);
        if (int.TryParse(suffix, out int number) && number > 0)
            value = number.ToString();
        return chinese ? "第 " + value + " 关" : "Level " + value;
    }
}
