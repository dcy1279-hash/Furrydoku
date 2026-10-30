using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class CellInput : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerExitHandler
{
    [SerializeField, Range(0.15f, 0.5f)]
    private float doubleClickInterval = 0.28f;

    private int index = -1;
    private Action<int> onSingleClick;
    private Action<int> onDoubleClick;
    private Action<int> onPreview;
    private Action<int> onCancelPreview;
    private Action<int, PointerEventData> onDragStart;
    private Action<PointerEventData> onDragMove;
    private Action<PointerEventData> onDragEnd;
    private Action<int,bool> onPressFeedback;
    private bool previewHiddenForSecondPress;

    private bool inputEnabled;
    private bool hasPendingClick;
    private double singleClickDeadline;
    private double ignoreClicksUntil;

    // 同一时刻只接受一根手指/一个鼠标按住手势。
    private static CellInput pointerOwner;
    [NonSerialized] private bool ownsPress;
    [NonSerialized] private bool isDragging;
    private int pointerId;
    private bool hasPressPointer;
    private bool allowClick = true;

    private bool IsReady => isActiveAndEnabled && inputEnabled &&
        index >= 0 && onSingleClick != null;

    public void Initialize(
        int cellIndex,
        Action<int> singleClickHandler,
        Action<int> doubleClickHandler = null,
        Action<int> previewHandler = null,
        Action<int> cancelPreviewHandler = null,
        Action<int, PointerEventData> dragStartHandler = null,
        Action<PointerEventData> dragMoveHandler = null,
        Action<PointerEventData> dragEndHandler = null,
        Action<int,bool> pressFeedbackHandler = null)
    {
        if (cellIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(cellIndex));
        if (singleClickHandler == null)
            throw new ArgumentNullException(nameof(singleClickHandler));

        CancelGesture();
        index = cellIndex;
        onSingleClick = singleClickHandler;
        onDoubleClick = doubleClickHandler;
        onPreview = previewHandler;
        onCancelPreview = cancelPreviewHandler;
        onDragStart = dragStartHandler;
        onDragMove = dragMoveHandler;
        onDragEnd = dragEndHandler;
        onPressFeedback = pressFeedbackHandler;
        inputEnabled = true;
        allowClick = true;
        hasPressPointer = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData == null ||
            eventData.button != PointerEventData.InputButton.Left)
            return;

        // 第二根手指落在同一个起点时也不能抢走第一根。
        if (pointerOwner == this && (ownsPress || isDragging))
            return;

        allowClick = false;
        if (!IsReady || pointerOwner != null)
            return;

        pointerOwner = this;
        pointerId = eventData.pointerId;
        hasPressPointer = true;
        ownsPress = true;
        allowClick = true;
        // Preserve the original pending click and release-based double-click
        // deadline. Only its visual preview is withdrawn on the second DOWN,
        // before waiting for its release, to minimise a flashing full cross.
        if(hasPendingClick && onDoubleClick!=null && Time.unscaledTimeAsDouble<=singleClickDeadline)
        {
            previewHiddenForSecondPress=true;
            onCancelPreview?.Invoke(index);
        }
        onPressFeedback?.Invoke(index,true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null || !ownsPress ||
            eventData.pointerId != pointerId ||
            eventData.button != PointerEventData.InputButton.Left)
            return;

        ownsPress = false;
        onPressFeedback?.Invoke(index,false);
        // 拖动的所有权由 EndDrag 释放，避免松手被当成单击。
        if (!isDragging && pointerOwner == this)
            pointerOwner = null;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if(eventData!=null&&hasPressPointer&&eventData.pointerId==pointerId)
            onPressFeedback?.Invoke(index,false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsReady || !allowClick || eventData == null ||
            eventData.button != PointerEventData.InputButton.Left ||
            eventData.dragging || isDragging ||
            (pointerOwner != null && pointerOwner != this))
            return;

        // 有实际按下事件时，只接受获准的那根手指。
        if (hasPressPointer && eventData.pointerId != pointerId)
            return;

        double now = Time.unscaledTimeAsDouble;
        if (now < ignoreClicksUntil)
        {
            ignoreClicksUntil = now + doubleClickInterval;
            return;
        }

        if (hasPendingClick)
        {
            if (now <= singleClickDeadline)
            {
                CancelPendingClick();
                ignoreClicksUntil = now + doubleClickInterval;
                if (IsReady && onDoubleClick != null)
                    onDoubleClick(index);
                return;
            }

            CommitSingleClick();
            if (!IsReady)
                return;
        }

        hasPendingClick = true;
        singleClickDeadline = now + doubleClickInterval;
        onPreview?.Invoke(index);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!IsReady || eventData == null ||
            eventData.button != PointerEventData.InputButton.Left ||
            pointerOwner != this || !ownsPress ||
            eventData.pointerId != pointerId ||
            onDragStart == null || onDragMove == null)
            return;

        isDragging = true;
        allowClick = false;
        onPressFeedback?.Invoke(index,false);
        CancelPendingClick();
        onDragStart(index, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!IsReady || !isDragging || pointerOwner != this ||
            eventData == null || eventData.pointerId != pointerId ||
            eventData.button != PointerEventData.InputButton.Left)
            return;

        onDragMove?.Invoke(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging || eventData == null ||
            eventData.pointerId != pointerId ||
            eventData.button != PointerEventData.InputButton.Left)
            return;

        FinishDrag(eventData);
    }

    private void FinishDrag(PointerEventData eventData)
    {
        bool notify = isDragging;
        isDragging = false;
        ownsPress = false;
        allowClick = false;
        if (pointerOwner == this)
            pointerOwner = null;

        if (notify)
            onDragEnd?.Invoke(eventData);
    }

    private void Update()
    {
        if (IsReady && hasPendingClick &&
            Time.unscaledTimeAsDouble > singleClickDeadline)
            CommitSingleClick();
    }

    private void LateUpdate()
    {
        // Up outside the original cell produces no PointerClick. Restore the
        // first click's preview in that case instead of losing the single tap.
        // For a real double click, OnPointerClick already cleared this flag.
        if(IsReady&&hasPendingClick&&previewHiddenForSecondPress&&!ownsPress&&!isDragging)
        {
            previewHiddenForSecondPress=false;
            onPreview?.Invoke(index);
        }
    }

    private void CommitSingleClick()
    {
        hasPendingClick = false;
        previewHiddenForSecondPress=false;
        singleClickDeadline = 0;
        onSingleClick(index);
    }

    public void CancelPendingClick()
    {
        bool restorePreview = hasPendingClick;
        hasPendingClick = false;
        previewHiddenForSecondPress=false;
        singleClickDeadline = 0;
        ignoreClicksUntil = 0;
        if (restorePreview)
            onCancelPreview?.Invoke(index);
    }

    private void CancelGesture()
    {
        onPressFeedback?.Invoke(index,false);
        FinishDrag(null);
        CancelPendingClick();
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
            CancelGesture();
    }

    private void OnDisable() => CancelGesture();

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            CancelGesture();
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
            CancelGesture();
    }
}

