using System;
using UnityEngine;
using UnityEngine.UI;

// Failure presentation only. The crying puppy is a pre-rendered frame sequence
// (FailureDogV2: intro once, then a seamless loop, 24 fps; tears are in the frames).
// Falls back to the old sprite that drops in and sobs with code-drawn tears.
// No lives/answers/saves are changed.
public sealed class PuppyFailureView : MonoBehaviour
{
    private const string Path = "FailureDogV1/";
    private const string FramesPath = "FailureDogV2/";
    private const float FrameRate = 24f;
    private Texture2D[] introFrames, loopFrames;
    private RawImage picture;
    private float frameAspect = 1f;   // width / height of the animation frames
    private const float ButtonReadyTime = 1.12f;
    private RectTransform safeParent, design, dogRoot, titleRect, countRect, buttonRect;
    private RawImage halo;
    private CanvasGroup dogGroup, titleGroup, countGroup, buttonGroup;
    private readonly RawImage[] tears = new RawImage[6];
    private PuppyVictoryGlyphText countText;
    private Button retryButton;
    private Sprite buttonSprite;
    private Action onRetry;
    private Vector2 lastSize = new Vector2(-1f, -1f);
    private float elapsed, designHeight = 780f, dogSize;
    private bool running, focused = true, paused, allowAction;
    public float EntranceAlpha => Smooth(elapsed / .32f);

    public static PuppyFailureView TryCreate(RectTransform safeParent)
    {
        if (safeParent == null) return null;
        Texture2D dog = Resources.Load<Texture2D>(Path + "DogCrying");
        Texture2D tear = Resources.Load<Texture2D>(Path + "Tear");
        Texture2D glyphs = Resources.Load<Texture2D>(Path + "FailureGlyphs");
        TextAsset metrics = Resources.Load<TextAsset>(Path + "FailureGlyphMetrics");
        Texture2D button = Resources.Load<Texture2D>("VictoryDogV4/Button");
        Texture2D dot = Resources.Load<Texture2D>("VictoryDogV4/SoftDot");
        if (dog == null || tear == null || glyphs == null || metrics == null || button == null || dot == null)
        {
            Debug.LogWarning("PuppyFailureView：失败素材不完整，保留原结算显示。");
            return null;
        }
        RectTransform root = MakeRect("PuppyFailureV1", safeParent);
        Stretch(root);
        var view = root.gameObject.AddComponent<PuppyFailureView>();
        view.safeParent = safeParent;
        // First frames only; the full clips stream in via DogFrameLibrary and are taken in Show().
        Texture2D introFirst = DogFrameLibrary.First(FramesPath + "intro"), loopFirst = DogFrameLibrary.First(FramesPath + "loop");
        view.introFrames = introFirst != null ? new[] { introFirst } : null;
        view.loopFrames = loopFirst != null ? new[] { loopFirst } : null;
        if (view.introFrames == null || view.loopFrames == null) view.introFrames = view.loopFrames = null;
        else view.frameAspect = view.introFrames[0].width / (float)view.introFrames[0].height;
        try { view.Build(dog, tear, glyphs, metrics, button, dot); view.RefreshLayout(true); }
        catch (Exception error)
        {
            root.gameObject.SetActive(false); Destroy(root.gameObject);
            Debug.LogException(error); return null;
        }
        root.gameObject.SetActive(false);
        return view;
    }

