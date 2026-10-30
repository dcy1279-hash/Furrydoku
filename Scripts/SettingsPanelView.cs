using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Icon-tile settings (Sound / Music / Voice / Vibration / Clarity), each with an
// ON/OFF slider inside the tile; × or tapping outside closes; Restart asks first.
// Opening the menu pauses board input/clock; never changes Time.timeScale or level data.
public class SettingsPanelView : MonoBehaviour
{
    private const int SettingCount = 5;
    private static readonly GameSetting[] Order =
        { GameSetting.Sound, GameSetting.Music, GameSetting.Voice, GameSetting.Vibration, GameSetting.Clarity };
    private static readonly string[] IconNames = { "sound", "music", "voice", "vibe", "clear" };
    private static readonly GameUiGraphic.Kind[] Icons =
        { GameUiGraphic.Kind.Speaker, GameUiGraphic.Kind.Music, GameUiGraphic.Kind.Voice,
          GameUiGraphic.Kind.Vibrate, GameUiGraphic.Kind.Palette };
    // Palette (matches the approved mock).
    private static readonly Color Card = new Color(1f, .98f, .95f), CardEdge = new Color(.918f, .859f, .776f);
    private static readonly Color Brown = new Color(.47f, .31f, .235f), Ink = new Color(.57f, .40f, .37f);
    private static readonly Color TileOn = new Color(1f, .953f, .878f), TileOnEdge = new Color(.94f, .84f, .667f);
    private static readonly Color TileOff = new Color(.965f, .945f, .92f), TileOffEdge = new Color(.90f, .87f, .83f);
    private static readonly Color IconOn = new Color(1f, .914f, .78f), IconOnRing = new Color(.95f, .72f, .40f);
    private static readonly Color IconOff = new Color(.945f, .925f, .90f), IconOffRing = new Color(.886f, .85f, .81f);
    private static readonly Color GlyphOff = new Color(.79f, .73f, .68f);
    private static readonly Color SwitchOn = new Color(.494f, .788f, .549f), SwitchOff = new Color(.824f, .784f, .745f);
    private static readonly Color Orange = new Color(1f, .604f, .122f), OrangeShade = new Color(.83f, .36f, 0f);

    private sealed class Tile
    {
        public GameSetting Setting;
        public RectTransform Root, Visual, IconRoot, Switch, Knob;
        public GameUiGraphic Back, Edge, IconBack, IconRing, Glyph, Slash, Pill;
        public RawImage IconImage;
        public Texture2D OnTexture, OffTexture;
        public TextMeshProUGUI Label;
    }

    private AudioController audio;
    private TMP_FontAsset font, heavyFont, boldFont;
    private RectTransform safe, content, launcher, backLauncher, rulesLauncher;
    private RulesStripView rules;
    private bool rulesAvailable = true;
    private HudView boundHud;
    private bool pauseOnly;
    private GameObject modal, confirm;
    private TextMeshProUGUI title, launcherLabel, restartLabel, confirmText, confirmYes, confirmNo;
    private RectTransform cardEdge, closeRect, restartRect, restartShade, confirmCard, confirmYesRect, confirmNoRect;
    private readonly Tile[] tiles = new Tile[SettingCount];
    private Action<bool> changed;
    private Action restart;
    private Action<GameSetting> settingChanged;
    private bool open, built;

