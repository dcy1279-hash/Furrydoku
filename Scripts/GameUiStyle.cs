using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared visual helpers; do not own gameplay, save data or borrowed textures.
public static class GameUiStyle
{
    public static readonly Color Ink = new Color(.57f, .40f, .37f, 1f);
    // Page background shared by the game screen and the loading screen (change it in one place).
    public static readonly Color PageBackground = new Color(.98f, .97f, .94f, 1f);
    // Runtime TMP font from a TTF in Resources/Fonts (cached); falls back when missing.
    private static readonly System.Collections.Generic.Dictionary<string, TMP_FontAsset> runtimeFonts =
        new System.Collections.Generic.Dictionary<string, TMP_FontAsset>();
    public static TMP_FontAsset RuntimeFont(string name, TMP_FontAsset fallback)
    {
        if (runtimeFonts.TryGetValue(name, out TMP_FontAsset cached) && cached != null) return cached;
        try
        {
            Font source = Resources.Load<Font>("Fonts/" + name);
            TMP_FontAsset asset = source != null ? TMP_FontAsset.CreateFontAsset(source) : null;
            if (asset == null) return fallback;
            asset.name = name + "_Runtime";
            runtimeFonts[name] = asset;
            return asset;
        }
        catch (System.Exception error)
        {
            Debug.LogWarning("GameUiStyle：字体 " + name + " 加载失败，改用默认字体：" + error.Message);
            return fallback;
        }
    }
    // Pop-up words (+score, Good!/Great!): rounded heavy face, white outline, faint warm shadow.
    public static Material PopTextMaterial(TMP_FontAsset font)
    {
        var m = new Material(font.material) { name = "PopText_Runtime" }; // never edit the shared material
        m.EnableKeyword("OUTLINE_ON"); m.EnableKeyword("UNDERLAY_ON");
        SetColor(m, "_OutlineColor", Color.white); SetFloat(m, "_OutlineWidth", .24f);
        SetFloat(m, "_FaceDilate", .06f);
        SetColor(m, "_UnderlayColor", new Color(.45f, .25f, .08f, .30f));
        SetFloat(m, "_UnderlayOffsetX", 0f); SetFloat(m, "_UnderlayOffsetY", -.30f);
        SetFloat(m, "_UnderlayDilate", .30f); SetFloat(m, "_UnderlaySoftness", .55f);
        return m;
    }
    // Warm yellow (top) to orange (bottom).
    public static void PopGradient(TextMeshProUGUI text)
    {
        text.color = Color.white; text.enableVertexGradient = true;
        Color top = new Color(1f, .84f, .35f), bottom = new Color(1f, .52f, .24f);
        text.colorGradient = new VertexGradient(top, top, bottom, bottom);
    }
    private static void SetFloat(Material m, string name, float v) { if (m.HasProperty(name)) m.SetFloat(name, v); }
    private static void SetColor(Material m, string name, Color v) { if (m.HasProperty(name)) m.SetColor(name, v); }

    public static RectTransform Panel(string name, Transform parent, Color color, float radius = .45f)
    {
        RectTransform r = PropBarView.MakeRect(name, parent);
        var graphic = r.gameObject.AddComponent<GameUiGraphic>();
        graphic.Shape = GameUiGraphic.Kind.RoundedPanel;
        graphic.CornerRatio = radius; graphic.color = color; graphic.raycastTarget = false;
        return r;
    }
    public static RectTransform Label(string name, Transform parent, TMP_FontAsset font, string chinese, string english)
    {
        RectTransform r = PropBarView.MakeRect(name, parent);
        Texture2D texture = Resources.Load<Texture2D>("GameLayoutV1/Glyphs");
        TextAsset metrics = Resources.Load<TextAsset>("GameLayoutV1/GlyphMetrics");
        if (texture != null && metrics != null)
        {
            var text = r.gameObject.AddComponent<PuppyVictoryGlyphText>();
            text.Configure(texture, metrics); text.color = Ink; text.SetText(chinese);
        }
        else
        {
            var text = r.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = PropBarView.Choose(font, chinese, english);
            text.color = Ink; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        }
        return r;
    }
    public static void LabelSize(RectTransform r, string chinese, float size)
    {
        var bitmap = r.GetComponent<PuppyVictoryGlyphText>();
        if (bitmap != null) bitmap.SetText(chinese, size);
        var tmp = r.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.fontSize = size;
    }
    public static void Place(RectTransform r, float x, float y, float width, float height)
    {
        r.anchorMin = r.anchorMax = new Vector2(.5f, 0f);
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(width, height);
    }
    public static void NavigationButton(RectTransform buttonRect, GameUiGraphic.Kind icon)
    {
        // Keep ButtonPressFeedback's stable hit target and shrinking visual child.
        Transform visual = buttonRect.Find("ButtonVisual");
        if (visual == null) return;
        Image old = visual.GetComponent<Image>();
        if (old != null) old.enabled = false;
        // Image and GameUiGraphic both derive from Graphic: even a disabled
        // Image forbids a second Graphic on the same GameObject.
        RectTransform surface = visual.Find("NavigationSurface") as RectTransform;
        if (surface == null) surface = PropBarView.MakeRect("NavigationSurface", visual);
        PropBarView.Stretch(surface);
        surface.SetAsFirstSibling();
        var panel = surface.GetComponent<GameUiGraphic>();
        if (panel == null) panel = surface.gameObject.AddComponent<GameUiGraphic>();
        panel.CornerRatio = .5f; panel.color = new Color(1f, .98f, .95f); panel.raycastTarget = false;
        // Soft beige outline (style A).
        RectTransform ringRect = visual.Find("NavigationRing") as RectTransform;
        if (ringRect == null) ringRect = PropBarView.MakeRect("NavigationRing", visual);
        PropBarView.Stretch(ringRect);
        ringRect.SetSiblingIndex(surface.GetSiblingIndex() + 1);
        var ring = ringRect.GetComponent<GameUiGraphic>();
        if (ring == null) ring = ringRect.gameObject.AddComponent<GameUiGraphic>();
        ring.Shape = GameUiGraphic.Kind.Ring; ring.color = new Color(.918f, .859f, .776f); ring.raycastTarget = false;
        buttonRect.GetComponent<Button>().targetGraphic = panel;
        RectTransform symbol = visual.Find("Icon") as RectTransform;
        if (symbol == null) symbol = PropBarView.MakeRect("Icon", visual);
        symbol.anchorMin = Vector2.one * .19f; symbol.anchorMax = Vector2.one * .81f;
        symbol.sizeDelta = symbol.anchoredPosition = Vector2.zero;
        string art = icon == GameUiGraphic.Kind.Back ? "nav_back" : icon == GameUiGraphic.Kind.Gear ? "nav_gear"
            : icon == GameUiGraphic.Kind.Exclamation ? "nav_rules" : null;
        Texture2D texture = art != null ? Resources.Load<Texture2D>("UiIcons/" + art) : null;
        if (texture != null && symbol.GetComponent<GameUiGraphic>() == null)
        {
            // Smooth, mip-mapped glyph rendered from the approved style-A SVG.
            var raw = symbol.GetComponent<RawImage>();
            if (raw == null) raw = symbol.gameObject.AddComponent<RawImage>();
            raw.texture = texture; raw.raycastTarget = false;
            return;
        }
        var g = symbol.GetComponent<GameUiGraphic>();
        if (g == null) g = symbol.gameObject.AddComponent<GameUiGraphic>();
        g.Shape = icon; g.color = Ink; g.raycastTarget = false;
    }
}
