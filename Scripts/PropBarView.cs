using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 下方三圆按钮，只发请求；库存暂不实现，测试阶段不限次数。
public class PropBarView : MonoBehaviour
{
    public const float HeightRatio = .33f;
    public const float BoardGapRatio = .025f;
    private RectTransform board;
    private RectTransform footer;
    private RectTransform buttonsRoot;
    private RectTransform actionArea;
    private Button[] buttons;
    private TextMeshProUGUI[] labels;
    private RectTransform[] propCaptions;
    private RectTransform[] shadows;
    private static readonly string[] IconNames = { "prop_reveal", "prop_hint", "prop_cross" };
    private readonly string[] propNames = { "揭晓动物", "思路提示", "随机补3叉" };
    private TextMeshProUGUI notice;
    private bool available;
    private bool hintVisible;
    private bool isBuilt;
    private float lastCaptionWidth = -1f;
    public TMP_FontAsset Font { get; private set; }
    public RectTransform Footer => footer;
    public RectTransform ActionArea => actionArea;

    public static PropBarView Create(RectTransform board, TMP_FontAsset preferredFont,
        Sprite circle, Action reveal, Action hint, Action crosses)
    {
        if (board == null || !(board.parent is RectTransform)) return null;
        TMP_Text prototype = board.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = preferredFont != null ? preferredFont :
            prototype != null ? prototype.font : TMP_Settings.defaultFontAsset;
        if (font == null || circle == null) return null;
        RectTransform root = MakeRect("PropBar", board.parent);
        root.gameObject.SetActive(false); // 构建期间不启用LateUpdate或按钮。
        try
        {
            var view = root.gameObject.AddComponent<PropBarView>();
            view.board = board;
            view.footer = root;
            view.Font = font;
            view.actionArea = MakeRect("PropAndHintArea", root);
            view.buttonsRoot = MakeRect("ThreePropButtons", view.actionArea);
            Stretch(view.buttonsRoot);
            view.buttons = new Button[3];
            view.labels = new TextMeshProUGUI[3];
            view.propCaptions = new RectTransform[3];
            Action[] actions = { reveal, hint, crosses };
            string[] chinese = { "揭晓动物", "思路提示", "随机补3叉" };
            string[] english = { "Reveal", "Hint", "3 Crosses" };
            view.shadows = new RectTransform[3];
            for (int i = 0; i < 3; i++)
            {
                // Soft drop shadow behind each white button so it lifts off the cream background.
                RectTransform shadowRect = MakeRect("PropShadow_" + i, view.buttonsRoot);
                Image shadow = shadowRect.gameObject.AddComponent<Image>();
                shadow.sprite = circle; shadow.preserveAspect = true; shadow.raycastTarget = false;
                shadow.color = new Color(.45f, .30f, .18f, .16f);
                view.shadows[i] = shadowRect;
                RectTransform buttonRect = MakeRect("Prop_" + i, view.buttonsRoot);
                Image background = buttonRect.gameObject.AddComponent<Image>();
                background.sprite = circle;
                background.color = Color.white;
                background.preserveAspect = true;
                background.raycastTarget = true;
                // Image 默认使用矩形命中（阈值0），无需再设。
                // 设置该属性会读取纹理；高清圆形已释放CPU像素，会在此抛异常。
                Button button = buttonRect.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                // No colour tint: buttons are briefly non-interactable while the player swipes
                // crosses, and Unity's disabled tint turned all three dark. Press feedback is the squash.
                button.transition = Selectable.Transition.None;
                Navigation navigation = button.navigation;
                navigation.mode = Navigation.Mode.None;
                button.navigation = navigation;
                Action callback = actions[i];
                button.onClick.AddListener(() => { if (view.available && !view.hintVisible) callback?.Invoke(); });
                view.buttons[i] = button;
                TextMeshProUGUI label = MakeText("Name", buttonRect, font);
                Stretch(label.rectTransform);
                label.margin = new Vector4(5f, 2f, 5f, 2f);
                label.text = Choose(font, chinese[i], english[i]);
                label.color = GameUiStyle.Ink;
                label.enableAutoSizing = true;
                view.labels[i] = label;
                label.gameObject.SetActive(false);
                view.propCaptions[i] = GameUiStyle.Label("PropName", buttonRect, font, chinese[i], english[i]);
                Stretch(view.propCaptions[i]);
                // Picture icon (magnifier+puppy / bulb / box with the game's cross); the text caption
                // stays only as a fallback when the icon texture is missing.
                Texture2D iconTexture = Resources.Load<Texture2D>("UiIcons/" + IconNames[i]);
                if (iconTexture != null)
                {
                    RectTransform iconRect = MakeRect("PropIcon", buttonRect);
                    iconRect.anchorMin = Vector2.one * .13f; iconRect.anchorMax = Vector2.one * .87f;
                    iconRect.sizeDelta = iconRect.anchoredPosition = Vector2.zero;
                    var icon = iconRect.gameObject.AddComponent<RawImage>();
                    icon.texture = iconTexture; icon.raycastTarget = false;
                    view.propCaptions[i].gameObject.SetActive(false);
                }
                ButtonPressFeedback.Attach(button);
            }
            view.notice = MakeText("PropNotice", view.buttonsRoot, font);
            view.notice.text = ""; // Keep space for actionable notices, not a permanent debug caption.
            view.isBuilt = true; // 所有按钮、文字和提示都创建好后才允许刷新。
            view.RefreshLayout();
            view.SetAvailable(false);
            root.gameObject.SetActive(true);
            return view;
        }
        catch (Exception exception)
        {
            // 只清理本次自建UI，失败返回控制器统一暂停；不留半成品反复报错。
            root.gameObject.SetActive(false);
            Destroy(root.gameObject);
            Debug.LogError("PropBarView：道具栏创建失败：" + exception.GetType().Name +
                "：" + exception.Message, board);
            return null;
        }
    }

