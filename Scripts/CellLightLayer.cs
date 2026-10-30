using UnityEngine;
using UnityEngine.UI;

// Plain white-texture layer that lets a host graphic append CellLightWash meshes
// even when the host itself samples a coloured atlas.
[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CellLightLayer : MaskableGraphic
{
    public System.Action<VertexHelper> Populate;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Populate?.Invoke(mesh);
    }
}
