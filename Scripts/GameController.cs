using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class GameController : MonoBehaviour
{
    // Android players default to 30 fps, which makes every animation look choppy on phones.
    // Follow the screen instead: the highest refresh rate the display offers (60/90/120 Hz),
    // never below 60. vSync off so this target is what paces the game.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void UseSmoothFrameRate()
    {
        double hz = Screen.currentResolution.refreshRateRatio.value;
        foreach (Resolution mode in Screen.resolutions)
            if (mode.refreshRateRatio.value > hz) hz = mode.refreshRateRatio.value;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mathf.Clamp(Mathf.RoundToInt((float)hz), 60, 144);
    }

    [SerializeField] private BoardBuilder boardBuilder;
    [SerializeField] private bool logStateChanges = true;
    [SerializeField] private LevelData level;
    [Header("Level Loading（关卡加载）")]
    [SerializeField] private LevelCatalog catalog;
    [System.NonSerialized] private int currentLevelIndex = -1;
    [System.NonSerialized] private bool isTransitioning;
    [System.NonSerialized] private double activeSeconds;
    [System.NonSerialized] private int mistakeCount;
    [System.NonSerialized] private bool runResultRecorded;
    [System.NonSerialized] private bool appPaused;
    [System.NonSerialized] private bool appFocused = true;
    [System.NonSerialized] private bool skipClockFrame;
    [System.NonSerialized] private LevelRunResult lastRunResult;

    // 后续自适应难度只订阅结算快照，不介入正在玩的棋盘。
    public event System.Action<LevelRunResult> RunCompleted;
    public LevelRunResult LastRunResult => lastRunResult;
    public int CurrentLevelNumber => currentLevelIndex >= 0 ? currentLevelIndex + 1 : 1;

    [System.NonSerialized] private SaveService saveService;
    [System.NonSerialized] private bool saveWritesBlocked;
    [System.NonSerialized] private bool isRestoring;
    [System.NonSerialized] private bool saveDirty;
    [System.NonSerialized] private bool saveFailureLogged;
    [System.NonSerialized] private double queuedSaveAt;
    [System.NonSerialized] private double nextClockSaveAt;
    [System.NonSerialized] private string currentLevelFingerprint;
    [System.NonSerialized] private string saveNotice;
    private readonly HashSet<string> completedLevelIds = new HashSet<string>(System.StringComparer.Ordinal);
    private readonly HashSet<string> unlockedLevelIds = new HashSet<string>(System.StringComparer.Ordinal);

    public bool IsLevelCompleted(string id) => completedLevelIds.Contains(id);
    public bool IsLevelUnlocked(string id) => unlockedLevelIds.Contains(id);

    private BoardState boardState;
    [System.NonSerialized] private LineCompletionTracker lineCompletionTracker;
    private readonly List<LineCompletionInfo> lineCompletionBuffer = new List<LineCompletionInfo>(2);

    // 表现层订阅：通知不生成叉、不扣命、不改变通关规则。
    public event System.Action<LineCompletionInfo> LineCompleted;
    private CellView[] views;
    private CellInput[] inputs;
    // 运行时创建的 Canvas HUD；对象引用保留给停用/热重载时隐藏，不另存玩法数据。
    private HudView hudView;
    private ResultPanelView resultPanel;
    private GameScreenLayout screenLayout;
    private LineCompletionFeedback lineFeedback;
    private AnimalScorePopupView scoreFeedback;
    private CorrectPlacementFeedback correctFeedback;
    private RuleConflictFeedback ruleFeedback;
    private BoardState deferredFailure;
    [Header("Audio（音频；为空时加载默认Resources配置）")]
    [SerializeField] private AudioLibrary audioLibrary;
    private AudioController gameAudio;
    private SettingsPanelView settingsPanel;
    [System.NonSerialized] private bool settingsOpen;
    [Header("Props（测试阶段道具：不限次数）")]
    [SerializeField] private TMPro.TMP_FontAsset propFont;
    private PropBarView propBar;
    private HintPanelView hintPanel;
    [System.NonSerialized] private HintStep pendingHint;
    private BoardState hintBoard;
    private int[] hintCells;
    [System.NonSerialized] private bool propActionInProgress;

    // 重新编译时不能只恢复“已就绪”，却丢失棋盘数据。
    [System.NonSerialized] private bool isReady;

    [System.NonSerialized] private bool isDragMarking;
    private int dragPointerId;
    private Vector2 previousDragPosition;

    private bool ownsRuntimeLevel;
    private bool avatarIdleAllowed;
    private string startupError;

    [System.NonSerialized] private GUIStyle hudStyle;

    // First-run rules tutorial (fixed 4×4 board). While it runs: no saving, HUD/props/settings hidden.
    [System.NonSerialized] private bool tutorialMode;
    private TutorialGuide tutorialGuide;
    private LevelData tutorialLevel;

    private bool HasRuntimeState =>
        isReady &&
        boardState != null &&
        lineCompletionTracker != null &&
        level != null &&
        views != null &&
        inputs != null &&
        views.Length == boardState.CellCount &&
        inputs.Length == boardState.CellCount;

    private void Start()
    {
        isReady = false;
        startupError = null;
        hudStyle = null;
        appFocused = Application.isFocused;
        gameAudio = AudioController.Create(transform, audioLibrary);
        if (boardBuilder == null || !boardBuilder.isActiveAndEnabled)
        {
            StopWithError("请关联启用的新 Board 对象。");
            return;
        }

        // 默认目录是已保存的资源，不在每次启动时生成新题。
        if (catalog == null) catalog = Resources.Load<LevelCatalog>("LevelCatalog");
        LevelData first;
        if (catalog != null)
        {
            if (!catalog.ValidateIndex(out string catalogError))
            {
                StopWithError(catalogError);
                return;
            }
            first = catalog.GetAt(0);
        }
        else
        {
            // 保留无目录的旧单关兼容入口；有目录时不使用固定工厂。
            if (level == null)
            {
                level = FirstLevelFactory.Create();
                ownsRuntimeLevel = true;
            }
            first = level;
        }

        RestoredChallenge restored = null;
        try
        {
            // The copy project shares productName with the original, so keep its saves in their
            // own folder: the new levels never collide with (or block) the original project's file.
            saveService = new SaveService(System.IO.Path.Combine(Application.persistentDataPath, "CopyLevelsV3"));
            SaveLoadStatus status = saveService.Load(
                (SaveData candidate, out string validationError) =>
                    SaveValidator.TryRestore(candidate, catalog, first, out restored, out validationError),
                out SaveData saved, out string loadMessage);
            if (status == SaveLoadStatus.Loaded || status == SaveLoadStatus.Recovered)
            {
                foreach (string id in saved.completedLevelIds) completedLevelIds.Add(id);
                foreach (string id in saved.unlockedLevelIds) unlockedLevelIds.Add(id);
                if (status == SaveLoadStatus.Recovered)
                    Debug.LogWarning("GameController：" + loadMessage, this);
            }
            else if (status == SaveLoadStatus.Blocked)
            {
                restored = null;
                saveWritesBlocked = true;
                saveNotice = "Save unavailable. Original files kept; this session is not being saved.";
                Debug.LogWarning("GameController：" + loadMessage, this);
            }
            Debug.Log("GameController：本地存档路径：" + saveService.PrimaryPath, this);
        }
        catch (System.Exception exception)
        {
            saveWritesBlocked = true;
            saveNotice = "Save unavailable. This session is not being saved.";
            Debug.LogWarning("GameController：本地存档初始化失败：" + exception.Message, this);
        }

        screenLayout = GameScreenLayout.Create(boardBuilder.GetComponent<RectTransform>());
        if (screenLayout == null)
        {
            StopWithError("安全区布局创建失败，请检查画布与上一条错误。");
            return;
        }
        screenLayout.LayoutChanging += CancelGesturesForLayoutChange;
        LevelData initial = restored != null ? restored.Level : first;
        int initialIndex = restored != null ? restored.CatalogIndex : catalog != null ? 0 : -1;
        // Players who already have progress never see the tutorial.
        if (restored != null) TutorialGuide.MarkCompleted();
        else if (!TutorialGuide.Completed)
        {
            tutorialMode = true;
            initial = TutorialLevel();
            initialIndex = -1;
        }
        if (!TryBeginLevel(initial, initialIndex))
        {
            if (string.IsNullOrEmpty(startupError)) StopWithError("关卡加载失败，请检查上一条错误。");
            return;
        }
        settingsPanel = SettingsPanelView.Create(boardBuilder.GetComponent<RectTransform>(),
            propBar != null ? propBar.Font : propFont, gameAudio, OnSettingsChanged, RestartLevel, OnSettingToggled);
        if (settingsPanel == null) Debug.LogWarning("GameController：设置面板创建失败，其他玩法继续。", this);
        else
        {
            settingsPanel.RulesAutoOpened += PlayLevelStartSound;
            // The first level is built before this panel exists, so set it here as well.
            settingsPanel.SetRulesPersistent(currentLevelIndex >= 0 && currentLevelIndex < 10);
            settingsPanel.BindHud(hudView);
        }
        if (tutorialMode) BeginTutorialGuide();
        ApplyChromeVisibility();
        // Opening loading screen over the ready game; the level's rules popup is re-shown after it.
        DogFrameLibrary.Preload(this); // stream the result-puppy clips while the loading screen plays
        LoadingScreenView.Show(boardBuilder.GetComponentInParent<Canvas>()?.rootCanvas.transform,
            propBar != null ? propBar.Font : propFont,
            () => { if (!tutorialMode && settingsPanel != null && (!HasRuntimeState || !boardState.IsFinished)) settingsPanel.ShowOpeningRules(); });
        // Temporary jump tool: editor and Development Builds only, never in a release build.
        // Hidden developer tools: invisible until unlocked (10 taps on the Settings title + code).
        DevLevelJumper.Create(this, CatalogCount);
        if (restored != null) RestoreChallenge(restored);
        SaveNow(); // 新建挑战或从备份恢复后，建立/修复主存档；不覆盖被阻止的旧文件。
    }

    private bool TryBeginLevel(LevelData target, int sequenceIndex)
    {
        if (isTransitioning) return false;
        // 先验证下一关。坏题不销毁当前棋盘，也不推进目录索引。
        if (!LevelValidator.Validate(target, out string error))
        {
            Debug.LogError("GameController：关卡加载拒绝：" + error, this);
            return false;
        }

        isTransitioning = true;
        try
        {
            ClearHint();
            ResetVisualFeedback();
            ReleaseLineFeedback();
            ReleaseScoreFeedback();
            ReleaseCorrectFeedback();
            ReleaseRuleFeedback();
            deferredFailure = null;
            if (propBar != null) propBar.gameObject.SetActive(false);
            SetCellInputsEnabled(false);
            isDragMarking = false;
            isReady = false;
            if (hudView != null) hudView.gameObject.SetActive(false);
            if (resultPanel != null) resultPanel.Hide();

            if (!boardBuilder.TryLoadLevel(target, out CellView[] builtViews,
                    out CellInput[] builtInputs))
            {
                StopWithError("棋盘加载失败；没有推进关卡，请检查上一条错误。");
                return false;
            }
            int expected = target.Size * target.Size;
            if (builtViews == null || builtInputs == null ||
                builtViews.Length != expected || builtInputs.Length != expected)
            {
                StopWithError("生成的格子数量不正确。");
                return false;
            }
            for (int i = 0; i < expected; i++)
                if (builtViews[i] == null || builtInputs[i] == null ||
                    !builtViews[i].isActiveAndEnabled || !builtInputs[i].isActiveAndEnabled ||
                    builtViews[i].Index != i)
                {
                    StopWithError($"新格子 {i} 的组件或编号不正确。");
                    return false;
                }

            HudView newHud = HudView.Create(boardBuilder.GetComponent<RectTransform>(), target);
            if (newHud == null)
            {
                StopWithError("棋盘上方界面创建失败，请检查上一条错误。");
                return false;
            }
            if (hudView != null) Destroy(hudView.gameObject);
            hudView = newHud;
            if (settingsPanel != null) settingsPanel.BindHud(hudView);
            if (resultPanel == null)
            {
                resultPanel = ResultPanelView.Create(boardBuilder.GetComponent<RectTransform>());
                if (resultPanel != null) resultPanel.ResultAppeared += OnResultAppeared;
            }
            if (resultPanel == null)
            {
                StopWithError("结算界面创建失败，请检查上一条错误。");
                return false;
            }

            level = target;
            currentLevelIndex = sequenceIndex;
            // Levels 1-10 keep the rules strip up until the player taps "!"; later ones auto-hide.
            if (settingsPanel != null) settingsPanel.SetRulesPersistent(sequenceIndex >= 0 && sequenceIndex < 10);
            views = builtViews;
            inputs = builtInputs;
            boardState = new BoardState(level.Size);
            lineCompletionTracker = new LineCompletionTracker(level.Size);
            for (int i = 0; i < expected; i++)
            {
                views[i].Render(CellState.Empty);
                inputs[i].Initialize(i, HandleSingleClick, HandleDoubleClick,
                    PreviewSingleClick, RestoreStableView, BeginDragMarking,
                    ContinueDragMarking, EndDragMarking, HandleCellPressFeedback);
            }
            PlaceGivenAnimals();
            scoreFeedback = AnimalScorePopupView.Create(boardBuilder.GetComponent<RectTransform>(),
                hudView.Font, hudView.ScoreDestination);
            if (scoreFeedback == null)
                Debug.LogWarning("GameController：加分弹字创建失败，保留稳定本关分数与正常玩法。", this);
            // Prop buttons keep their round background independently of the new dog head.
            Sprite propCircle = CellView.GetPropButtonSprite();
            PropBarView newBar = PropBarView.Create(boardBuilder.GetComponent<RectTransform>(),
                propFont, propCircle, RevealAnimal, ShowLogicalHint, AddRandomCrosses);
            if (newBar == null)
            {
                StopWithError("下方道具栏创建失败，请检查字体与动物占位图。");
                return false;
            }
            if (propBar != null) Destroy(propBar.gameObject);
            propBar = newBar;
            // The hint overlay lives on the canvas root (not inside the prop bar), so replace it per level.
            if (hintPanel != null) Destroy(hintPanel.gameObject);
            hintPanel = HintPanelView.Create(propBar);
            if (hintPanel == null)
            {
                StopWithError("提示说明区创建失败。");
                return false;
            }
            // 只订阅表现层；缺字体或创建失败不改变已有玩法。
            lineFeedback = LineCompletionFeedback.Create(boardBuilder.GetComponent<RectTransform>(),
                views, level.Size, propFont, CanDisplayLineFeedback,
                () => scoreFeedback != null && scoreFeedback.IsPlaying);
            if (lineFeedback != null)
                LineCompleted += lineFeedback.Enqueue;
            else
                Debug.LogWarning("GameController：扫线表现创建失败，保留行列检测与正常玩法。", this);
            correctFeedback = CorrectPlacementFeedback.Create(boardBuilder.GetComponent<RectTransform>(), views, level);
            if (correctFeedback != null) correctFeedback.BindCompletionState(boardState);
            // Same-operation completions: row/column first, then the colour.
            // New lines arriving during a colour wave wait for its scaling to finish.
            if (correctFeedback != null)
                correctFeedback.BindLineSequence(() => Mathf.Max(ruleFeedback != null ? ruleFeedback.Remaining : 0f,
                    lineFeedback != null ? lineFeedback.RegionStartDelay : 0f));
            if (lineFeedback != null)
                lineFeedback.BindRegionSequence(() => (ruleFeedback == null || !ruleFeedback.IsPlaying) &&
                    (correctFeedback == null || correctFeedback.RegionMotionRemaining <= 0f));
            if (correctFeedback == null)
                Debug.LogWarning("GameController：区域金光创建失败，保留正常放置与计分。", this);
            ruleFeedback = RuleConflictFeedback.Create(boardBuilder.GetComponent<RectTransform>(), views, level, screenLayout);
            BeginRoundRecord();
            ApplyClarity();
            currentLevelFingerprint = SaveValidator.Fingerprint(level);
            if (!TutorialGuide.IsTutorialLevel(level)) unlockedLevelIds.Add(level.Id);
            startupError = null;
            isReady = true;
            RefreshHud();
            SyncAvatarIdle(true);
            ApplyChromeVisibility();
            Debug.Log($"GameController：已加载 {level.Id}，{level.Size}×{level.Size}，3 条命；关卡规则与唯一解检查通过。", this);
            return true;
        }
        finally
        {
            isTransitioning = false;
        }
    }

    public void LoadNextLevel()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !HasRuntimeState ||
            isTransitioning || !boardState.IsWon || catalog == null)
            return;
        LevelData next = catalog.GetNext(currentLevelIndex);
        if (next == null) return; // 最后一关不越界，也不假装有下一关。
        if (TryBeginLevel(next, currentLevelIndex + 1))
            SaveNow();
        else if (HasRuntimeState)
            ShowResultPanel(); // 验证拒绝时恢复旧结算按钮，不丢失当前结果。
    }

    private void RestoreChallenge(RestoredChallenge saved)
    {
        isRestoring = true;
        levelStartSoundPending = false; // resuming mid-game is not a fresh start
        introPending = introPlaying = false; SetCellScales(1f);
        ResetVisualFeedback(); // 读档不重播旧通知或旧动画。
        try
        {
            SetCellInputsEnabled(false);
            boardState = saved.Board;
            if (correctFeedback != null) correctFeedback.BindCompletionState(boardState);
            lineCompletionTracker = saved.Lines;
            activeSeconds = saved.Data.activeSeconds;
            mistakeCount = saved.Data.mistakes;
            runResultRecorded = saved.Data.runResultRecorded;
            lastRunResult = runResultRecorded
                ? new LevelRunResult(level, boardState.IsWon, activeSeconds, mistakeCount) : null;
            foreach (CellView view in views)
                view.Render(boardState.GetCellState(view.Index));
            skipClockFrame = true;
            RefreshHud();
            SetCellInputsEnabled(!boardState.IsFinished && appFocused && !appPaused && !settingsOpen);
            ShowResultPanel(); // 恢复胜利/失败面板，不再派发通关、扣命或行列完成事件。
            Debug.Log($"GameController：已恢复 {level.Id}，剩余 {boardState.RemainingLives} 条命，已找到 {boardState.PlacedCount}/{level.Size}。", this);
        }
        finally { isRestoring = false; }
    }

    private void RequestSave()
    {
        if (!HasRuntimeState || isRestoring || saveWritesBlocked || tutorialMode) return;
        if (!saveDirty) queuedSaveAt = Time.unscaledTimeAsDouble + .20d;
        saveDirty = true; // 同一次快速划动的多格变更合并写入，不每格刷磁盘。
    }

    private void LateUpdate()
    {
        if (propBar != null) propBar.SetAvailable(CanUseProps);
        if (!Application.isPlaying || !HasRuntimeState || isTransitioning || isRestoring) return;
        double now = Time.unscaledTimeAsDouble;
        if ((saveDirty && now >= queuedSaveAt) ||
            (!boardState.IsFinished && !appPaused && appFocused && now >= nextClockSaveAt))
            SaveNow();
    }

    private void SaveNow()
    {
        if (!Application.isPlaying || !HasRuntimeState || isTransitioning || isRestoring ||
            saveService == null || saveWritesBlocked || tutorialMode ||
            (boardState.IsFinished && !runResultRecorded))
            return;

        string[] completed = new string[completedLevelIds.Count];
        completedLevelIds.CopyTo(completed);
        System.Array.Sort(completed, System.StringComparer.Ordinal);
        string[] unlocked = new string[unlockedLevelIds.Count];
        unlockedLevelIds.CopyTo(unlocked);
        System.Array.Sort(unlocked, System.StringComparer.Ordinal);
        var snapshot = new SaveData {
            version = 1,
            currentLevelId = level.Id,
            levelDataVersion = level.DataVersion,
            levelFingerprint = currentLevelFingerprint,
            boardSize = level.Size,
            cells = boardState.ExportCells(),
            remainingLives = boardState.RemainingLives,
            reportedRows = lineCompletionTracker.ExportRows(),
            reportedColumns = lineCompletionTracker.ExportColumns(),
            completedLevelIds = completed,
            unlockedLevelIds = unlocked,
            activeSeconds = activeSeconds,
            mistakes = mistakeCount,
            outcome = boardState.IsWon ? "Won" : boardState.IsFailed ? "Failed" : "Playing",
            runResultRecorded = runResultRecorded
        };
        nextClockSaveAt = Time.unscaledTimeAsDouble + 5d;
        if (saveService.Save(snapshot, out string error))
        {
            saveDirty = false;
            saveFailureLogged = false;
            saveNotice = null;
        }
        else
        {
            saveDirty = true;
            queuedSaveAt = nextClockSaveAt;
            saveNotice = "Saving failed. Progress may be lost.";
            if (!saveFailureLogged)
                Debug.LogWarning("GameController：存档写入失败，原存档/备份保留：" + error, this);
            saveFailureLogged = true;
        }
    }

    // Consecutive correct placements; any mistake starts the praise/chime ladder over.
    [System.NonSerialized] private int correctStreak;

    // The do-re-mi start run plays with the level's opening rules pop-up, once per fresh round
    // (new level or retry) — never under the loading screen and never for a restored game.
    [System.NonSerialized] private bool levelStartSoundPending;
    private void PlayLevelStartSound()
    {
        if (!levelStartSoundPending || LoadingScreenView.IsShowing) return;
        levelStartSoundPending = false;
        gameAudio?.PlayExtra("GameSfx/level_start");
    }

    // Opening of a fresh round: the tiles pop in on a diagonal wave from the bottom-left corner to
    // the top-right, each overshooting a little and springing back, with the do-re-mi start run.
    // Waits for the loading screen; a restored mid-game board simply appears.
    private const float IntroStep = .045f, IntroCellTime = .42f;
    [System.NonSerialized] private bool introPending, introPlaying;
    [System.NonSerialized] private float introTime;

    private void SetCellScales(float scale)
    {
        if (views == null) return;
        foreach (CellView view in views) if (view != null) view.transform.localScale = Vector3.one * scale;
    }

    private void TickBoardIntro()
    {
        if (introPending && !introPlaying)
        {
            if (!HasRuntimeState || views == null || LoadingScreenView.IsShowing) return;
            introPending = false; introPlaying = true; introTime = 0f;
            PlayLevelStartSound();
        }
        if (!introPlaying) return;
        if (views == null) { introPlaying = false; return; }
        introTime += Mathf.Min(Time.unscaledDeltaTime, .05f);
        int n = level != null ? level.Size : Mathf.RoundToInt(Mathf.Sqrt(views.Length));
        bool done = true;
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] == null) continue;
            int diagonal = (n - 1 - i / n) + i % n;               // 0 at bottom-left
            float p = Mathf.Clamp01((introTime - diagonal * IntroStep) / IntroCellTime);
            if (p < 1f) done = false;
            // ease-out-back: grows past full size (~1.1) and settles back, a jelly pop
            float q = p - 1f, s = p <= 0f ? 0f : 1f + 2.70158f * q * q * q + 1.70158f * q * q;
            views[i].transform.localScale = Vector3.one * Mathf.Max(0f, s);
        }
        if (done) { introPlaying = false; SetCellScales(1f); }
    }

    private void BeginRoundRecord()
    {
        levelStartSoundPending = true;
        introPending = true; introPlaying = false;
        SetCellScales(0f); // hidden until the intro wave reaches them
        resultJinglePending = false;
        activeSeconds = 0d;
        mistakeCount = 0;
        correctStreak = 0;
        runResultRecorded = false;
        skipClockFrame = true;
    }

    private void Update()
    {
        TickBoardIntro();
        // A fatal error is saved/recorded immediately, but let its two red flashes
        // finish before opening the failure modal. Cancellation/focus loss cannot strand it.
        if (deferredFailure != null)
        {
            if (!HasRuntimeState || boardState != deferredFailure || !boardState.IsFailed) deferredFailure = null;
            else if (appFocused && !appPaused && !settingsOpen && (ruleFeedback == null || !ruleFeedback.IsPlaying))
            { deferredFailure = null; ShowResultPanel(); }
        }
        SyncAvatarIdle();
        // 从本局准备好起算，包括前台思考时间；结算/失焦/后台/控制器停用不计。
        if (skipClockFrame)
        {
            skipClockFrame = false;
            return; // 不把加载或恢复前台那一帧的大 delta 算作解题时间。
        }
        if (Application.isPlaying && HasRuntimeState && !boardState.IsFinished &&
            !isTransitioning && !appPaused && appFocused && !settingsOpen)
            activeSeconds += Time.unscaledDeltaTime;
    }

    private void SyncAvatarIdle(bool force = false)
    {
        bool allowed = Application.isPlaying && isActiveAndEnabled && HasRuntimeState &&
            !boardState.IsFinished && !isTransitioning && !appPaused && appFocused && !settingsOpen;
        if (!force && avatarIdleAllowed == allowed) return;
        avatarIdleAllowed = allowed;
        if (views != null)
            foreach (CellView view in views) if (view != null) view.SetAnimalIdleAllowed(allowed);
        if (hudView != null) hudView.SetAnimalIdleAllowed(allowed);
    }

    private void OnApplicationFocus(bool focused)
    {
        appFocused = focused;
        skipClockFrame = true;
        if (!focused)
        {
            ResetVisualFeedback();
            ClearHint();
            SetCellInputsEnabled(false); // 取消未提交的单双击和旧拖动，只保存稳定状态。
            SaveNow();
        }
        else if (HasRuntimeState && isActiveAndEnabled && !appPaused)
            SetCellInputsEnabled(!boardState.IsFinished && !settingsOpen);
    }

    private void OnApplicationPause(bool paused)
    {
        appPaused = paused;
        skipClockFrame = true;
        if (paused)
        {
            ResetVisualFeedback();
            ClearHint();
            SetCellInputsEnabled(false);
            SaveNow();
        }
        else if (HasRuntimeState && isActiveAndEnabled && appFocused)
            SetCellInputsEnabled(!boardState.IsFinished && !settingsOpen);
    }

    // Set when a round has just ended; consumed when the result puppy appears, so a restored
    // finished game shows its puppy silently.
    [System.NonSerialized] private bool resultJinglePending;
    private void OnResultAppeared(bool won)
    {
        if (!resultJinglePending) return;
        resultJinglePending = false;
        if (gameAudio == null) return;
        // ~3 s mallet jingles in Resources/GameSfx (win waltz / falling minor phrase);
        // fall back to the library clips if a file is missing.
        if (!gameAudio.PlayExtra(won ? "GameSfx/win_jingle" : "GameSfx/lose_jingle"))
            gameAudio.PlaySound(won ? GameSound.Win : GameSound.Lose);
        // No voice line on the result: a win is voiced by the announcer on the last animal, and
        // the old synthesized "再试一次" on failure was removed (the lose music says it alone).
    }

    private void RecordRunResult()
    {
        if (runResultRecorded || !HasRuntimeState || !boardState.IsFinished) return;
        runResultRecorded = true;
        BoardState completedState = boardState;
        lastRunResult = new LevelRunResult(level, boardState.IsWon, activeSeconds, mistakeCount);
        LevelRunResult snapshot = lastRunResult;
        if (boardState.IsWon)
        {
            completedLevelIds.Add(level.Id);
            unlockedLevelIds.Add(level.Id);
            LevelData next = catalog != null ? catalog.GetNext(currentLevelIndex) : null;
            if (next != null) unlockedLevelIds.Add(next.Id);
        }
        SaveNow(); // 先保存已结束状态与解锁，再派发结算，重启不重复发送。
        // The result music waits for the puppy to appear (after the board sweep / red flashes).
        resultJinglePending = true;
        Debug.Log($"GameController：结算记录 {snapshot.LevelId}，{(snapshot.Won ? "胜利" : "失败")}，有效用时 {snapshot.ActiveSeconds:F1} 秒，错误 {snapshot.Mistakes} 次（尚未自动调整难度）。", this);
        System.Action<LevelRunResult> handlers = RunCompleted;
        if (handlers == null) return;
        foreach (System.Action<LevelRunResult> handler in handlers.GetInvocationList())
        {
            if (boardState != completedState || !HasRuntimeState) break;
            try { handler(snapshot); }
            catch (System.Exception exception) { Debug.LogException(exception, this); }
        }
    }

    private void StopWithError(string message)
    {
        isReady = false;
        startupError = message;
        ResetVisualFeedback();
        if (lineFeedback != null) lineFeedback.gameObject.SetActive(false);
        ClearHint();
        if (propBar != null) propBar.gameObject.SetActive(false);
        if (hudView != null) hudView.gameObject.SetActive(false);
        if (resultPanel != null) resultPanel.Hide();

        SetCellInputsEnabled(false);

        Debug.LogError("GameController：" + message, this);
    }

    private bool CanHandleInput(int index)
    {
        return Application.isPlaying &&
               isActiveAndEnabled &&
               appFocused && !appPaused && !settingsOpen &&
               HasRuntimeState &&
               index >= 0 &&
               index < boardState.CellCount &&
               views[index] != null &&
               !boardState.IsFinished &&
               !boardState.IsCellLocked(index);
    }

    private void HandleCellPressFeedback(int index,bool pressed)
    {
        // Even an already-locked animal/cross counts as touching the board.
        // This is visual dismissal only; the existing pointer/click still proceeds.
        if (pressed && settingsPanel != null) settingsPanel.HideRules();
        // Soft 'jelly' press on every touch of the board while the round is live.
        if (pressed && boardState != null && !boardState.IsFinished)
        {
            gameAudio?.PlayPress();
            gameAudio?.TapHaptic(); // the game's only vibration: a light tick per board touch
        }
        if(views==null||index<0||index>=views.Length||views[index]==null)return;
        // Release is always accepted, including menu/open or input-disabled
        // cleanup. A locked animal/red cross still ignores new gameplay input.
        if(!pressed||CanHandleInput(index))views[index].SetPressFeedback(pressed);
    }

    // 普通单击立即改变画面，不等待双击间隔结束。
    private void PreviewSingleClick(int index)
    {
        if (!CanHandleInput(index) ||
            (tutorialGuide != null && !tutorialGuide.AllowTap(index, boardState.GetCellState(index))))
            return;

        ClearHint(); // 新操作使上一步提示失效，预览仍不写入存档。
        CellState previewState =
            boardState.GetCellState(index) == CellState.Empty
                ? CellState.Marked
                : CellState.Empty;

        views[index].RenderAnimated(previewState);
        // The pop already played on pointer-down; a new cross adds only a light scratch accent.
        if (previewState == CellState.Marked) gameAudio?.PlaySound(GameSound.MarkOn);
    }

    // 按棋盘数据恢复正式显示。
    private void RestoreStableView(int index)
    {
        if (boardState == null ||
            views == null ||
            index < 0 ||
            index >= boardState.CellCount ||
            index >= views.Length ||
            views[index] == null)
        {
            return;
        }

        views[index].Render(boardState.GetCellState(index));
    }

    private void HandleSingleClick(int index)
    {
        if (!CanHandleInput(index) ||
            (tutorialGuide != null && !tutorialGuide.AllowTap(index, boardState.GetCellState(index))) ||
            !boardState.TryToggleMark(index))
        {
            RestoreStableView(index);
            return;
        }
        tutorialGuide?.OnBoardChanged(boardState);

        ClearHint();
        RestoreStableView(index);

        if (logStateChanges)
        {
            Debug.Log(
                $"GameController：格子 {index} → {boardState.GetCellState(index)}。",
                this);
        }
        CheckLineCompletion(index);
        SaveNow();
    }


    // 滑动只添加普通 X，不使用单击的切换逻辑。
    // 起点可以是动物、红 X 或普通 X，整局结束后则不能继续。
    private bool CanDragMark =>
        Application.isPlaying && isActiveAndEnabled &&
        appFocused && !appPaused && !settingsOpen && HasRuntimeState && !boardState.IsFinished;

    private void BeginDragMarking(int startIndex, PointerEventData eventData)
    {
        if (!CanDragMark || eventData == null)
            return;

        ClearHint();
        // 撤回所有未完成的单击预览，避免滑动后又自动取消叉。
        foreach (CellInput input in inputs)
        {
            if (input != null)
                input.CancelPendingClick();
        }

        isDragMarking = true;
        dragPointerId = eventData.pointerId;
        previousDragPosition = eventData.pressPosition;
        AddDragMark(startIndex);
        ContinueDragMarking(eventData);
    }

    private void ContinueDragMarking(PointerEventData eventData)
    {
        if (!isDragMarking || !CanDragMark ||
            eventData == null || eventData.pointerId != dragPointerId)
            return;

        Vector2 current = eventData.position;
        // 检查两次输入采样之间的线段，不仅检查终点；
        // 快速跨过多个格子时也能补上途中经过的空格。
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] == null)
                continue;

            RectTransform rectTransform = views[i].transform as RectTransform;
            if (rectTransform == null)
                continue;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, previousDragPosition,
                    eventData.pressEventCamera, out Vector2 from) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, current,
                    eventData.pressEventCamera, out Vector2 to) &&
                SegmentIntersectsRect(from, to, rectTransform.rect))
            {
                AddDragMark(i);
            }
        }

        previousDragPosition = current;
    }

    private void EndDragMarking(PointerEventData eventData)
    {
        // null 表示失焦、停用或重试取消，不再补画最后一段。
        if (eventData != null)
            ContinueDragMarking(eventData);

        isDragMarking = false;
        SaveNow();
    }

    private void AddDragMark(int index)
    {
        if (!CanHandleInput(index) ||
            boardState.GetCellState(index) != CellState.Empty ||
            (tutorialGuide != null && !tutorialGuide.AllowDrag(index)))
            return;

        ClearHint();
        // 只有 Empty 才调用切换，所以 Marked 永远不会被滑动取消。
        if (boardState.TryToggleMark(index))
        {
            ShowNewCross(index);
            CheckLineCompletion(index);
            RequestSave();
            tutorialGuide?.OnBoardChanged(boardState);
        }
    }

    // 线段与格子矩形相交检测，包含转向、斜划与反向划。
    private static bool SegmentIntersectsRect(Vector2 from, Vector2 to, Rect rect)
    {
        if (float.IsNaN(from.x) || float.IsNaN(from.y) ||
            float.IsNaN(to.x) || float.IsNaN(to.y) ||
            float.IsInfinity(from.x) || float.IsInfinity(from.y) ||
            float.IsInfinity(to.x) || float.IsInfinity(to.y))
            return false;

        float dx = to.x - from.x;
        float dy = to.y - from.y;
        float enter = 0f;
        float exit = 1f;
        return ClipSegment(-dx, from.x - rect.xMin, ref enter, ref exit) &&
               ClipSegment(dx, rect.xMax - from.x, ref enter, ref exit) &&
               ClipSegment(-dy, from.y - rect.yMin, ref enter, ref exit) &&
               ClipSegment(dy, rect.yMax - from.y, ref enter, ref exit);
    }

    private static bool ClipSegment(
        float direction, float distance, ref float enter, ref float exit)
    {
        if (direction == 0f)
            return distance >= 0f;

        float fraction = distance / direction;
        if (direction < 0f)
        {
            if (fraction > exit) return false;
            if (fraction > enter) enter = fraction;
        }
        else
        {
            if (fraction < enter) return false;
            if (fraction < exit) exit = fraction;
        }

        return true;
    }

    private void HandleDoubleClick(int index)
    {
        if (!CanHandleInput(index))
            return;
        if (tutorialGuide != null && !tutorialGuide.AllowPlace(index))
        {
            RestoreStableView(index);
            tutorialGuide.OnRejectedPlace(index);
            return;
        }

        bool correct = false;

        foreach (int answer in level.SolutionIndices)
        {
            if (answer == index)
            {
                correct = true;
                break;
            }
        }

        ResolvePlacement(index, correct);
    }

    // 玩家双击与左道具共用落点、扣命、槽位、胜负及存档处理。
    private void ResolvePlacement(int index, bool correct)
    {
        if (!CanHandleInput(index)) return;
        ClearHint();
        BoardState changedBoard = boardState;
        int previousLives = boardState.RemainingLives;
        if (!boardState.TryResolvePlacement(index, correct))
            return;
        if (!correct) { mistakeCount++; correctStreak = 0; }
        else correctStreak++;

        RestoreStableView(index);
        RefreshHud(correct);
        if (correct) gameAudio?.PlayCorrectStep(correctStreak); // rises with each consecutive animal
        else gameAudio?.PlaySound(GameSound.Error);
        if (correct) gameAudio?.PlayAnnouncer(correctStreak, boardState.Size); // Nice ... Insane, restarts after a mistake
        if (correct) PlayPlacementFeedback(index); // 数据已经落定，动画不阻塞后续处理。
        else PlayErrorFeedback(index, previousLives);
        if (correct && scoreFeedback != null)
        {
            try { scoreFeedback.Play(views[index], boardState.PlacedCount - level.GivenCount, boardState.IsWon); }
            catch (System.Exception error)
            { Debug.LogWarning("GameController：跳过本次加分弹字，不改变稳定分数：" + error.Message, this); }
        }

        if (logStateChanges)
        {
            Debug.Log(
                $"GameController：格子 {index} → {(correct ? "正确动物并锁定" : "红 X 并锁定")}; 剩余 {boardState.RemainingLives} 条命。",
                this);
        }

        // 正确动物最后放入、红叉最后补齐，均会检查其行/列。
        // 本次变更即使同时结束挑战也可以发出完成通知，后续动画另定优先级。
        CheckLineCompletion(index);
        if (!HasRuntimeState || boardState != changedBoard) return;
        if (tutorialGuide != null)
        {
            // The tutorial ends on its own card, never on the victory/result flow or a save.
            if (boardState.IsFinished) SetCellInputsEnabled(false);
            tutorialGuide.OnBoardChanged(boardState);
            return;
        }

        if (!boardState.IsFinished)
        {
            SaveNow();
            return;
        }

        SetCellInputsEnabled(false);

        if (boardState.IsWon)
        {
            var placed = new List<int>();

            for (int i = 0; i < boardState.CellCount; i++)
            {
                if (boardState.GetCellState(i) == CellState.Placed)
                    placed.Add(i);
            }

            if (!LevelValidator.ValidateSolution(
                level.Size,
                level.RegionIds,
                placed,
                out string error))
            {
                StopWithError("通关复验失败：" + error);
                return;
            }
        }

        RecordRunResult();
        if (!HasRuntimeState || !boardState.IsFinished) return;
        ShowResultPanel(true);
        Debug.Log(
            boardState.IsWon
                ? $"GameController：{level.Id} 通关！"
                : "GameController：生命用完，本次挑战失败。",
            this);
    }

    private void CheckLineCompletion(int changedCellIndex)
    {
        if (!HasRuntimeState)
            return;

        // Every stable single/drag/prop/placement change already passes here.
        // Region completion is independent of whether a row/column also completes.
        if (!isRestoring && appFocused && !appPaused && !settingsOpen && correctFeedback != null)
        {
            try { correctFeedback.ObserveCompletion(boardState, changedCellIndex); }
            catch (System.Exception error)
            { Debug.LogWarning("GameController：跳过本次同色标满动画，不改变棋盘：" + error.Message, this); }
        }

        BoardState stateAtNotification = boardState;
        LineCompletionTracker trackerAtNotification = lineCompletionTracker;
        lineCompletionTracker.CollectNewCompletions(
            boardState, changedCellIndex, lineCompletionBuffer);
        if (lineCompletionBuffer.Count == 0)
            return;

        // Tracker has already recorded every completed line for save/restore.
        // A pet placed LAST only plays its own arrival, not a whole-line wave.
        // Completion celebrations require the pet to predate the final cross.
        CellState changedState = boardState.GetCellState(changedCellIndex);
        if (changedState != CellState.Marked && changedState != CellState.Rejected)
            return;

        // 一个操作可能同时完成行与列，固定先行后列，两条分别通知。
        // 快照避免后续表现订阅者重入/重试时改写正在派发的列表。
        LineCompletionInfo[] notifications = lineCompletionBuffer.ToArray();
        foreach (LineCompletionInfo completion in notifications)
        {
            if (!HasRuntimeState || boardState != stateAtNotification ||
                lineCompletionTracker != trackerAtNotification || !isActiveAndEnabled)
                break;

            if (logStateChanges)
            {
                string axis = completion.Axis == PuzzleLineAxis.Row ? "行" : "列";
                Debug.Log(
                    $"GameController：Good！第 {completion.LineIndex + 1} {axis}已标满（正确动物 + 普通叉/红叉），本次挑战只通知一次。",
                    this);
            }

            // 表现订阅者播放Good/波纹与音频，事件不修改玩法。
            System.Action<LineCompletionInfo> handlers = LineCompleted;
            if (handlers == null) continue;
            foreach (System.Action<LineCompletionInfo> handler in handlers.GetInvocationList())
            {
                if (!HasRuntimeState || boardState != stateAtNotification ||
                    lineCompletionTracker != trackerAtNotification || !isActiveAndEnabled)
                    break;
                try
                {
                    handler(completion);
                }
                catch (System.Exception error)
                {
                    // 表现层异常不能中断已有扣命/胜负处理。
                    Debug.LogException(error, this);
                }
            }
        }
    }

    private void ShowResultPanel(bool celebrateNewWin = false)
    {
        if (!HasRuntimeState || resultPanel == null || !boardState.IsFinished)
            return;
        if (!boardState.IsWon && ruleFeedback != null && ruleFeedback.IsPlaying)
        {
            deferredFailure = boardState;
            if (propBar != null) propBar.SetAvailable(false);
            return;
        }
        deferredFailure = null;
        if (boardState.IsWon)
        {
            // A fast final correct placement must not inherit an earlier error shake,
            // otherwise the victory geometry guard would cancel its own ceremony.
            if (ruleFeedback != null) ruleFeedback.Cancel();
            if (screenLayout != null) screenLayout.CancelErrorShake();
        }
        if (settingsPanel != null) settingsPanel.SetRulesAvailable(false);
        SyncAvatarIdle(true);

        ClearHint();
        if (!boardState.IsWon) ResetVisualFeedback(); // No raised puppy/gold burst above the failure modal.
        if (lineFeedback != null) lineFeedback.ResetFeedback(); // 结算优先，取消扫线/待播队列。
        if (propBar != null) propBar.SetAvailable(false);
        Sprite animalSprite = null;
        Color animalColor = Color.white;
        // 只复用展示素材，不读取关卡答案或改变动物位置。
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && boardState.GetCellState(i) == CellState.Placed &&
                views[i].TryGetAnimalAppearance(out animalSprite, out animalColor))
                break;
        }
        if (animalSprite == null && views.Length > 0 && views[0] != null)
            views[0].TryGetAnimalAppearance(out animalSprite, out animalColor);

        LevelData next = catalog != null && currentLevelIndex >= 0
            ? catalog.GetNext(currentLevelIndex) : null;
        bool batchComplete = boardState.IsWon && catalog != null && next == null;
        bool advance = boardState.IsWon && next != null;
        resultPanel.Show(boardState.IsWon, level, animalSprite, animalColor,
            advance ? LoadNextLevel : (System.Action)RestartLevel, next, batchComplete,
            boardState.RemainingLives, celebrateNewWin && boardState.IsWon ? views : null,
            boardState.Size - boardState.PlacedCount, hudView,
            celebrateNewWin && correctFeedback != null ? correctFeedback.Remaining : 0f);
    }

    public void RestartLevel()
    {
        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            !HasRuntimeState || isTransitioning)
        {
            return;
        }

        ResetVisualFeedback();
        ClearHint();
        SetCellInputsEnabled(false);
        if (resultPanel != null) resultPanel.Hide();
        deferredFailure = null;

        boardState = new BoardState(level.Size);
        if (correctFeedback != null) correctFeedback.BindCompletionState(boardState);
        lineCompletionTracker = new LineCompletionTracker(level.Size);
        PlaceGivenAnimals();

        for (int i = 0; i < views.Length; i++)
            RestoreStableView(i);

        BeginRoundRecord();
        SetCellInputsEnabled(appFocused && !appPaused && !settingsOpen);
        RefreshHud();
        if (settingsPanel != null) settingsPanel.ShowOpeningRules();

        SaveNow();
        Debug.Log(
            "GameController：已重试，标记和锁定清空，恢复 3 条命。",
            this);
    }

    private void CancelGesturesForLayoutChange()
    {
        ResetVisualFeedback();
        ClearHint();
        // 旋转/改变窗口时取消尚未提交的预览与按住手势，不把旧屏幕线段画到新位置。
        isDragMarking = false;
        SetCellInputsEnabled(false);
        if (HasRuntimeState && isActiveAndEnabled && appFocused && !appPaused && !settingsOpen)
            SetCellInputsEnabled(!boardState.IsFinished && !settingsOpen);
    }

    private void PlayPlacementFeedback(int index)
    {
        if (!HasRuntimeState || index < 0 || index >= views.Length ||
            boardState.GetCellState(index) != CellState.Placed) return;
        try
        {
            // 双击与揭晓道具只走这一处；读档/重绘不触发。
            if (views[index] != null) views[index].PlayPlacementFeedback();
            if (correctFeedback != null) correctFeedback.Play(index);
            if (hudView != null) hudView.PlayCollectionFeedback(level.RegionIds[index]);
        }
        catch (System.Exception exception)
        {
            // 视觉组件异常不能阻断本次生命、胜负、通知或存档。
            Debug.LogException(exception, this);
        }
    }

    private void PlayErrorFeedback(int index, int previousLives)
    {
        if (!HasRuntimeState || index < 0 || index >= views.Length ||
            boardState.GetCellState(index) != CellState.Rejected ||
            previousLives - boardState.RemainingLives != 1) return;
        try
        {
            // 初次有效错误才到这里；锁定格重复点、Render或读档均不重播。
            if (views[index] != null) views[index].PlayErrorFeedback();
            if (hudView != null)
                hudView.PlayLifeLossFeedback(previousLives, boardState.RemainingLives);
            if (lineFeedback != null) lineFeedback.ResetFeedback();
            if (correctFeedback != null) correctFeedback.ResetFeedback();
            if (ruleFeedback != null) ruleFeedback.Play(index, boardState);
            else if (screenLayout != null) screenLayout.PlayErrorShake();
        }
        catch (System.Exception exception)
        {
            // 视觉异常不能中断生命、永久锁定、失败判定或存档。
            Debug.LogException(exception, this);
        }
    }

    private void ResetVisualFeedback()
    {
        if (ruleFeedback != null) ruleFeedback.Cancel();
        if (screenLayout != null) screenLayout.CancelErrorShake();
        if (correctFeedback != null) correctFeedback.ResetFeedback();
        if (scoreFeedback != null) scoreFeedback.ResetFeedback();
        if (lineFeedback != null) lineFeedback.ResetFeedback();
        if (views != null)
            foreach (CellView view in views)
                if (view != null) view.ResetFeedback();
        if (hudView != null) hudView.ResetFeedback();
    }


    // 仅决定是否还适合显示反馈，不发通知/落子/扣命。已结束或重新打开的线不扫。
    private bool CanDisplayLineFeedback(LineCompletionInfo completion)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !HasRuntimeState ||
            isTransitioning || isRestoring || !appFocused || appPaused || settingsOpen ||
            boardState.IsFinished || completion.LineIndex < 0 ||
            completion.LineIndex >= boardState.Size ||
            completion.AnimalCellIndex < 0 || completion.AnimalCellIndex >= boardState.CellCount ||
            completion.ChangedCellIndex < 0 || completion.ChangedCellIndex >= boardState.CellCount)
            return false;
        bool row = completion.Axis == PuzzleLineAxis.Row;
        if (!row && completion.Axis != PuzzleLineAxis.Column) return false;
        int n = boardState.Size;
        if ((row ? completion.AnimalCellIndex / n : completion.AnimalCellIndex % n) != completion.LineIndex ||
            (row ? completion.ChangedCellIndex / n : completion.ChangedCellIndex % n) != completion.LineIndex)
            return false;
        int placed = 0;
        for (int offset = 0; offset < n; offset++)
        {
            int index = row ? completion.LineIndex * n + offset : offset * n + completion.LineIndex;
            CellState state = boardState.GetCellState(index);
            if (state == CellState.Placed)
            {
                if (index != completion.AnimalCellIndex) return false;
                placed++;
            }
            else if (state != CellState.Marked && state != CellState.Rejected) return false;
        }
        return placed == 1;
    }

    private void ReleaseLineFeedback()
    {
        if (lineFeedback == null) return;
        LineCompleted -= lineFeedback.Enqueue;
        lineFeedback.ResetFeedback();
        lineFeedback.gameObject.SetActive(false);
        Destroy(lineFeedback.gameObject);
        lineFeedback = null;
    }

    private void ReleaseScoreFeedback()
    {
        if (scoreFeedback == null) return;
        scoreFeedback.ResetFeedback();
        scoreFeedback.gameObject.SetActive(false);
        Destroy(scoreFeedback.gameObject);
        scoreFeedback = null;
    }

    private void ReleaseCorrectFeedback()
    {
        if (correctFeedback == null) return;
        correctFeedback.ResetFeedback();
        correctFeedback.gameObject.SetActive(false);
        Destroy(correctFeedback.gameObject); correctFeedback = null;
    }

    private void ReleaseRuleFeedback()
    {
        if (ruleFeedback == null) return;
        ruleFeedback.Cancel(); ruleFeedback.gameObject.SetActive(false);
        Destroy(ruleFeedback.gameObject); ruleFeedback = null;
    }

    private void RefreshHud(bool animateScore = false)
    {
        if (HasRuntimeState && hudView != null)
            hudView.Render(level, boardState, views, animateScore);
    }

    private void OnDisable()
    {
        settingsOpen = false;
        if (settingsPanel != null) { settingsPanel.Close(false); settingsPanel.gameObject.SetActive(false); }
        if (gameAudio != null) gameAudio.gameObject.SetActive(false);
        ResetVisualFeedback();
        if (lineFeedback != null) lineFeedback.gameObject.SetActive(false);
        if (scoreFeedback != null) scoreFeedback.gameObject.SetActive(false);
        if (correctFeedback != null) correctFeedback.gameObject.SetActive(false);
        if (ruleFeedback != null) ruleFeedback.gameObject.SetActive(false);
        ClearHint();
        if (propBar != null) propBar.gameObject.SetActive(false);
        SetCellInputsEnabled(false);
        SaveNow();
        if (hudView != null) hudView.gameObject.SetActive(false);
        if (resultPanel != null) resultPanel.Hide();
    }

    private void OnEnable()
    {
        settingsOpen = false;
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(true);
        if (gameAudio != null) gameAudio.gameObject.SetActive(true);
        ClearHint();
        if (propBar != null) propBar.gameObject.SetActive(false);
        skipClockFrame = true;
        if (hudView != null) hudView.gameObject.SetActive(false);
        if (resultPanel != null) resultPanel.Hide();
        if (!Application.isPlaying)
        {
            isReady = false;
            SetCellInputsEnabled(false);
            return;
        }

        if (!HasRuntimeState)
        {
            isReady = false;
            SetCellInputsEnabled(false);

            // 首次启动 inputs 为空，随后由 Start 初始化。
            // 已有格子却丢失数据，则暂停，不自动重建或恢复进度。
            if (inputs != null &&
                inputs.Length > 0 &&
                string.IsNullOrEmpty(startupError))
            {
                startupError =
                    "Scripts reloaded. Stop Play, then Play again.";

                Debug.LogWarning(
                    "GameController：脚本重新加载后运行状态不可用，已暂停格子输入。请停止 Play（运行）后再运行；重新运行将尝试读取本地存档。",
                    this);
            }

            return;
        }

        SetCellInputsEnabled(!boardState.IsFinished && appFocused && !appPaused && !settingsOpen);
        if (hudView != null) hudView.gameObject.SetActive(true);
        if (lineFeedback != null) lineFeedback.gameObject.SetActive(true);
        if (scoreFeedback != null) scoreFeedback.gameObject.SetActive(true);
        if (correctFeedback != null) correctFeedback.gameObject.SetActive(true);
        if (ruleFeedback != null) ruleFeedback.gameObject.SetActive(true);
        if (propBar != null) propBar.gameObject.SetActive(true);
        RefreshHud();
        ShowResultPanel();
    }

    private void SetCellInputsEnabled(bool enabled)
    {
        if (inputs == null)
            return;

        foreach (CellInput input in inputs)
        {
            if (input != null)
                input.SetInputEnabled(enabled);
        }
    }

    private void OnApplicationQuit()
    {
        ResetVisualFeedback();
        SetCellInputsEnabled(false);
        SaveNow();
    }

    private void OnDestroy()
    {
        ResetVisualFeedback();
        ReleaseLineFeedback();
        ReleaseScoreFeedback();
        ReleaseCorrectFeedback();
        ReleaseRuleFeedback();
        LineCompleted = null;
        SaveNow();
        ClearHint();
        if (propBar != null) Destroy(propBar.gameObject);
        if (hintPanel != null) Destroy(hintPanel.gameObject);
        if (hudView != null) Destroy(hudView.gameObject);
        if (resultPanel != null) Destroy(resultPanel.gameObject);
        if (screenLayout != null)
        {
            screenLayout.LayoutChanging -= CancelGesturesForLayoutChange;
            screenLayout.AbandonForTeardown();
        }
        if (settingsPanel != null) Destroy(settingsPanel.gameObject);
        if (gameAudio != null && gameAudio.transform.parent == transform) Destroy(gameAudio.gameObject);
        RunCompleted = null;
        if (ownsRuntimeLevel && level != null)
            Destroy(level);
        EndTutorialGuide();
        if (tutorialLevel != null) Destroy(tutorialLevel);
    }


    // Settings > Clarity: a soft, see-through pet icon per colour in the centre of each cell.
    private void ApplyClarity()
    {
        if (views == null || level == null) return;
        bool on = gameAudio != null && gameAudio.Settings != null && gameAudio.Settings.Get(GameSetting.Clarity);
        for (int i = 0; i < views.Length && i < level.RegionIds.Count; i++)
            if (views[i] != null) views[i].ApplyRegionDecoration(level.RegionIds[i], on);
    }

    private void OnSettingToggled(GameSetting setting)
    {
        if (setting == GameSetting.Clarity) ApplyClarity();
    }

    private void OnSettingsChanged(bool open)
    {
        settingsOpen = open;
        SyncAvatarIdle(true);
        skipClockFrame = true;
        if (open)
        {
            SetCellInputsEnabled(false); // 撤回未提交的预览，保留稳定存档。
            isDragMarking = false;
            ResetVisualFeedback();
            ClearHint();
            SaveNow();
        }
        else if (HasRuntimeState && isActiveAndEnabled && appFocused && !appPaused)
            SetCellInputsEnabled(!boardState.IsFinished);
    }

    private void ShowNewCross(int index)
    {
        views[index].RenderAnimated(CellState.Marked);
        // Drag-marked / prop-added crosses: the same pop plus the light scratch accent.
        gameAudio?.PlayPress();
        gameAudio?.PlaySound(GameSound.MarkOn);
    }

    private bool CanUseProps => CanDragMark && !isDragMarking &&
        !isTransitioning && !isRestoring && !propActionInProgress;

    private void PreparePropAction()
    {
        // 撤回尚未稳定提交的单击，取消按住手势，避免道具后又出现旧点击。
        SetCellInputsEnabled(false);
        isDragMarking = false;
        if (CanDragMark) SetCellInputsEnabled(true);
    }

    public void RevealAnimal()
    {
        if (!CanUseProps) return;
        propActionInProgress = true;
        try
        {
            PreparePropAction();
            ClearHint();
            int index = AnimalRevealService.Choose(level, boardState);
            if (index < 0) return;
            Debug.Log($"GameController：左道具揭晓第{index / level.Size + 1}行、第{index % level.Size + 1}列的动物，不扣命。", this);
            ResolvePlacement(index, true);
        }
        finally { propActionInProgress = false; }
    }

    public void AddRandomCrosses()
    {
        if (!CanUseProps) return;
        propActionInProgress = true;
        try
        {
            PreparePropAction();
            ClearHint();
            int[] targets = RandomCrossService.Choose(level, boardState);
            int added = ApplyCrosses(targets);
            if (propBar != null)
                propBar.SetNotice(added > 0 ? $"已随机补{added}个叉" : "没有可补叉的安全空格",
                    added > 0 ? $"Added {added} safe crosses" : "No safe empty cells left");
            Debug.Log($"GameController：右道具随机补叉，本次新增{added}个；已有标记/锁定格不变。", this);
        }
        finally { propActionInProgress = false; }
    }

    public void ShowLogicalHint()
    {
        if (!CanUseProps || hintPanel == null) return;
        PreparePropAction();
        ClearHint();
        pendingHint = LogicalHintService.Find(level, boardState);
        hintBoard = boardState;
        hintCells = boardState.ExportCells();
        Debug.Log("GameController：思路提示：" + pendingHint.Chinese, this);
        hintPanel.Show(pendingHint, views, ApplyLogicalHint, ClearHint);
        gameAudio?.PlaySound(GameSound.Hint);
    }

    private void ApplyLogicalHint()
    {
        if (!CanUseProps || pendingHint == null || !pendingHint.CanApply) return;
        if (boardState != hintBoard || !HintSnapshotMatches())
        {
            ClearHint();
            return; // 提示不能应用到后来变化的棋盘或另一关。
        }
        int[] targets = pendingHint.Crosses;
        propActionInProgress = true;
        try
        {
            PreparePropAction();
            ClearHint();
            int added = ApplyCrosses(targets);
            Debug.Log($"GameController：应用思路提示，仅新增{added}个普通叉，没有放动物。", this);
        }
        finally { propActionInProgress = false; }
    }

    private bool HintSnapshotMatches()
    {
        if (hintCells == null || hintCells.Length != boardState.CellCount) return false;
        for (int i = 0; i < hintCells.Length; i++)
            if (hintCells[i] != (int)boardState.GetCellState(i)) return false;
        return true;
    }

    // 共同的稳定补叉入口。先完整校验，再改数据；不允许切换已有叉或误标答案。
    private int ApplyCrosses(int[] targets)
    {
        if (!CanDragMark || targets == null || targets.Length == 0) return 0;
        var unique = new HashSet<int>();
        var answers = new HashSet<int>(level.SolutionIndices);
        foreach (int index in targets)
            if (index < 0 || index >= boardState.CellCount || !unique.Add(index) ||
                answers.Contains(index) || boardState.GetCellState(index) != CellState.Empty)
            {
                Debug.LogWarning("GameController：补叉目标已失效或不安全，没有应用本次道具。", this);
                return 0;
            }
        BoardState changedBoard = boardState;
        int added = 0;
        foreach (int index in targets)
        {
            if (!CanDragMark || boardState != changedBoard) break;
            if (boardState.TryToggleMark(index))
            {
                added++;
                ShowNewCross(index);
                CheckLineCompletion(index);
            }
        }
        if (boardState == changedBoard) SaveNow();
        return added;
    }

    private void ClearHint()
    {
        pendingHint = null;
        hintBoard = null;
        hintCells = null;
        if (hintPanel != null) hintPanel.Hide();
    }

    // Pre-placed animals: locked, already in the collection, no score/sound/announcer.
    private void PlaceGivenAnimals()
    {
        foreach (int index in level.GivenIndices)
            if (boardState.TryResolvePlacement(index, true) && views != null && index < views.Length && views[index] != null)
                views[index].Render(CellState.Placed);
    }

    private LevelData TutorialLevel()
    {
        if (tutorialLevel == null) tutorialLevel = TutorialGuide.CreateLevel();
        return tutorialLevel;
    }

    private void BeginTutorialGuide()
    {
        EndTutorialGuide();
        tutorialGuide = TutorialGuide.Create(boardBuilder.GetComponent<RectTransform>(), views,
            propBar != null ? propBar.Font : propFont, FinishTutorial);
        if (tutorialGuide == null)
        {
            Debug.LogWarning("GameController：新手引导创建失败，直接进入第1关。", this);
            FinishTutorial();
            return;
        }
        tutorialGuide.OnBoardChanged(boardState);
    }

    private void EndTutorialGuide()
    {
        if (tutorialGuide != null) Destroy(tutorialGuide.gameObject);
        tutorialGuide = null;
    }

    private void FinishTutorial()
    {
        TutorialGuide.MarkCompleted();
        EndTutorialGuide();
        tutorialMode = false;
        LevelData first = catalog != null ? catalog.GetAt(0) : level;
        if (first != null && TryBeginLevel(first, catalog != null ? 0 : -1))
        {
            if (settingsPanel != null) settingsPanel.ShowOpeningRules();
            SaveNow();
        }
        ApplyChromeVisibility();
    }

    // HUD, props and settings stay alive (their feedback keeps running) but are invisible during the tutorial.
    private void ApplyChromeVisibility()
    {
        bool visible = !tutorialMode;
        SetGroupVisible(hudView != null ? hudView.gameObject : null, visible);
        SetGroupVisible(propBar != null ? propBar.gameObject : null, visible);
        SetGroupVisible(settingsPanel != null ? settingsPanel.gameObject : null, visible);
        if (!visible && settingsPanel != null) settingsPanel.SetRulesAvailable(false); // also cancels a pending opening
    }

    private static void SetGroupVisible(GameObject target, bool visible)
    {
        if (target == null) return;
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null) { if (visible) return; group = target.AddComponent<CanvasGroup>(); }
        group.alpha = visible ? 1f : 0f;
        group.blocksRaycasts = visible; group.interactable = visible;
    }

    // ---- Developer jump (DevLevelJumper; editor/development builds only) ----
    public int CatalogCount => catalog != null ? catalog.Count : 0;

    public void DevStartTutorial()
    {
        if (!Application.isPlaying || isTransitioning) return;
        CloseSettingsForDevJump();
        tutorialMode = true;
        EndTutorialGuide();
        if (TryBeginLevel(TutorialLevel(), -1)) BeginTutorialGuide();
        else tutorialMode = false;
        ApplyChromeVisibility();
    }

    // The developer tools are unlocked from the settings card, so it is usually still open: close it
    // properly first, or the tutorial hides it while 'settings open' keeps every cell blocked.
    private void CloseSettingsForDevJump()
    {
        if (settingsPanel != null) settingsPanel.Close(true);
        settingsOpen = false;
    }

    public bool DevJumpToLevel(int number)
    {
        if (!Application.isPlaying || isTransitioning || catalog == null ||
            number < 1 || number > catalog.Count) return false;
        CloseSettingsForDevJump();
        bool wasTutorial = tutorialMode;
        tutorialMode = false;
        if (!TryBeginLevel(catalog.GetAt(number - 1), number - 1))
        {
            tutorialMode = wasTutorial;
            ApplyChromeVisibility();
            return false;
        }
        EndTutorialGuide();
        ApplyChromeVisibility();
        if (settingsPanel != null) settingsPanel.ShowOpeningRules();
        SaveNow();
        return true;
    }

    // 正常界面仍用 Canvas；OnGUI 仅保留启动错误/存档不可用提示，不伪装为结算。
    private void OnGUI()
    {
        if (!Application.isPlaying || !isActiveAndEnabled ||
            (string.IsNullOrEmpty(startupError) && string.IsNullOrEmpty(saveNotice)))
            return;

        if (hudStyle == null)
            hudStyle = new GUIStyle(GUI.skin.label);
        hudStyle.fontSize = Mathf.Clamp(Screen.width / 24, 14, 26);
        hudStyle.alignment = TextAnchor.MiddleCenter;
        hudStyle.normal.textColor = Color.white;
        string message = !string.IsNullOrEmpty(startupError) ? startupError : saveNotice;
        GUI.Label(new Rect(10, 10, Screen.width - 20, 90), message, hudStyle);
    }
}
