using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 只缩放视觉子容器，根按钮命中范围不缩小，靠边松手仍可点击。
public class ButtonPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IPointerExitHandler, ICancelHandler
{
    private Button button;
    private RectTransform visual;
    private bool pressed;
    private int pointer;
    public static void Attach(Button target)
    {
        if (target == null || target.GetComponent<ButtonPressFeedback>() != null) return;
        Image original = target.GetComponent<Image>();
        if (original == null) return;
        var children = new RectTransform[target.transform.childCount];
        for (int i = 0; i < children.Length; i++) children[i] = target.transform.GetChild(i) as RectTransform;
        RectTransform rect = PropBarView.MakeRect("ButtonVisual", target.transform);
        PropBarView.Stretch(rect);
        Image copy = rect.gameObject.AddComponent<Image>();
        copy.sprite = original.overrideSprite; copy.type = original.type;
        copy.material = original.material; copy.color = original.color;
        copy.preserveAspect = original.preserveAspect; copy.fillCenter = original.fillCenter;
        copy.fillMethod = original.fillMethod; copy.fillAmount = original.fillAmount;
        copy.fillOrigin = original.fillOrigin; copy.fillClockwise = original.fillClockwise;
        copy.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
        copy.maskable = original.maskable; copy.raycastTarget = false;
        foreach (RectTransform child in children)
        {
            if (child == null) continue;
            var min = child.anchorMin; var max = child.anchorMax; var pivot = child.pivot;
            var size = child.sizeDelta; var position = child.anchoredPosition3D;
            var scale = child.localScale; var rotation = child.localRotation;
            child.SetParent(rect, false);
            child.anchorMin = min; child.anchorMax = max; child.pivot = pivot;
            child.sizeDelta = size; child.anchoredPosition3D = position;
            child.localScale = scale; child.localRotation = rotation;
        }
        target.targetGraphic = copy;
        Color hit = original.color; hit.a = .001f; original.color = hit;
        original.canvasRenderer.cullTransparentMesh = false;
        var feedback = target.gameObject.AddComponent<ButtonPressFeedback>();
        feedback.button = target; feedback.visual = rect;
        target.onClick.AddListener(feedback.PlayClick);
    }
    private void PlayClick() { AudioController.Current?.PlaySound(GameSound.UiClick); }
    public void OnPointerDown(PointerEventData data)
    {
        if (pressed || button == null || !button.IsInteractable() ||
            data.button != PointerEventData.InputButton.Left) return;
        pointer = data.pointerId; pressed = true;
    }
    public void OnPointerUp(PointerEventData data) { if (data.pointerId == pointer) pressed = false; }
    public void OnPointerExit(PointerEventData data) { if (data.pointerId == pointer) pressed = false; }
    public void OnCancel(BaseEventData data) { ResetVisual(); }
    private void Update()
    {
        if (visual == null) return;
        if (button == null || !button.IsInteractable()) pressed = false;
        float scale = Mathf.MoveTowards(visual.localScale.x, pressed ? .94f : 1f,
            Time.unscaledDeltaTime * .85f);
        visual.localScale = Vector3.one * scale;
    }
    private void ResetVisual() { pressed = false; if (visual != null) visual.localScale = Vector3.one; }
    private void OnDisable() => ResetVisual();
    private void OnApplicationFocus(bool value) { if (!value) ResetVisual(); }
    private void OnApplicationPause(bool value) { if (value) ResetVisual(); }
    private void OnDestroy() { if (button != null) button.onClick.RemoveListener(PlayClick); }
}