    public static SettingsPanelView Create(RectTransform board, TMP_FontAsset font,
        AudioController audio, Action<bool> changed, Action restart = null, Action<GameSetting> settingChanged = null)
    {
        Canvas canvas = board != null ? board.GetComponentInParent<Canvas>() : null;
        if (canvas == null || font == null || audio == null) return null;
        RectTransform root = PropBarView.MakeRect("GameSettings", canvas.rootCanvas.transform);
        PropBarView.Stretch(root);
        var view = root.gameObject.AddComponent<SettingsPanelView>();
        view.font = font; view.audio = audio; view.changed = changed;
        view.restart = restart; view.settingChanged = settingChanged;
        view.heavyFont = LoadFont("SFRounded-Heavy", font);
        view.boldFont = LoadFont("SFRounded-Bold", font);
        var launchSafe = SafeAreaFitter.Create(root, "SettingsLauncherSafeArea");
        if (launchSafe == null) { Destroy(root.gameObject); return null; }
        view.launcher = view.MakeButton("SettingsButton", launchSafe.transform, out view.launcherLabel);
        view.launcherLabel.text = "Settings";
        view.launcher.GetComponent<Button>().onClick.AddListener(() => view.Show());
        view.launcherLabel.gameObject.SetActive(false);
        GameUiStyle.NavigationButton(view.launcher, GameUiGraphic.Kind.Gear);
        view.backLauncher = view.MakeButton("PauseBackButton", launchSafe.transform, out TextMeshProUGUI backLabel);
        backLabel.gameObject.SetActive(false);
        GameUiStyle.NavigationButton(view.backLauncher, GameUiGraphic.Kind.Back);
        view.backLauncher.GetComponent<Button>().onClick.AddListener(() => view.ShowPause());

        view.rulesLauncher = view.MakeButton("RulesButton", launchSafe.transform, out TextMeshProUGUI rulesLabel);
        rulesLabel.gameObject.SetActive(false);
        GameUiStyle.NavigationButton(view.rulesLauncher, GameUiGraphic.Kind.Exclamation);
        view.rulesLauncher.GetComponent<Button>().onClick.AddListener(() => {
            if (view.rulesAvailable && !view.open && view.rules != null) view.rules.Toggle();
        });
        view.rules = RulesStripView.Create(root, font);
        view.rules.AutoOpened += () => view.RulesAutoOpened?.Invoke();
        view.rules.BindBoard(board);

        // Full-screen dimmer: tapping outside the card closes the menu.
        RectTransform modal = PropBarView.MakeRect("SettingsModal", root);
        PropBarView.Stretch(modal);
        view.modal = modal.gameObject;
        Image dimmer = modal.gameObject.AddComponent<Image>();
        dimmer.color = new Color(.16f, .11f, .07f, .72f);
        dimmer.raycastTarget = true;
        Button outside = modal.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.navigation = new Navigation { mode = Navigation.Mode.None };
        outside.onClick.AddListener(() => { if (view.confirm == null || !view.confirm.activeSelf) view.Close(true); });
        var modalSafe = SafeAreaFitter.Create(modal, "SettingsSafeArea");
        if (modalSafe == null) { Destroy(root.gameObject); return null; }
        view.safe = modalSafe.GetComponent<RectTransform>();
        view.BuildCard();
        view.built = true;
        view.RefreshValues();
        view.RefreshLayout();
        view.modal.SetActive(false);
        return view;
    }

    private static TMP_FontAsset LoadFont(string name, TMP_FontAsset fallback)
    {
        try
        {
            Font source = Resources.Load<Font>("Fonts/" + name);
            if (source == null) return fallback;
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(source);
            if (asset == null) return fallback;
            asset.name = name + "_Runtime";
            return asset;
        }
        catch (Exception error)
        {
            Debug.LogWarning("SettingsPanelView：圆体字体加载失败，改用默认字体：" + error.Message);
            return fallback;
        }
    }

