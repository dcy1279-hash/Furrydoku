using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BoardVictoryGlowLayer : MaskableGraphic
{
    private BoardVictorySweepGraphic owner;
    private Material ownedMaterial;
    public override Texture mainTexture => owner != null ? owner.mainTexture : Texture2D.whiteTexture;
    public void Bind(BoardVictorySweepGraphic source)
    {
        owner = source; raycastTarget = false;
        Shader shader = Resources.Load<Shader>("VictorySeamsV1/UISeamScreen");
        if (shader != null && shader.isSupported)
        {
            ownedMaterial = new Material(shader) { name = "VictorySeamScreen_Runtime", hideFlags = HideFlags.DontSave };
            material = ownedMaterial;
        }
        else Debug.LogWarning("胜利格缝：柔光材质不可用，暂用普通透明光晕；不影响结算。", this);
        SetAllDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        if (owner != null) owner.Populate(mesh, true); else mesh.Clear();
    }
    protected override void OnDestroy()
    {
        base.OnDestroy(); if (ownedMaterial != null) Destroy(ownedMaterial);
    }
}