    public void SetAvailable(bool enabled)
    {
        available = enabled && isBuilt;
        if (buttons == null) return;
        foreach (Button button in buttons)
            if (button != null) button.interactable = available && !hintVisible;
    }

    public void SetHintVisible(bool visible)
    {
        hintVisible = visible;
        if (buttonsRoot != null) buttonsRoot.gameObject.SetActive(!visible);
        SetAvailable(available);
    }

    public void SetNotice(string chinese, string english)
    {
        if (isBuilt && notice != null) notice.text = Choose(Font, chinese, english);
    }

    private void LateUpdate() => RefreshLayout();

    private void RefreshLayout()
    {
        if (!isBuilt || board == null || footer == null || notice == null ||
            buttons == null || labels == null || buttons.Length != labels.Length ||
            !(footer.parent is RectTransform parent)) return;
        float width = Mathf.Min(board.rect.width, board.rect.height);
        bool captionSizeChanged = !Mathf.Approximately(width, lastCaptionWidth);
        footer.anchorMin = footer.anchorMax = parent.pivot;
        footer.pivot = new Vector2(.5f, 1f);
        Vector3 bottom = parent.InverseTransformPoint(board.TransformPoint(
            new Vector3(board.rect.center.x, board.rect.yMin, 0f)));
        footer.anchoredPosition = new Vector2(bottom.x, bottom.y - width * BoardGapRatio);
        footer.sizeDelta = new Vector2(width, width * HeightRatio);
        actionArea.anchorMin = actionArea.anchorMax = new Vector2(.5f, 1f);
        actionArea.pivot = new Vector2(.5f, 1f);
        actionArea.anchoredPosition = Vector2.zero;
        actionArea.sizeDelta = new Vector2(width, width * .33f);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null || labels[i] == null) continue;
            RectTransform rect = (RectTransform)buttons[i].transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2((i - 1) * width * .30f, -width * .10f);
            rect.sizeDelta = Vector2.one * width * .19f; // a touch bigger (was .17)
            if (shadows != null && shadows[i] != null)
            {
                shadows[i].anchorMin = shadows[i].anchorMax = rect.anchorMin;
                shadows[i].anchoredPosition = rect.anchoredPosition + Vector2.down * width * .010f;
                shadows[i].sizeDelta = Vector2.one * width * .199f;
            }
            labels[i].fontSizeMin = width * .017f;
            labels[i].fontSizeMax = width * .028f;
            if (captionSizeChanged) GameUiStyle.LabelSize(propCaptions[i], propNames[i], width * .028f);
        }
        lastCaptionWidth = width;
        notice.rectTransform.anchorMin = notice.rectTransform.anchorMax = new Vector2(.5f, 1f);
        notice.rectTransform.anchoredPosition = new Vector2(0f, -width * .24f);
        notice.rectTransform.sizeDelta = new Vector2(width, width * .07f);
        notice.fontSize = width * .028f;
    }

    internal static string Choose(TMP_FontAsset font, string chinese, string english) =>
        font != null && font.HasCharacters(chinese, out uint[] missing, true, false) ? chinese : english;

    internal static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        return rect;
    }

    internal static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    internal static TextMeshProUGUI MakeText(string name, Transform parent, TMP_FontAsset font)
    {
        TextMeshProUGUI text = MakeRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.enableAutoSizing = false;
        return text;
    }
}