    private void BuildCard()
    {
        // Card (edge panel behind a slightly smaller face) that swallows taps.
        cardEdge = PropBarView.MakeRect("SettingsCardEdge", safe);
        Panel(cardEdge, CardEdge, .08f, true).gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
        content = PropBarView.MakeRect("SettingsCard", cardEdge);
        Panel(content, Card, .075f, false);

        title = Text("Title", content, heavyFont, Brown);
        title.text = "Settings";
        // Secret: 10 quick taps on the title open the developer password pad. No visible feedback.
        title.raycastTarget = true;
        Button secret = title.gameObject.AddComponent<Button>();
        secret.transition = Selectable.Transition.None;
        secret.onClick.AddListener(CountSecretTap);

        closeRect = HitButton("Close", content, () => Close(true));
        Transform closeVisual = closeRect.GetChild(0);
        Panel(Child("CloseBack", closeVisual), new Color(.945f, .914f, .875f), .5f, false);
        Glyph(Child("CloseX", closeVisual, .26f), GameUiGraphic.Kind.Close, Ink);

        for (int i = 0; i < SettingCount; i++)
        {
            int captured = i;
            var tile = new Tile { Setting = Order[i] };
            tile.Root = HitButton("Setting_" + Order[i], content, () => Toggle(Order[captured]));
            tile.Visual = tile.Root.GetChild(0) as RectTransform;
            tile.Edge = Panel(Child("TileEdge", tile.Visual), TileOnEdge, .19f, false);
            tile.Back = Panel(Child("TileFace", tile.Visual, .018f), TileOn, .18f, false);
            tile.IconRoot = Child("Icon", tile.Visual);
            tile.IconRing = Panel(Child("IconRing", tile.IconRoot), IconOnRing, .5f, false);
            tile.IconBack = Panel(Child("IconFace", tile.IconRoot, .04f), IconOn, .5f, false);
            tile.Glyph = Glyph(Child("Glyph", tile.IconRoot, .17f), Icons[i], Ink);
            tile.Slash = Glyph(Child("OffSlash", tile.IconRoot, .12f), GameUiGraphic.Kind.Slash, GlyphOff);
            // Smooth, mip-mapped icon art (same SVG as the approved mock); vector shapes only as fallback.
            tile.OnTexture = Resources.Load<Texture2D>("UiIcons/settings_" + IconNames[i] + "_on");
            tile.OffTexture = Resources.Load<Texture2D>("UiIcons/settings_" + IconNames[i] + "_off");
            if (tile.OnTexture != null && tile.OffTexture != null)
            {
                tile.IconRing.enabled = tile.IconBack.enabled = tile.Glyph.enabled = tile.Slash.enabled = false;
                tile.IconImage = Child("IconArt", tile.IconRoot).gameObject.AddComponent<RawImage>();
                tile.IconImage.raycastTarget = false;
            }
            tile.Switch = Child("Switch", tile.Visual);
            tile.Pill = Panel(tile.Switch, SwitchOn, .5f, false);
            tile.Label = Text("State", tile.Switch, boldFont, Color.white);
            tile.Knob = PropBarView.MakeRect("Knob", tile.Switch);
            Panel(tile.Knob, Color.white, .5f, false);
            tiles[i] = tile;
        }

        restartShade = PropBarView.MakeRect("RestartShade", content);
        Panel(restartShade, OrangeShade, .5f, false);
        restartRect = HitButton("Restart", content, OpenConfirm);
        Transform restartVisual = restartRect.GetChild(0);
        Panel(Child("RestartFace", restartVisual), Orange, .5f, false);
        restartLabel = Text("Label", restartVisual, boldFont, Color.white);
        PropBarView.Stretch(restartLabel.rectTransform);
        restartLabel.text = "Restart";

        // Confirmation layer over the card.
        RectTransform shield = PropBarView.MakeRect("RestartConfirm", cardEdge);
        PropBarView.Stretch(shield);
        confirm = shield.gameObject;
        Image shieldImage = shield.gameObject.AddComponent<Image>();
        shieldImage.color = new Color(.16f, .11f, .07f, .35f); shieldImage.raycastTarget = true;
        Button shieldButton = shield.gameObject.AddComponent<Button>();
        shieldButton.transition = Selectable.Transition.None;
        shieldButton.onClick.AddListener(() => confirm.SetActive(false));
        confirmCard = PropBarView.MakeRect("ConfirmCard", shield);
        Panel(confirmCard, Card, .14f, true).gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
        confirmText = Text("Question", confirmCard, heavyFont, Brown);
        confirmText.text = "Restart this level?";
        confirmNoRect = HitButton("Cancel", confirmCard, () => confirm.SetActive(false));
        Panel(Child("CancelFace", confirmNoRect.GetChild(0)), new Color(.945f, .914f, .875f), .5f, false);
        confirmNo = Text("Label", confirmNoRect.GetChild(0), boldFont, Brown);
        PropBarView.Stretch(confirmNo.rectTransform); confirmNo.text = "Cancel";
        confirmYesRect = HitButton("ConfirmRestart", confirmCard, ConfirmRestart);
        Panel(Child("RestartFace", confirmYesRect.GetChild(0)), Orange, .5f, false);
        confirmYes = Text("Label", confirmYesRect.GetChild(0), boldFont, Color.white);
        PropBarView.Stretch(confirmYes.rectTransform); confirmYes.text = "Restart";
        confirm.SetActive(false);
    }

