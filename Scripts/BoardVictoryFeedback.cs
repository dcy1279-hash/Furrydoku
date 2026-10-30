using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Victory-only presentation after the stable win/save; never changes round data.
public sealed class BoardVictoryFeedback : MonoBehaviour
{
    public const float Duration = BoardVictoryTiming.Duration;
    private readonly List<AnimalPose> animals = new List<AnimalPose>(10);
    private readonly List<CellPose> cells = new List<CellPose>(100);
    private RectTransform board, sweepRect;
    private BoardVictorySweepGraphic sweep;
    private Rect originalRect;
    private Matrix4x4 originalMatrix;
    private Action completed;
    private HudView lifeHud;
    private float elapsed;
    private bool playing, focused = true, paused;
    public float Progress => Mathf.Clamp01(elapsed / Duration);

    public bool TryPlay(RectTransform targetBoard, CellView[] views, Action onComplete, HudView targetHud = null, float placementWarmup = 0f)
    {
        Cancel();
        if (!isActiveAndEnabled || targetBoard == null || !(targetBoard.parent is RectTransform) ||
            views == null || views.Length < 16 || views.Length > 100 || onComplete == null ||
            targetBoard.rect.width <= 0f || targetBoard.rect.height <= 0f) return false;
        try
        {
            board = targetBoard;
            float warmup = Mathf.Clamp(placementWarmup, 0f, 1f);
            var rects = new Rect[views.Length];
            var colors = new Color[views.Length];
            var corners = new Vector3[4];
            Rect bounds = default;
            for (int i = 0; i < views.Length; i++)
            {
                CellView view = views[i];
                if (view == null || !view.isActiveAndEnabled || !(view.transform is RectTransform root))
                { Cancel(); return false; }
                warmup = Mathf.Max(warmup, view.AnimalArrivalRemaining);
                view.ResetFeedback(true); // Allow the final ordinary reveal to finish first.
                root.GetWorldCorners(corners);
                Vector2 min = board.InverseTransformPoint(corners[0]), max = min;
                for (int c = 1; c < 4; c++)
                {
                    Vector2 p = board.InverseTransformPoint(corners[c]);
                    min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                }
                rects[i] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                colors[i] = view.RegionColor;
                bounds = i == 0 ? rects[i] : Rect.MinMaxRect(Mathf.Min(bounds.xMin, min.x),
                    Mathf.Min(bounds.yMin, min.y), Mathf.Max(bounds.xMax, max.x), Mathf.Max(bounds.yMax, max.y));
            }
            for (int i = 0; i < views.Length; i++)
            {
                CellView view = views[i];
                float diagonal = (Mathf.InverseLerp(bounds.xMin, bounds.xMax, rects[i].center.x) +
                    Mathf.InverseLerp(bounds.yMin, bounds.yMax, rects[i].center.y)) * .5f;
                cells.Add(new CellPose(view, diagonal));
                if (view.TryGetPlacedAnimalRect(out RectTransform animal)) animals.Add(new AnimalPose(animal));
            }
            if (animals.Count == 0) { Cancel(); return false; }
            if (sweep == null)
            {
                var go = new GameObject("BoardVictoryGoldSeams", typeof(RectTransform));
                go.layer = board.gameObject.layer; go.SetActive(false);
                sweepRect = go.GetComponent<RectTransform>(); sweepRect.SetParent(board.parent, false);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                sweep = go.AddComponent<BoardVictorySweepGraphic>(); sweep.raycastTarget = false;
            }
            sweepRect.anchorMin = board.anchorMin; sweepRect.anchorMax = board.anchorMax;
            sweepRect.pivot = board.pivot; sweepRect.sizeDelta = board.sizeDelta;
            sweepRect.anchoredPosition3D = board.anchoredPosition3D;
            sweepRect.localScale = board.localScale; sweepRect.localRotation = board.localRotation;
            sweepRect.SetSiblingIndex(board.GetSiblingIndex() + 1);
            sweep.Configure(rects, colors);
            if (!sweep.Ready) { Cancel(); return false; }
            originalRect = board.rect; originalMatrix = board.localToWorldMatrix;
            elapsed = -warmup; completed = onComplete; lifeHud = targetHud;
            focused = Application.isFocused; paused = false; playing = true;
            if (lifeHud != null) lifeHud.ResetVictoryLifeFeedback();
            sweep.SetTime(-1f); sweep.gameObject.SetActive(true);
            return true;
        }
        catch (Exception error)
        {
            Cancel(); Debug.LogWarning("BoardVictoryFeedback：跳过棋盘庆祝，继续显示结算：" + error.Message, this);
            return false;
        }
    }
    private void Update()
    {
        if (!playing || !focused || paused) return;
        if (board == null || !board.gameObject.activeInHierarchy || sweep == null ||
            board.rect != originalRect || board.localToWorldMatrix != originalMatrix)
        { Finish(); return; }
        elapsed += Mathf.Min(Time.unscaledDeltaTime, .06f);
        if (elapsed < 0f) return;
        float lightTime = BoardVictoryTiming.LightTime(elapsed);
        sweep.SetTime(lightTime);
        foreach (CellPose cell in cells) cell.Apply(lightTime);
        foreach (AnimalPose animal in animals)
        {
            if (animal.Target == null || !animal.Target.gameObject.activeInHierarchy) { Finish(); return; }
            animal.Apply(elapsed);
        }
        if (lifeHud != null) lifeHud.ApplyVictoryLifeFeedback(elapsed, BoardVictoryTiming.LifeStart);
        if (elapsed >= Duration) Finish();
    }
    private void Finish()
    {
        Action callback = completed; Cancel(); callback?.Invoke();
    }
    public void Cancel()
    {
        playing = false; completed = null; elapsed = 0f;
        foreach (AnimalPose animal in animals) animal.Restore(); animals.Clear();
        foreach (CellPose cell in cells) cell.Restore(); cells.Clear();
        if (lifeHud != null) lifeHud.ResetVictoryLifeFeedback(); lifeHud = null;
        if (sweepRect != null) sweepRect.gameObject.SetActive(false);
        board = null;
    }
    private void OnDisable() => Cancel();
    private void OnApplicationFocus(bool value) { focused = value; }
    private void OnApplicationPause(bool value) { paused = value; }
    private void OnDestroy()
    {
        Cancel(); if (sweepRect != null) Destroy(sweepRect.gameObject);
    }
    private sealed class AnimalPose
    {
        public readonly RectTransform Target;
        private readonly PuppyAvatarView avatar;
        private readonly Vector3 scale, position;
        private readonly Quaternion rotation;
        private readonly Graphic image;
        private readonly Color color;
        private bool driven;
        public AnimalPose(RectTransform target)
        {
            Target = target; avatar = target.GetComponent<PuppyAvatarView>();
            scale = target.localScale; position = target.anchoredPosition3D; rotation = target.localRotation;
            image = target.GetComponent<Graphic>(); color = image != null ? image.color : Color.white;
        }
        public void Apply(float time)
        {
            if (avatar != null && avatar.ApplyVictoryPose(time)) { driven = true; return; }
            // Legacy circle fallback remains visible if a character is unavailable.
            Target.localScale = scale * BoardVictoryTiming.AvatarScale(time);
            Target.localRotation = rotation * Quaternion.Euler(0f, 0f, -BoardVictoryTiming.Tilt(time));
            if (image != null) { Color c = color; c.a = BoardVictoryTiming.Hidden(time) ? 0f : color.a; image.color = c; }
        }
        public void Restore()
        {
            if (avatar != null && driven) avatar.EndVictoryPose();
            if (Target == null) return;
            Target.localScale = scale; Target.localRotation = rotation; Target.anchoredPosition3D = position;
            if (image != null) image.color = color;
        }
    }
    private sealed class CellPose
    {
        private readonly CellView view;
        private readonly float diagonal;
        public CellPose(CellView target, float value) { view = target; diagonal = value; }
        public void Apply(float time)
        {
            if (view != null) view.TrySetLineWaveScale(BoardVictoryTiming.CellScale(diagonal, time));
        }
        public void Restore() { if (view != null) view.ResetLineWave(); }
    }
}