    private void Build(Texture2D dog, Texture2D tear, Texture2D glyphs, TextAsset metrics,
        Texture2D buttonTexture, Texture2D dot)
    {
        design = MakeRect("FailureDesign", transform);
        design.anchorMin = design.anchorMax = design.pivot = new Vector2(.5f, .5f);
        halo = MakeImage("QuietBlueHalo", design, dot);
        halo.color = new Color(.62f, .72f, 1f, .16f);
        dogRoot = MakeRect("DogDropAndSob", design);
        dogGroup = dogRoot.gameObject.AddComponent<CanvasGroup>();
        picture = MakeImage("SadPuppy", dogRoot, Animated ? introFrames[0] : dog);
        Stretch(picture.rectTransform);
        for (int i = 0; i < tears.Length; i++) tears[i] = MakeImage("MovingTear_" + i, dogRoot, tear);
        if (Animated) foreach (RawImage t in tears) t.gameObject.SetActive(false); // tears are in the frames

        titleRect = MakeRect("FailureTitle", design);
        PuppyVictoryGlyphText title = titleRect.gameObject.AddComponent<PuppyVictoryGlyphText>();
        title.Configure(glyphs, metrics); title.SetText("So close!", 33f);
        title.color = new Color(1f, .90f, .86f, 1f);
        titleGroup = titleRect.gameObject.AddComponent<CanvasGroup>();

        countRect = MakeRect("AnimalsStillMissing", design);
        countText = countRect.gameObject.AddComponent<PuppyVictoryGlyphText>();
        countText.Configure(glyphs, metrics);
        countText.color = new Color(1f, .95f, .87f, 1f);
        countGroup = countRect.gameObject.AddComponent<CanvasGroup>();

        buttonRect = MakeRect("RetryButton", design);
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonSprite = Sprite.Create(buttonTexture, new Rect(0, 0, buttonTexture.width, buttonTexture.height),
            new Vector2(.5f, .5f), 100f);
        buttonSprite.name = "PuppyFailureRetryButton";
        buttonImage.sprite = buttonSprite; buttonImage.raycastTarget = true;
        retryButton = buttonRect.gameObject.AddComponent<Button>(); retryButton.targetGraphic = buttonImage;
        Navigation nav = retryButton.navigation; nav.mode = Navigation.Mode.None; retryButton.navigation = nav;
        ColorBlock colors = retryButton.colors;
        colors.normalColor = colors.highlightedColor = colors.selectedColor = Color.white;
        colors.pressedColor = new Color(.92f, .92f, .92f, 1f);
        colors.disabledColor = new Color(1f, 1f, 1f, .75f); retryButton.colors = colors;
        RectTransform labelRect = MakeRect("RetryLabel", buttonRect);
        Stretch(labelRect);
        PuppyVictoryGlyphText label = labelRect.gameObject.AddComponent<PuppyVictoryGlyphText>();
        label.Configure(glyphs, metrics); label.SetText("Try again", 24f);
        buttonGroup = buttonRect.gameObject.AddComponent<CanvasGroup>();
        retryButton.onClick.AddListener(HandleRetry);
        ButtonPressFeedback.Attach(retryButton);
    }

    public void Show(int missingAnimals, Action retry)
    {
        if (Animated)
        {
            introFrames = DogFrameLibrary.Get(FramesPath + "intro") ?? introFrames;
            loopFrames = DogFrameLibrary.Get(FramesPath + "loop") ?? loopFrames;
        }
        int missing = Mathf.Clamp(missingAnimals, 0, 10);
        countText.SetText(missing + (missing == 1 ? " puppy left to find" : " puppies left to find"), 27f);
        elapsed = 0f; onRetry = retry; running = true; allowAction = true;
        focused = Application.isFocused; paused = false;
        gameObject.SetActive(true); transform.SetAsLastSibling();
        RefreshLayout(true); ApplyAt(0f);
    }
    public void Hide()
    {
        running = false; onRetry = null;
        if (retryButton != null) retryButton.interactable = false;
        gameObject.SetActive(false);
    }
    public void SetInteractable(bool value)
    {
        allowAction = value;
        if (retryButton != null) retryButton.interactable = value && running &&
            onRetry != null && elapsed >= ButtonReadyTime;
    }
    private void HandleRetry()
    {
        if (!running || !allowAction || elapsed < ButtonReadyTime || onRetry == null) return;
        Action callback = onRetry;
        allowAction = false; retryButton.interactable = false;
        callback(); // Original ResultPanelView owns retry and duplicate-submit protection.
    }

    private void LateUpdate()
    {
        if (!running) return;
        RefreshLayout(false);
        if (focused && !paused) elapsed += Mathf.Min(Time.unscaledDeltaTime, .06f);
        ApplyAt(elapsed);
    }

    private void RefreshLayout(bool force)
    {
        if (safeParent == null || design == null) return;
        Vector2 size = safeParent.rect.size;
        if (size.x <= 0f || size.y <= 0f || (!force && size == lastSize)) return;
        lastSize = size;
        designHeight = size.y / size.x > 2.1f ? 840f : 780f;
        design.sizeDelta = new Vector2(390f, designHeight);
        design.localScale = Vector3.one * Mathf.Min(size.x / 390f, size.y / designHeight);
        design.anchoredPosition = Vector2.zero;
        dogSize = Mathf.Min(350f, designHeight * .43f);
        if (Animated)
        {
            // Bigger than the old sprite: the frames carry their own motion and tears.
            float h = designHeight * .50f;
            dogRoot.sizeDelta = new Vector2(Mathf.Min(370f, h * frameAspect), h);
        }
        else dogRoot.sizeDelta = new Vector2(dogSize, dogSize);
        Place(halo.rectTransform, 195f, designHeight * .45f, dogSize * 1.4f, dogSize * 1.45f);
        Place(titleRect, 195f, designHeight * .135f, 360f, 62f);
        countRect.sizeDelta = new Vector2(370f, 64f);
        buttonRect.sizeDelta = new Vector2(304f, 68f);
    }

