using UnityEngine;

// Safe Area（安全显示区域）。只用于主显示器的 Screen Space - Overlay（屏幕空间覆盖）画布。
// 父对象可以是根 Canvas 或全屏遮罩；不把安全区套到背景上，也不硬编码刘海高度。
[DefaultExecutionOrder(-300)]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform area;
    private RectTransform parentRect;

    public static SafeAreaFitter Create(RectTransform parent, string objectName)
    {
        Canvas canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
        if (!Application.isPlaying || parent == null || canvas == null ||
            canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ||
            canvas.rootCanvas.targetDisplay != 0)
        {
            Debug.LogError("SafeAreaFitter：当前适配需要主显示器的 Screen Space - Overlay（屏幕空间覆盖）画布。");
            return null;
        }

        var root = new GameObject(objectName, typeof(RectTransform));
        root.layer = parent.gameObject.layer;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        SafeAreaFitter fitter = root.AddComponent<SafeAreaFitter>();
        fitter.area = rect;
        fitter.parentRect = parent;
        return fitter;
    }

    // 返回 false 时不把瞬时无效窗口尺寸当成新布局。
    public bool RefreshNow()
    {
        if (area == null || parentRect == null ||
            Screen.width <= 0 || Screen.height <= 0)
            return false;
        Rect safe = Screen.safeArea;
        Rect parentBounds = parentRect.rect;
        if (!IsPositiveFinite(safe.width) || !IsPositiveFinite(safe.height) ||
            !IsFinite(safe.x) || !IsFinite(safe.y) ||
            !IsPositiveFinite(parentBounds.width) || !IsPositiveFinite(parentBounds.height))
            return false;

        Vector2 screenMin = new Vector2(
            Mathf.Clamp(safe.xMin, 0f, Screen.width),
            Mathf.Clamp(safe.yMin, 0f, Screen.height));
        Vector2 screenMax = new Vector2(
            Mathf.Clamp(safe.xMax, 0f, Screen.width),
            Mathf.Clamp(safe.yMax, 0f, Screen.height));
        if (screenMax.x <= screenMin.x || screenMax.y <= screenMin.y)
            return false;

        // Overlay 的 camera 参数必须为 null；用坐标换算兼容 Canvas Scaler（画布缩放）。
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screenMin, null, out Vector2 localMin) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screenMax, null, out Vector2 localMax))
            return false;

        Vector2 min = new Vector2(
            Mathf.Clamp01((localMin.x - parentBounds.xMin) / parentBounds.width),
            Mathf.Clamp01((localMin.y - parentBounds.yMin) / parentBounds.height));
        Vector2 max = new Vector2(
            Mathf.Clamp01((localMax.x - parentBounds.xMin) / parentBounds.width),
            Mathf.Clamp01((localMax.y - parentBounds.yMin) / parentBounds.height));
        if (!IsFinite(min.x) || !IsFinite(min.y) || !IsFinite(max.x) || !IsFinite(max.y) ||
            max.x <= min.x || max.y <= min.y)
            return false;

        if (area.anchorMin != min) area.anchorMin = min;
        if (area.anchorMax != max) area.anchorMax = max;
        if (area.offsetMin != Vector2.zero) area.offsetMin = Vector2.zero;
        if (area.offsetMax != Vector2.zero) area.offsetMax = Vector2.zero;
        return true;
    }

    private void LateUpdate() => RefreshNow();

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsPositiveFinite(float value) => IsFinite(value) && value > 0f;
}
