using UnityEngine;
using UnityEngine.UI;

// 纯表现组件：只动图片缩放与短暂背景亮度，不读答案、不改格子或存档。
// 同一组件一次管理一个弹出；重试/停用/失焦恢复原值，不等待动画决定玩法。
public class CellFeedback : MonoBehaviour
{
    public const float PlacementDuration = .24f;
    public const float ErrorDuration = .20f;
    public const float LifeLossDuration = .22f;
    private RectTransform target;
    private Graphic flashGraphic;
    private Vector3 restingScale = Vector3.one;
    private Color restingColor;
    private float elapsed;
    private bool playing;
    private bool markMode, showing, errorMode;
    private Vector3 restingPosition;
    private float shakeAmplitude;
    private float vanishScale = .85f;
    private System.Action completed;
    private float duration = PlacementDuration;

    public void PlayMark(RectTransform rect, Graphic graphic, bool show, System.Action onComplete)
    {
        ResetFeedback();
        if (!Application.isPlaying || !isActiveAndEnabled || rect == null || graphic == null)
        { onComplete?.Invoke(); return; }
        target = rect; flashGraphic = graphic;
        restingScale = rect.localScale; restingColor = graphic.color;
        markMode = true; showing = show; duration = .11f; completed = onComplete;
        elapsed = 0f; playing = true; ApplyFrame(0f);
    }

    // 永久红叉只短暂位移，不淡出，不修改格子根或颜色。
    public void PlayError(RectTransform markRect)
    {
        ResetFeedback();
        if (!Application.isPlaying || !isActiveAndEnabled || markRect == null ||
            !markRect.gameObject.activeInHierarchy) return;
        target = markRect;
        restingScale = target.localScale;
        restingPosition = target.anchoredPosition3D;
        RectTransform parent = target.parent as RectTransform;
        float side = parent != null ? Mathf.Min(parent.rect.width, parent.rect.height) :
            Mathf.Min(target.rect.width, target.rect.height);
        shakeAmplitude = Mathf.Clamp(side * .035f, 1f, 12f);
        errorMode = true;
        duration = ErrorDuration;
        elapsed = 0f;
        playing = true;
        ApplyFrame(0f);
    }

    // HUD的临时生命视觉片段；稳定的暗色生命槽不缩放/不隐藏。
    public void PlayLifeLoss(RectTransform rect, Graphic graphic, System.Action onComplete)
    {
        PlayMark(rect, graphic, false, onComplete);
        if (!playing) return;
        duration = LifeLossDuration;
        vanishScale = .55f;
        ApplyFrame(0f);
    }

    public void PlayPlace(RectTransform imageRect, Graphic background = null)
    {
        ResetFeedback();
        if (!Application.isPlaying || !isActiveAndEnabled || imageRect == null ||
            !imageRect.gameObject.activeInHierarchy)
            return;

        target = imageRect;
        restingScale = target.localScale;
        flashGraphic = background;
        if (flashGraphic != null) restingColor = flashGraphic.color;
        elapsed = 0f;
        duration = PlacementDuration;
        playing = true;
        ApplyFrame(0f);
    }

    private void Update()
    {
        if (!playing) return;
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            ResetFeedback();
            return;
        }
        elapsed += Time.unscaledDeltaTime; // UI效果独立时间，不延迟落子/保存。
        float progress = Mathf.Clamp01(elapsed / duration);
        ApplyFrame(progress);
        if (progress >= 1f)
        {
            var callback = completed;
            ResetFeedback(); // 取消不调用完成；只有自然结束才执行一次。
            callback?.Invoke();
        }
    }

    private void ApplyFrame(float progress)
    {
        if (target == null) return;
        if (errorMode)
        {
            float displacement = Mathf.Sin(progress * Mathf.PI * 6f) *
                (1f - progress) * shakeAmplitude;
            target.anchoredPosition3D = restingPosition + Vector3.right * displacement;
            return;
        }
        if (markMode)
        {
            target.localScale = restingScale * (showing
                ? Mathf.SmoothStep(.82f, 1f, progress) : Mathf.SmoothStep(1f, vanishScale, progress));
            Color color = restingColor;
            color.a *= showing ? Mathf.Lerp(.65f, 1f, progress) : 1f - progress;
            flashGraphic.color = color;
            return;
        }
        float scale = progress <= .58f
            ? Mathf.SmoothStep(.70f, 1.12f, progress / .58f)
            : Mathf.SmoothStep(1.12f, 1f, (progress - .58f) / .42f);
        target.localScale = restingScale * scale;
        if (flashGraphic != null)
        {
            float pulse = Mathf.Sin(progress * Mathf.PI) * .62f;
            Color white = new Color(1f, 1f, 1f, restingColor.a);
            flashGraphic.color = Color.Lerp(restingColor, white, pulse);
        }
    }

    public void ResetFeedback()
    {
        playing = false;
        markMode = false;
        completed = null;
        elapsed = 0f;
        if (target != null)
        {
            target.localScale = restingScale;
            if (errorMode) target.anchoredPosition3D = restingPosition;
        }
        errorMode = false;
        vanishScale = .85f;
        if (flashGraphic != null) flashGraphic.color = restingColor;
        target = null;
        flashGraphic = null;
    }

    private void OnDisable() => ResetFeedback();
    private void OnDestroy() => ResetFeedback();
    private void OnApplicationFocus(bool focused) { if (!focused) ResetFeedback(); }
    private void OnApplicationPause(bool paused) { if (paused) ResetFeedback(); }
}