    // ---- small builders -------------------------------------------------------
    private RectTransform MakeButton(string name, Transform parent, out TextMeshProUGUI label)
    {
        RectTransform rect = PropBarView.MakeRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(.26f, .43f, .58f, 1f); image.raycastTarget = true;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        label = PropBarView.MakeText("Label", rect, font);
        PropBarView.Stretch(label.rectTransform);
        ButtonPressFeedback.Attach(button);
        return rect;
    }
    // Transparent hit target + press-shrink visual child (ButtonPressFeedback) holding the art.
    private RectTransform HitButton(string name, Transform parent, Action onClick)
    {
        RectTransform rect = PropBarView.MakeRect(name, parent);
        Image hit = rect.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => onClick?.Invoke());
        ButtonPressFeedback.Attach(button);
        if (rect.childCount == 0) { RectTransform v = PropBarView.MakeRect("ButtonVisual", rect); PropBarView.Stretch(v); }
        return rect;
    }
    private static RectTransform Child(string name, Transform parent, float inset = 0f)
    {
        RectTransform rect = PropBarView.MakeRect(name, parent);
        rect.anchorMin = Vector2.one * inset; rect.anchorMax = Vector2.one * (1f - inset);
        rect.sizeDelta = rect.anchoredPosition = Vector2.zero;
        return rect;
    }
    private static GameUiGraphic Panel(RectTransform rect, Color colour, float corner, bool raycast)
    {
        var g = rect.gameObject.AddComponent<GameUiGraphic>();
        g.Shape = GameUiGraphic.Kind.RoundedPanel; g.CornerRatio = corner; g.color = colour; g.raycastTarget = raycast;
        return g;
    }
    private static GameUiGraphic Glyph(RectTransform rect, GameUiGraphic.Kind kind, Color colour)
    {
        var g = rect.gameObject.AddComponent<GameUiGraphic>();
        g.Shape = kind; g.color = colour; g.raycastTarget = false;
        return g;
    }
    private static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset face, Color colour)
    {
        TextMeshProUGUI t = PropBarView.MakeText(name, parent, face);
        t.color = colour;
        return t;
    }

    private int secretTaps;
    private float lastSecretTap = -100f;
    private void CountSecretTap()
    {
        if (Time.unscaledTime - lastSecretTap > 1.5f) secretTaps = 0;
        lastSecretTap = Time.unscaledTime;
        if (++secretTaps < 10) return;
        secretTaps = 0;
        DevLevelJumper.RequestUnlock();
    }

    // ---- public API (unchanged) -------------------------------------------------
    public void BindHud(HudView hud)
    {
        bool newRound = hud != boundHud;
        boundHud = hud;
        if (rules != null) rules.BindHud(hud);
        RefreshLayout();
        if (newRound) ShowOpeningRules();
    }
    public void ShowOpeningRules()
    {
        rulesAvailable = true;
        if (!open && boundHud != null && boundHud.gameObject.activeInHierarchy) rules?.ScheduleOpening();
    }
    public event Action RulesAutoOpened;   // the level-opening rules pop-up just appeared
    public void HideRules() => rules?.Dismiss();
    public void SetRulesPersistent(bool value) => rules?.SetPersistent(value);
    public void SetRulesAvailable(bool value)
    {
        rulesAvailable = value;
        if (!value) rules?.Hide(true);
    }
    public void Show() => ShowMenu(false);
    public void ShowPause() => ShowMenu(true);
    private void ShowMenu(bool pause)
    {
        if (!built || open || !isActiveAndEnabled) return;
        rules?.Hide(true);
        pauseOnly = pause;
        title.text = pause ? "Paused" : "Settings";
        confirm.SetActive(false);
        restartRect.gameObject.SetActive(restart != null);
        restartShade.gameObject.SetActive(restart != null);
        open = true;
        transform.SetAsLastSibling();
        modal.SetActive(true);
        RefreshValues(); RefreshLayout();
        changed?.Invoke(true);
    }
    public void Close(bool notify)
    {
        bool wasOpen = open;
        open = false;
        if (confirm != null) confirm.SetActive(false);
        if (modal != null) modal.SetActive(false);
        if (wasOpen && notify) changed?.Invoke(false);
        if (wasOpen && rulesAvailable && isActiveAndEnabled) rules?.RestorePersistent();
    }
    private void OpenConfirm()
    {
        if (!open || restart == null) return;
        confirm.SetActive(true);
        RefreshLayout();
    }
    private void ConfirmRestart()
    {
        if (!open) return;
        Action action = restart;
        Close(true);              // resume input first, then start the fresh round
        action?.Invoke();
    }
    private void Toggle(GameSetting setting)
    {
        if (!open || audio == null) return;
        bool saved = audio.Settings.Toggle(setting);
        if (!saved) Debug.LogWarning("SettingsPanelView：设置本次已生效，但保存失败。");
        audio.ApplySettings();
        RefreshValues();
        settingChanged?.Invoke(setting);
    }
    private void RefreshValues()
    {
        if (audio == null) return;
        foreach (Tile tile in tiles)
        {
            if (tile == null) continue;
            bool on = audio.Settings.Get(tile.Setting);
            tile.Back.color = on ? TileOn : TileOff;
            tile.Edge.color = on ? TileOnEdge : TileOffEdge;
            tile.IconBack.color = on ? IconOn : IconOff;
            tile.IconRing.color = on ? IconOnRing : IconOffRing;
            tile.Glyph.color = on ? Ink : GlyphOff;
            tile.Glyph.Muted = !on; tile.Glyph.SetVerticesDirty();
            tile.Slash.gameObject.SetActive(!on && tile.IconImage == null);
            if (tile.IconImage != null) tile.IconImage.texture = on ? tile.OnTexture : tile.OffTexture;
            tile.Pill.color = on ? SwitchOn : SwitchOff;
            tile.Label.text = on ? "ON" : "OFF";
        }
        RefreshSwitches();
    }
    private void RefreshSwitches()
    {
        foreach (Tile tile in tiles)
        {
            if (tile == null || audio == null) continue;
            bool on = audio.Settings.Get(tile.Setting);
            Rect r = tile.Switch.rect; float h = r.height;
            tile.Knob.sizeDelta = Vector2.one * (h - h * .25f);
            tile.Knob.anchoredPosition = new Vector2((on ? 1f : -1f) * (r.width * .5f - h * .5f), 0f);
            RectTransform label = tile.Label.rectTransform;
            label.sizeDelta = new Vector2(r.width - h, h);
            label.anchoredPosition = new Vector2((on ? -1f : 1f) * h * .5f * .9f, 0f);
            tile.Label.fontSize = h * .50f;
        }
    }

    // ---- layout (mock units: card 640 x 760, origin at card centre, y up) ----------
    private void RefreshLayout()
    {
        if (!built || safe == null || launcher == null || !(launcher.parent is RectTransform parent)) return;
        bool hudVisible = boundHud != null && boundHud.gameObject.activeInHierarchy;
        launcher.gameObject.SetActive(hudVisible);
        backLauncher.gameObject.SetActive(hudVisible);
        rulesLauncher.gameObject.SetActive(hudVisible);
        rulesLauncher.GetComponent<Button>().interactable = hudVisible && rulesAvailable && !open;
        if (!hudVisible) rules?.Hide(true);
        if (hudVisible)
        {
            MatchAnchor(launcher, parent, boundHud.SettingsAnchor);
            MatchAnchor(backLauncher, parent, boundHud.BackAnchor);
            MatchAnchor(rulesLauncher, parent, boundHud.RulesAnchor);
        }
        if (!open) return;
        float width = Mathf.Min(860f, safe.rect.width * .88f, safe.rect.height * .80f / 1.1875f);
        float k = width / 640f;
        cardEdge.sizeDelta = new Vector2(640f, 760f) * k; cardEdge.anchoredPosition = Vector2.zero;
        content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
        content.sizeDelta = -Vector2.one * 10f * k; content.anchoredPosition = Vector2.zero;
        Place(title.rectTransform, 400f, 80f, 0f, 308f, k); title.fontSize = 50f * k;
        Place(closeRect, 60f, 60f, 260f, 314f, k);
        float[] xs = { -194f, 0f, 194f, -97f, 97f }, ys = { 123f, 123f, 123f, -71f, -71f };
        for (int i = 0; i < SettingCount; i++)
        {
            Tile t = tiles[i];
            Place(t.Root, 170f, 170f, xs[i], ys[i], k);
            Place(t.IconRoot, 104f, 104f, 0f, 19f, k);
            Place(t.Switch, 96f, 36f, 0f, -53f, k);
        }
        Place(restartShade, 400f, 86f, 0f, -299f, k);
        Place(restartRect, 400f, 84f, 0f, -288f, k);
        restartLabel.fontSize = 38f * k;
        Place(confirmCard, 520f, 300f, 0f, 0f, k);
        Place(confirmText.rectTransform, 480f, 90f, 0f, 60f, k); confirmText.fontSize = 38f * k;
        Place(confirmNoRect, 210f, 76f, -120f, -70f, k); confirmNo.fontSize = 32f * k;
        Place(confirmYesRect, 210f, 76f, 120f, -70f, k); confirmYes.fontSize = 32f * k;
        RefreshSwitches();
    }
    private static void Place(RectTransform rect, float w, float h, float x, float y, float k)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(w, h) * k; rect.anchoredPosition = new Vector2(x, y) * k;
    }
    private static void MatchAnchor(RectTransform button, RectTransform parent, RectTransform anchor)
    {
        if (anchor == null) return;
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, anchor);
        button.anchorMin = button.anchorMax = parent.pivot;
        button.pivot = new Vector2(.5f, .5f);
        button.anchoredPosition = new Vector2(bounds.center.x, bounds.center.y);
        button.sizeDelta = new Vector2(bounds.size.x, bounds.size.y);
    }
    private void LateUpdate() => RefreshLayout();
    private void OnDisable() { rules?.Hide(true); Close(true); }
    private void OnDestroy() { changed = null; restart = null; settingChanged = null; }
}
