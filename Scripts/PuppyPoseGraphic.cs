using UnityEngine;
using UnityEngine.UI;

// Single approved painting on a shallow, deformable relief mesh. The texture
// never changes during turning, so there is no second face/fur outline to ghost.
// This is deliberately 2.5D, suitable for subtle near-frontal looks only.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PuppyPoseGraphic : MaskableGraphic
{
    [SerializeField] private Sprite source;
    private float yaw, pitch, blink, wink, earLag;
    public bool preserveAspect = true;
    public Sprite sprite
    {
        get => source;
        set { if (source == value) return; source = value; SetMaterialDirty(); SetVerticesDirty(); }
    }
    public override Texture mainTexture => source != null ? source.texture : Texture2D.whiteTexture;
    private const int Steps = 48;
    private struct Sample
    {
        public Vector2 uv;
        public float depth, leftEye, rightEye, ears;
    }
    private static readonly Sample[] Samples = BuildSamples();
    private static float Gaussian(float x, float y, float cx, float cy, float rx, float ry)
    {
        float dx = (x - cx) / rx, dy = (y - cy) / ry;
        return Mathf.Exp(-.5f * (dx * dx + dy * dy));
    }
    private static float EyeWeight(float x, float y, float cx, float cy)
    {
        float dx = (x - cx) / .095f, dy = (y - cy) / .093f;
        return Mathf.Exp(-(dx * dx * dx * dx + dy * dy * dy * dy));
    }
    private static Sample[] BuildSamples()
    {
        var result = new Sample[(Steps + 1) * (Steps + 1)];
        for (int row = 0; row <= Steps; row++) for (int col = 0; col <= Steps; col++)
        {
            float x = col / (float)Steps, y = row / (float)Steps;
            float dx = (x - .5f) / .365f, dy = (y - .5f) / .43f;
            float dome = Mathf.Sqrt(Mathf.Clamp01(1f - dx * dx - dy * dy));
            result[row * (Steps + 1) + col] = new Sample
            {
                uv = new Vector2(x, y),
                depth = .05f + .22f * dome + .16f * Gaussian(x,y,.50f,.61f,.12f,.09f) +
                    .055f * Gaussian(x,y,.50f,.73f,.21f,.12f),
                leftEye = EyeWeight(x,y,.377f,.455f),
                rightEye = EyeWeight(x,y,.650f,.460f),
                ears = Gaussian(x,y,.14f,.46f,.13f,.23f) + Gaussian(x,y,.87f,.46f,.13f,.23f)
            };
        }
        return result;
    }
    public void SetDepthPose(float yawDegrees, float closeBoth = 0f, float closeOne = 0f,
        float pitchDegrees = 0f, float follow = 0f)
    {
        yawDegrees = Mathf.Clamp(yawDegrees, -9f, 9f);
        pitchDegrees = Mathf.Clamp(pitchDegrees, -1.5f, 1.5f);
        closeBoth = Mathf.Clamp01(closeBoth); closeOne = Mathf.Clamp01(closeOne);
        follow = Mathf.Clamp(follow, -.5f, .5f);
        if (Mathf.Approximately(yaw,yawDegrees) && Mathf.Approximately(pitch,pitchDegrees) &&
            Mathf.Approximately(blink,closeBoth) && Mathf.Approximately(wink,closeOne) &&
            Mathf.Approximately(earLag,follow)) return;
        yaw = yawDegrees; pitch = pitchDegrees; blink = closeBoth; wink = closeOne; earLag = follow;
        SetVerticesDirty();
    }
    // Retain the previous method signature for reset/lifecycle compatibility.
    public void SetTurn(int side, float amount, bool turnedSource)
    {
        SetDepthPose(turnedSource ? 0f : side * amount * 12f);
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (source == null) return;
        Rect r = GetPixelAdjustedRect();
        if (preserveAspect)
        {
            float edge = Mathf.Min(r.width, r.height);
            r = new Rect(r.center.x - edge * .5f, r.center.y - edge * .5f, edge, edge);
        }
        float a = yaw * Mathf.Deg2Rad, b = pitch * Mathf.Deg2Rad;
        float c = Mathf.Cos(a), s = Mathf.Sin(a), cp = Mathf.Cos(b), sp = Mathf.Sin(b);
        Vector4 outer = UnityEngine.Sprites.DataUtility.GetOuterUV(source);
        foreach (Sample sample in Samples)
        {
            float x = sample.uv.x - .5f, y = .5f - sample.uv.y, z = sample.depth - .10f;
            float rightClose = Mathf.Max(blink, wink);
            // Compact only the eye neighbourhood, not the whole generated head.
            // Smooth monotone falloff keeps the surrounding fur attached.
            y += (sample.uv.y - .455f) * .95f * blink * sample.leftEye +
                (sample.uv.y - .460f) * .95f * rightClose * sample.rightEye;
            float rotatedX = x * c + z * s;
            float rotatedY = y * cp - z * sp + sample.ears * earLag * .013f;
            vh.AddVert(new Vector2(r.center.x + rotatedX * r.width, r.center.y + rotatedY * r.height), color,
                new Vector2(Mathf.Lerp(outer.x,outer.z,sample.uv.x), Mathf.Lerp(outer.y,outer.w,1f-sample.uv.y)));
        }
        for (int row = 0; row < Steps; row++) for (int col = 0; col < Steps; col++)
        {
            int a0 = row * (Steps + 1) + col;
            vh.AddTriangle(a0,a0+1,a0+Steps+1);
            vh.AddTriangle(a0+1,a0+Steps+2,a0+Steps+1);
        }
    }
}
