using UnityEngine;
using UnityEngine.UI;

// Inner-face animation composes with the existing Symbol/slot scale used by
// line waves and victory. Never resizes/reorders a grid cell or handles input.
public sealed class PuppyAvatarView : MonoBehaviour
{
    public const float ArrivalDuration = .54f;
    private RectTransform face;
    private PuppyPoseGraphic normalImage, expressionImage;
    private PuppyHead3DGraphic modelImage;
    private bool using3D;
    private CanvasGroup faceGroup;
    private Canvas popCanvas;
    private Sprite normal, wink, left, right, blink;
    private bool lookBoth;
    private uint random;
    private float clock, nextLook, lookStart = -1f, arrivalTime;
    private float direction;
    private float followedTurn;
    private bool built, arriving, largeArrival, idleAllowed = true, focused = true, paused;
    private bool victoryDriven;
    public float ArrivalRemaining => arriving ? Mathf.Max(0f, ArrivalDuration - arrivalTime) : 0f;

    public static PuppyAvatarView Attach(Image host, int seed)
    {
        if (host == null || !PuppyAvatarAssets.TryGet(out Sprite n, out Sprite w, out Sprite l, out Sprite r)) return null;
        var view = host.GetComponent<PuppyAvatarView>();
        if (view == null) view = host.gameObject.AddComponent<PuppyAvatarView>();
        view.normal = n; view.wink = w; view.left = l; view.right = r;
        view.blink = PuppyAvatarAssets.Blink;
        view.random = (uint)(seed + 7919) * 747796405u + 2891336453u;
        host.sprite = n; host.color = Color.white;
        // Do not hide the original Image until both animated layers exist.
        // A stale reference after recompilation must not erase a placed animal.
        if (!view.EnsureVisuals()) return null;
        view.focused = Application.isFocused; view.built = true; view.ResetVisual();
        return view;
    }
    private bool HasVisuals => normal != null && face != null && faceGroup != null &&
        normalImage != null && normalImage.sprite != null && expressionImage != null;