    private bool Animated => introFrames != null && loopFrames != null;

    private void ApplyAt(float t)
    {
        if (Animated) { ApplyFrames(t); ApplyText(t); return; }
        float p = Mathf.Clamp01((t - .04f) / .62f);
        float ease = 1f - Mathf.Pow(1f - p, 3f);
        float landed = Mathf.Clamp01((t - .66f) / .22f);
        float settle = t >= .66f && t < .88f ? Mathf.Sin(landed * Mathf.PI) : 0f;
        float cryTime = Mathf.Max(0f, t - .90f);
        float cryFade = Smooth((t - .90f) / .30f);
        float sob = Mathf.Pow(.5f + .5f * Mathf.Sin(cryTime * Mathf.PI * 2f / 1.45f), 3f) * cryFade;
        SetPoint(dogRoot, 195f, Mathf.Lerp(-dogSize * .65f, designHeight * .44f, ease) + sob * 1.2f);
        dogRoot.localScale = new Vector3(1f + settle * .035f + sob * .006f,
            1f - settle * .04f - sob * .012f, 1f);
        dogGroup.alpha = Smooth((t - .04f) / .18f);
        // Eye anchors measured on this uncut source sprite, not on the happy
        // victory head. Tears remain children of the same moving dog canvas.
        for (int i = 0; i < tears.Length; i++)
        {
            bool right = i >= 3;
            float age = ((cryTime + (i % 3) * .37f + (right ? .16f : 0f)) % 1.15f) / 1.15f;
            float x = (right ? .622f : .382f) + (right ? 1f : -1f) * .012f * age;
            float y = (right ? .315f : .296f) + .014f + .135f * age * age;
            RectTransform tear = tears[i].rectTransform;
            tear.sizeDelta = new Vector2(dogSize * .014f, dogSize * .021f);
            // These child anchors use the top-left image corner.
            tear.anchorMin = tear.anchorMax = new Vector2(0f, 1f);
            SetPoint(tear, x * dogSize, y * dogSize);
            float alpha = cryFade * Smooth(age / .12f) * (1f - Smooth((age - .62f) / .38f));
            tears[i].color = new Color(1f, 1f, 1f, .88f * alpha);
        }
        ApplyText(t);
    }

    // Frame-sequence puppy: soft fade and small rise, intro once then the loop forever.
    private void ApplyFrames(float t)
    {
        int index = Mathf.FloorToInt(t * FrameRate);
        picture.texture = index < introFrames.Length ? introFrames[index]
            : loopFrames[(index - introFrames.Length) % loopFrames.Length];
        float rise = 1f - Smooth(t / .35f);
        SetPoint(dogRoot, 195f, designHeight * .43f + rise * 18f);
        dogRoot.localScale = Vector3.one;
        dogGroup.alpha = Smooth(t / .25f);
    }

    private void ApplyText(float t)
    {
        titleGroup.alpha = Smooth((t - .18f) / .30f);
        countGroup.alpha = Smooth((t - .58f) / .30f);
        SetPoint(countRect, 195f, designHeight * .70f + 8f * (1f - countGroup.alpha));
        buttonGroup.alpha = Smooth((t - .82f) / .30f);
        SetPoint(buttonRect, 195f, designHeight * .86f + 8f * (1f - buttonGroup.alpha));
        retryButton.interactable = allowAction && running && onRetry != null && t >= ButtonReadyTime;
    }

    private void OnApplicationFocus(bool value) { focused = value; }
    private void OnApplicationPause(bool value) { paused = value; }
    private void OnDisable() { running = false; onRetry = null; if (retryButton != null) retryButton.interactable = false; }
    private void OnDestroy()
    {
        if (retryButton != null) retryButton.onClick.RemoveListener(HandleRetry);
        if (buttonSprite != null) Destroy(buttonSprite);
    }
    private static float Smooth(float p) { p = Mathf.Clamp01(p); return p * p * (3f - 2f * p); }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(.5f, .5f);
        return rect;
    }
    private static RawImage MakeImage(string name, Transform parent, Texture2D texture)
    {
        RectTransform rect = MakeRect(name, parent);
        RawImage image = rect.gameObject.AddComponent<RawImage>(); image.texture = texture; image.raycastTarget = false;
        return image;
    }
    private static void SetPoint(RectTransform rect, float x, float y) { rect.anchoredPosition = new Vector2(x, -y); }
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    { rect.sizeDelta = new Vector2(width, height); SetPoint(rect, x, y); }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
}
