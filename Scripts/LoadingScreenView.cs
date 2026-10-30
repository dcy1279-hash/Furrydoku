using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Opening loading screen: the game name/logo on top, one random friendly line under it,
// and a rounded progress bar at the bottom with the puppy head riding its tip.
// Covers and blocks the game while shown, stays at least MinSeconds, then fades out.
// The logo is Resources/Branding/game_logo.png when present, otherwise a text placeholder.
public sealed class LoadingScreenView : MonoBehaviour
{
    private const float MinSeconds = 2.2f, FadeSeconds = .35f;
    private const string PlaceholderName = "Furrydoku";
    private static readonly string[] Lines =
    {
        "Every puppy has its perfect spot.",
        "A few minutes of puzzles a day keeps your mind sharp.",
        "Take a deep breath — there's no timer here.",
        "Puppies love a calm, clever friend.",
        "Tip: swipe to cross out many cells at once.",
        "Tip: double-tap a cell to place a puppy.",
        "Every puzzle has exactly one answer — no guessing needed.",
        "Stuck? The bulb shows you the next logical step.",
    };
    private static readonly Color Brown = new Color(.47f, .31f, .235f);
    private static readonly Color Track = new Color(1f, 1f, 1f), TrackEdge = new Color(.918f, .859f, .776f);
    private static readonly Color Fill = new Color(1f, .70f, .36f);

    private RectTransform root, fill, rider;
    private CanvasGroup group;
    private float elapsed, fadeElapsed = -1f;
    private Action done;
    private static LoadingScreenView active;
    public static bool IsShowing => active != null;   // e.g. hold the level-start sound until it is gone

    public static LoadingScreenView Show(Transform canvasRoot, TMP_FontAsset fallbackFont, Action onDone)
    {
        if (canvasRoot == null) { onDone?.Invoke(); return null; }
        RectTransform root = PropBarView.MakeRect("LoadingScreen", canvasRoot);
        PropBarView.Stretch(root);
        root.SetAsLastSibling();
        var view = root.gameObject.AddComponent<LoadingScreenView>();
        active = view;
        view.root = root; view.done = onDone;
        view.group = root.gameObject.AddComponent<CanvasGroup>();
        var page = root.gameObject.AddComponent<Image>();
        page.color = GameUiStyle.PageBackground; page.raycastTarget = true; // blocks the game underneath
        view.Build(GameUiStyle.RuntimeFont("ArialRoundedBold", fallbackFont),
            GameUiStyle.RuntimeFont("SFRounded-Heavy", fallbackFont));
        return view;
    }

    private void Build(TMP_FontAsset font, TMP_FontAsset heavy)
    {
        // Random friendly line in the upper middle.
        var line = Text("Line", font, 50f, Brown);
        line.text = "“" + Lines[UnityEngine.Random.Range(0, Lines.Length)] + "”";
        Place(line.rectTransform, new Vector2(.5f, .45f), new Vector2(860f, 240f)); // under the name

        // Progress bar: white track with a beige rim, warm orange fill, puppy head on the tip.
        RectTransform bar = PropBarView.MakeRect("ProgressBar", root);
        Place(bar, new Vector2(.5f, .17f), new Vector2(720f, 44f)); // loading bar at the bottom
        RectTransform rim = GameUiStyle.Panel("Rim", bar, TrackEdge, .5f);
        PropBarView.Stretch(rim);
        RectTransform track = GameUiStyle.Panel("Track", bar, Track, .5f);
        PropBarView.Stretch(track); track.offsetMin = new Vector2(5f, 5f); track.offsetMax = new Vector2(-5f, -5f);
        fill = GameUiStyle.Panel("Fill", track, Fill, .5f);
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, .5f);
        fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
        rider = PropBarView.MakeRect("PuppyRider", bar);
        rider.anchorMin = rider.anchorMax = new Vector2(0f, .5f);
        rider.sizeDelta = Vector2.one * 120f;
        var head = rider.gameObject.AddComponent<Image>();
        if (PuppyAvatarAssets.TryGet(out Sprite face, out _, out _, out _)) head.sprite = face;
        else head.enabled = false;
        head.preserveAspect = true; head.raycastTarget = false;

        // Logo at the bottom: artwork when provided, otherwise a rounded text placeholder.
        Texture2D logo = Resources.Load<Texture2D>("Branding/game_logo");
        if (logo != null)
        {
            RectTransform r = PropBarView.MakeRect("Logo", root);
            Place(r, new Vector2(.5f, .71f), new Vector2(880f, 880f * logo.height / Mathf.Max(1f, logo.width))); // name on top
            var image = r.gameObject.AddComponent<RawImage>();
            image.texture = logo; image.raycastTarget = false;
        }
        else
        {
            var title = Text("LogoText", heavy, 120f, Brown);
            title.text = PlaceholderName;
            title.outlineWidth = .18f; title.outlineColor = Color.white;
            Place(title.rectTransform, new Vector2(.5f, .66f), new Vector2(940f, 220f)); // name on top
        }
        SetProgress(0f);
    }

    private TextMeshProUGUI Text(string name, TMP_FontAsset font, float size, Color colour)
    {
        RectTransform r = PropBarView.MakeRect(name, root);
        var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font; t.fontSize = size; t.color = colour; t.raycastTarget = false;
        t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    private static void Place(RectTransform r, Vector2 anchor, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(.5f, .5f);
        r.anchoredPosition = Vector2.zero; r.sizeDelta = size;
    }

    private void SetProgress(float p)
    {
        p = Mathf.Clamp01(p);
        fill.anchorMax = new Vector2(p, 1f);
        float width = ((RectTransform)rider.parent).rect.width;
        float bob = Mathf.Sin(Time.unscaledTime * 9f) * 6f;
        rider.anchoredPosition = new Vector2(Mathf.Lerp(20f, width - 20f, p), 52f + Mathf.Abs(bob));
        rider.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 9f) * 6f);
    }

    private void Update()
    {
        // Count shown time frame by frame, at most 1/20 s per frame: a startup hitch (first
        // texture uploads) must not make the bar jump to the end before anyone saw it.
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        elapsed += dt;
        // Eased fill over the minimum time; the game itself is already built underneath.
        float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / MinSeconds), 2.2f);
        SetProgress(p);
        if (fadeElapsed < 0f && elapsed >= MinSeconds) fadeElapsed = 0f;
        if (fadeElapsed < 0f) return;
        fadeElapsed += dt;
        float k = fadeElapsed / FadeSeconds;
        group.alpha = 1f - Mathf.Clamp01(k);
        if (k < 1f) return;
        Action callback = done; done = null;
        if (active == this) active = null;
        Destroy(gameObject);
        callback?.Invoke();
    }

    private void OnDestroy() { if (active == this) active = null; }
}