    private bool EnsureVisuals()
    {
        Image host = GetComponent<Image>();
        if (host == null) return false;
        if (normal == null && !PuppyAvatarAssets.TryGet(out normal, out wink, out left, out right))
        {
            host.enabled = true;
            return false;
        }
        if (wink == null || left == null || right == null)
            PuppyAvatarAssets.TryGet(out normal, out wink, out left, out right);
        if (blink == null) blink = PuppyAvatarAssets.Blink;
        host.sprite = normal; host.color = Color.white;
        if (face == null) face = transform.Find("AnimatedPuppyFace") as RectTransform;
        if (face == null) face = PropBarView.MakeRect("AnimatedPuppyFace", transform);
        PropBarView.Stretch(face);
        if (faceGroup == null) faceGroup = face.GetComponent<CanvasGroup>();
        if (faceGroup == null) faceGroup = face.gameObject.AddComponent<CanvasGroup>();
        faceGroup.blocksRaycasts = false; faceGroup.interactable = false;
        normalImage = MakeImage("Normal", normal);
        expressionImage = MakeImage("Expression", wink != null ? wink : normal);
        if (normalImage == null || expressionImage == null)
        {
            face.gameObject.SetActive(false);
            host.enabled = true; // Static approved puppy is the safe fallback.
            return false;
        }
        face.gameObject.SetActive(true);
        normalImage.color = Color.white; normalImage.enabled = true;
        expressionImage.enabled = false;
        Configure3D();
        host.enabled = false;
        return true;
    }
    private void Configure3D()
    {
        bool wanted = PuppyAvatarPresentation.Use3D && PuppyHead3DGraphic.Available;
        if (wanted && modelImage == null)
        {
            RectTransform rect = face.Find("Experimental3DHead") as RectTransform;
            if (rect == null) rect = PropBarView.MakeRect("Experimental3DHead", face);
            PropBarView.Stretch(rect);
            modelImage = rect.GetComponent<PuppyHead3DGraphic>();
            if (modelImage == null) modelImage = rect.gameObject.AddComponent<PuppyHead3DGraphic>();
            modelImage.raycastTarget = false; modelImage.color = Color.white;
        }
        using3D = wanted && modelImage != null;
        normalImage.enabled = !using3D;
        expressionImage.enabled = false;
        if (modelImage != null)
        {
            modelImage.enabled = using3D;
            if (using3D) modelImage.SetPose(0f);
        }
    }
    private PuppyPoseGraphic MakeImage(string name, Sprite sprite)
    {
        RectTransform r = face.Find(name) as RectTransform;
        if (r == null) r = PropBarView.MakeRect(name, face);
        PropBarView.Stretch(r);
        // Also covers a runtime object created before RequireComponent existed.
        if (r.GetComponent<CanvasRenderer>() == null) r.gameObject.AddComponent<CanvasRenderer>();
        PuppyPoseGraphic image = r.GetComponent<PuppyPoseGraphic>();
        if (image == null)
        {
            if (r.GetComponent<Graphic>() != null) return null;
            image = r.gameObject.AddComponent<PuppyPoseGraphic>();
        }
        if (image == null) return null;
        image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }
    private float Random01()
    {
        random ^= random << 13; random ^= random >> 17; random ^= random << 5;
        return (random & 0x00ffffffu) / 16777215f;
    }
    private void ScheduleLook() { nextLook = clock + 4.5f + Random01() * 4.5f; }
    public void SetIdleAllowed(bool value)
    {
        if (idleAllowed == value) return;
        idleAllowed = value;
        // Finishing the round stops idle looks but preserves the last reveal.
        if (!arriving) ResetVisual();
    }
    public void PlayArrival(bool exceedCell = true)
    {
        if (!built || !isActiveAndEnabled) return;
        if (!HasVisuals && !EnsureVisuals()) return;
        if (using3D != (PuppyAvatarPresentation.Use3D && PuppyHead3DGraphic.Available) || (using3D && modelImage == null))
        {
            Configure3D(); ResetVisual();
        }
        ResetVisual();
        largeArrival = exceedCell; arriving = true; arrivalTime = 0f;
        if (exceedCell)
        {
            // A temporary non-interactive sorting canvas avoids changing the
            // grid child order when the face briefly extends over neighbours.
            Canvas parent = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
            if (parent != null)
            {
                if (popCanvas == null) popCanvas = gameObject.AddComponent<Canvas>();
                popCanvas.overrideSorting = true; popCanvas.sortingLayerID = parent.rootCanvas.sortingLayerID;
                popCanvas.sortingOrder = parent.rootCanvas.sortingOrder + 1; popCanvas.enabled = true;
            }
        }
        ApplyArrival();
    }
    private void Update()
    {
        if (victoryDriven) return; // BoardVictoryFeedback supplies the shared paused/focus-safe clock.
        if (!built || !focused || paused) return;
        if (!HasVisuals && !EnsureVisuals()) return;
        if (using3D != (PuppyAvatarPresentation.Use3D && PuppyHead3DGraphic.Available) || (using3D && modelImage == null))
        {
            Configure3D(); ResetVisual();
        }
        float delta = Mathf.Min(Time.unscaledDeltaTime, .05f);
        clock += delta;
        if (arriving)
        {
            arrivalTime += delta; ApplyArrival();
            if (arrivalTime >= ArrivalDuration) ResetVisual();
            return;
        }
        if (!idleAllowed) return;
        if (lookStart < 0f && clock >= nextLook)
        { lookStart = clock; direction = Random01() < .5f ? -1f : 1f; lookBoth = Random01() < .55f; }
        if (lookStart < 0f) return;
        float t = clock - lookStart;
        float first = Smooth(t / .34f) * (1f - Smooth((t - .80f) / .32f));
        float second = lookBoth ? Smooth((t - 1.35f) / .34f) * (1f - Smooth((t - 2.0f) / .34f)) : 0f;
        float turn = direction * (first - second) * .65f;
        followedTurn = Mathf.Lerp(followedTurn, turn, 1f - Mathf.Exp(-delta * 16f));
        face.localRotation = Quaternion.Euler(0f, 0f, -turn * 1.8f);
        face.anchoredPosition = new Vector2(turn * face.rect.width * .009f, -Mathf.Abs(turn) * face.rect.height * .004f);
        float close = Smooth((t - 1.14f) / .045f) * (1f - Smooth((t - 1.25f) / .065f));
        if (blink != null && Mathf.Abs(turn) < .02f && close > .001f) Expression(blink, close);
        else HeadPose(turn < 0f ? -1 : 1, Mathf.Abs(turn), followedTurn - turn);
        if (t >= (lookBoth ? 2.40f : 1.44f)) ResetVisual();
    }
    private void ApplyArrival()
    {
        float t = arrivalTime;
        float grow = Smooth(t / .09f);
        // Hold the small overshoot long enough to read the wink; the return
        // starts near the end of the closed-eye beat rather than immediately.
        float settle = Smooth((t - .27f) / .23f);
        float peak = largeArrival ? 1.28f : 1.045f;
        float scale = t < .09f ? Mathf.Lerp(.86f, peak, grow) : Mathf.Lerp(peak, 1f, settle);
        face.localScale = Vector3.one * scale;
        face.anchoredPosition = Vector2.up * face.rect.height * (largeArrival ? .018f : .006f) * grow * (1f - settle);
        float tilt = Smooth((t - .08f) / .075f) * (1f - Smooth((t - .28f) / .20f));
        face.localRotation = Quaternion.Euler(0f, 0f, (largeArrival ? -7f : -3f) * tilt);
        faceGroup.alpha = Smooth(t / .035f);
        float close = Smooth((t - .14f) / .05f) * (1f - Smooth((t - .26f) / .075f));
        Expression(wink, close);
    }
    private void Expression(Sprite sprite, float amount)
    {
        if (using3D && modelImage != null)
        {
            modelImage.SetPose(0f, sprite == blink ? amount : 0f, sprite == wink ? amount : 0f);
            return;
        }
        normalImage.sprite = normal; normalImage.enabled = true;
        normalImage.SetDepthPose(0f, sprite == blink ? amount : 0f, sprite == wink ? amount : 0f);
        expressionImage.enabled = false;
    }
    private void HeadPose(int side, float amount, float follow = 0f)
    {
        if (using3D && modelImage != null)
        {
            modelImage.SetPose(side * amount * 22f);
            return;
        }
        normalImage.sprite = normal; normalImage.enabled = true;
        normalImage.SetDepthPose(side * amount * 12f, 0f, 0f, -amount * .9f, follow);
        expressionImage.enabled = false;
    }
    public bool BeginVictoryPose()
    {
        if (!built || !isActiveAndEnabled || (!HasVisuals && !EnsureVisuals())) return false;
        ResetVisual();
        victoryDriven = true;
        Canvas parent = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
        if (parent != null)
        {
            if (popCanvas == null) popCanvas = gameObject.AddComponent<Canvas>();
            popCanvas.overrideSorting = true;
            popCanvas.sortingLayerID = parent.rootCanvas.sortingLayerID;
            popCanvas.sortingOrder = parent.rootCanvas.sortingOrder + 1;
            popCanvas.enabled = true;
        }
        return true;
    }
    public bool ApplyVictoryPose(float time)
    {
        if (!victoryDriven && !BeginVictoryPose()) return false;
        if (!HasVisuals) return false;
        float scale = BoardVictoryTiming.AvatarScale(time);
        Quaternion rotation = Quaternion.Euler(0f, 0f, -BoardVictoryTiming.Tilt(time));
        // Rotate around a virtual lower-neck pivot without changing any anchors.
        Vector3 neck = Vector3.up * face.rect.height * .22f;
        face.localScale = Vector3.one * scale;
        face.localRotation = rotation;
        face.anchoredPosition = rotation * neck - neck;
        faceGroup.alpha = BoardVictoryTiming.Hidden(time) ? 0f : 1f;
        bool close = BoardVictoryTiming.Wink(time);
        if (using3D && modelImage != null) modelImage.SetPose(0f, 0f, close ? 1f : 0f);
        else
        {
            // Use the same approved expression image as the preview. No crossfade ghosting.
            bool expression = close && wink != null;
            normalImage.enabled = !expression;
            normalImage.sprite = normal;
            normalImage.SetDepthPose(0f);
            expressionImage.enabled = expression;
            expressionImage.sprite = expression ? wink : normal;
            expressionImage.SetDepthPose(0f);
        }
        return true;
    }
    public void EndVictoryPose()
    {
        if (victoryDriven) ResetVisual();
    }
    public void ResetVisual()
    {
        victoryDriven = false;
        if (!built) return;
        arriving = false; arrivalTime = 0f; lookStart = -1f; followedTurn = 0f;
        // Disabling/destroying children or a script reload can invalidate a
        // layer before the host receives its lifecycle callback.
        if (face != null)
        {
            face.localScale = Vector3.one; face.localRotation = Quaternion.identity;
            face.anchoredPosition = Vector2.zero;
        }
        if (faceGroup != null) faceGroup.alpha = 1f;
        if (expressionImage != null)
        {
            expressionImage.enabled = false; expressionImage.SetTurn(0, 0f, false);
        }
        if (normalImage != null)
        {
            normalImage.enabled = !using3D;
            normalImage.sprite = normal;
            normalImage.SetTurn(0, 0f, false);
        }
        if (modelImage != null) modelImage.SetPose(0f);
        if (popCanvas != null) popCanvas.overrideSorting = false;
        ScheduleLook();
    }
    private static float Smooth(float p) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p));
    private void OnEnable()
    {
        focused = Application.isFocused; paused = false;
        if (built && !HasVisuals) EnsureVisuals();
        ResetVisual();
    }
    private void OnDisable() => ResetVisual();
    private void OnApplicationFocus(bool value) { focused = value; if (!value) ResetVisual(); }
    private void OnApplicationPause(bool value) { paused = value; if (value) ResetVisual(); }
}
