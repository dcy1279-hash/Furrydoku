using System;
using UnityEngine;
using UnityEngine.UI;

// Actual 3D vertices/normals from the accompanying native Blender model.
// Orthographic UI projection and depth-sorted opaque triangles need no extra
// Camera, RenderTexture, render-pipeline setup, colliders or per-frame textures.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PuppyHead3DGraphic : MaskableGraphic
{
    [Serializable] private sealed class Model
    {
        public int version;
        public float[] positions, normals, colours;
        public int[] triangles, eyeTags;
        public float eyeY, extent;
    }
    private static Model model;
    private static bool attempted;
    private Vector3[] transformed;
    private Color32[] shades;
    private float[] depths;
    private int[] order;
    private float yaw, blink, wink;
    public static bool Available
    {
        get
        {
            if (attempted) return model != null;
            attempted = true;
            TextAsset asset = Resources.Load<TextAsset>("PuppyHead3DV1/HeadMesh");
            if (asset == null) return false;
            try
            {
                Model candidate = JsonUtility.FromJson<Model>(asset.text);
                int count = candidate != null && candidate.eyeTags != null ? candidate.eyeTags.Length : 0;
                if (candidate == null || candidate.version != 1 || count < 3 || count >= 60000 ||
                    candidate.positions == null || candidate.normals == null || candidate.colours == null ||
                    candidate.positions.Length != count * 3 || candidate.normals.Length != count * 3 ||
                    candidate.colours.Length != count * 3 || candidate.triangles == null ||
                    candidate.triangles.Length % 3 != 0 || candidate.extent <= 0f) return false;
                foreach (int index in candidate.triangles) if (index < 0 || index >= count) return false;
                model = candidate;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("PuppyHead3DGraphic：试验模型无法加载，保留2D头像。" + exception.Message);
            }
            return model != null;
        }
    }
    public void SetPose(float degrees, float closeBoth = 0f, float closeOne = 0f)
    {
        degrees = Mathf.Round(Mathf.Clamp(degrees, -20f, 20f) * 8f) / 8f;
        closeBoth = Mathf.Clamp01(closeBoth); closeOne = Mathf.Clamp01(closeOne);
        if (Mathf.Approximately(yaw, degrees) && Mathf.Approximately(blink, closeBoth) &&
            Mathf.Approximately(wink, closeOne)) return;
        yaw = degrees; blink = closeBoth; wink = closeOne; SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!Available) return;
        int count = model.eyeTags.Length, triangleCount = model.triangles.Length / 3;
        if (transformed == null || transformed.Length != count)
        {
            transformed = new Vector3[count]; shades = new Color32[count];
            depths = new float[triangleCount]; order = new int[triangleCount];
        }
        float angle = yaw * Mathf.Deg2Rad, c = Mathf.Cos(angle), s = Mathf.Sin(angle);
        Vector3 light = new Vector3(-.45f, .65f, 1f).normalized;
        Vector3 halfLight = (light + Vector3.forward).normalized;
        Rect rect = GetPixelAdjustedRect();
        float scale = Mathf.Min(rect.width, rect.height) * .5f / model.extent;
        for (int i = 0; i < count; i++)
        {
            int k = i * 3;
            float x = model.positions[k], y = model.positions[k + 1], z = model.positions[k + 2];
            float nx = model.normals[k], ny = model.normals[k + 1], nz = model.normals[k + 2];
            if (model.eyeTags[i] != 0)
            {
                float closed = Mathf.Max(blink, model.eyeTags[i] == 2 ? wink : 0f);
                float eyeScale = Mathf.Lerp(1f, .045f, closed);
                y = model.eyeY + (y - model.eyeY) * eyeScale; ny /= eyeScale;
            }
            Vector3 p = new Vector3(c * x + s * z, y, -s * x + c * z);
            Vector3 n = new Vector3(c * nx + s * nz, ny, -s * nx + c * nz).normalized;
            transformed[i] = p;
            float diffuse = .58f + .38f * Mathf.Max(0f, Vector3.Dot(n, light));
            float dark = Mathf.Max(model.colours[k], Mathf.Max(model.colours[k + 1], model.colours[k + 2])) < .50f ? 1f : 0f;
            float specular = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(n, halfLight)), 46f) * .13f * dark;
            Color shade = new Color(model.colours[k] * diffuse + specular,
                model.colours[k + 1] * diffuse + specular,
                model.colours[k + 2] * diffuse + specular, 1f) * color;
            shades[i] = shade;
            vh.AddVert(rect.center + new Vector2(p.x, p.y) * scale, shades[i], Vector2.zero);
        }
        // A UI canvas has no mesh depth buffer. Back-to-front triangle ordering
        // preserves occlusion of the nose/cheeks/ears during small real yaw.
        for (int t = 0; t < triangleCount; t++)
        {
            int k = t * 3; order[t] = t;
            depths[t] = (transformed[model.triangles[k]].z + transformed[model.triangles[k + 1]].z +
                transformed[model.triangles[k + 2]].z) / 3f + t * .000000001f;
        }
        Array.Sort(depths, order);
        foreach (int triangle in order)
        {
            int k = triangle * 3;
            vh.AddTriangle(model.triangles[k], model.triangles[k + 1], model.triangles[k + 2]);
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetModel() { model = null; attempted = false; }
}
