using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Code-native UI assets, shared by the whole board. No downloaded artwork or
// modified scene/prefab files. Existing Image components keep their hit areas.
public static class BoardArtStyle
{
    public const float GapToCellRatio = .065f;
    public const float CellCornerRatio = .10f;
    public const float BoardPaddingRatio = .018f;
    public const float FrameCornerRatio = .034f;
    // 256² mask with a 64-texel corner and a mip chain: cells show corners of only ~10-30 px,
    // and minifying a 1-texel edge without mips is what made the corners jagged.
    private const int TextureSide = 256, CornerTexels = 64, Border = CornerTexels + 1;
    private static Texture2D roundedTexture, crossTexture;
    private static Sprite roundedSprite, crossSprite;

    public static void CalculateGrid(int size, float boardSide, out float spacing, out int padding)
    {
        padding = Mathf.Max(1, Mathf.RoundToInt(boardSide * BoardPaddingRatio));
        float side = (boardSide - padding * 2f) / (size + (size - 1) * GapToCellRatio);
        spacing = Mathf.Max(0f, side * GapToCellRatio);
    }
    public static void ApplyRounded(Image image, float radius)
    {
        if (image == null) return;
        image.overrideSprite = null;
        // This uGUI version validates texture readability even when assigning
        // zero. Usually the threshold is already zero: do not call its setter.
        // If a prefab has a nonzero value, detach its sprite before resetting,
        // then assign our GPU-only rounded mask. Raycast at <=0 uses the rect.
        if (image.alphaHitTestMinimumThreshold != 0f)
        {
            image.sprite = null;
            image.alphaHitTestMinimumThreshold = 0f;
        }
        image.sprite = Rounded;
        image.type = Image.Type.Sliced; image.fillCenter = true; image.preserveAspect = false;
        image.pixelsPerUnitMultiplier = Border / (Mathf.Max(.5f, radius) * Mathf.Max(.01f,image.pixelsPerUnit));
        // Keep the original rectangular click target, even in transparent corners.
    }
    public static CrossStrokeGraphic EnsureCross(GameObject mark)
    {
        if (mark == null) return null;
        Graphic original=mark.GetComponent<Graphic>();
        if(original==null)return null;
        original.enabled=false;
        Transform old=mark.transform.Find("RoundedWhiteCross");
        if(old!=null){Graphic g=old.GetComponent<Graphic>();if(g!=null)g.enabled=false;}
        // Keep original TMP/Image and all Inspector references. Own Graphic
        // gets its own child, not a second Graphic on either old object.
        RectTransform rect=mark.transform.Find("TwoStrokeCross") as RectTransform;
        if(rect==null)rect=PropBarView.MakeRect("TwoStrokeCross",mark.transform);
        PropBarView.Stretch(rect);
        var graphic=rect.GetComponent<CrossStrokeGraphic>();
        if(graphic==null)graphic=rect.gameObject.AddComponent<CrossStrokeGraphic>();
        graphic.raycastTarget=false;return graphic;
    }
    private static Sprite Rounded
    {
        get
        {
            if (roundedSprite != null) return roundedSprite;
            roundedTexture = NewTexture("Board rounded rectangle mask");
            var pixels = new Color32[TextureSide * TextureSide];
            for (int y=0;y<TextureSide;y++) for (int x=0;x<TextureSide;x++)
            {
                float qx=Mathf.Abs(x+.5f-TextureSide*.5f)-(TextureSide*.5f-CornerTexels);
                float qy=Mathf.Abs(y+.5f-TextureSide*.5f)-(TextureSide*.5f-CornerTexels);
                float distance = new Vector2(Mathf.Max(qx,0f),Mathf.Max(qy,0f)).magnitude +
                    Mathf.Min(Mathf.Max(qx,qy),0f)-CornerTexels;
                pixels[y*TextureSide+x]=new Color(1f,1f,1f,Mathf.Clamp01(.5f-distance));
            }
            roundedTexture.SetPixels32(pixels); roundedTexture.Apply(true,true); // build mips
            roundedSprite=Sprite.Create(roundedTexture,new Rect(0,0,TextureSide,TextureSide),Vector2.one*.5f,
                100f,0,SpriteMeshType.FullRect,new Vector4(Border,Border,Border,Border));
            roundedSprite.name="Board shared rounded nine-slice"; roundedSprite.hideFlags=HideFlags.DontSave;
            return roundedSprite;
        }
    }
    private static Sprite Cross
    {
        get
        {
            if (crossSprite != null) return crossSprite;
            crossTexture=NewTexture("Rounded thick cross mask");
            var pixels=new Color32[TextureSide*TextureSide];
            for(int y=0;y<TextureSide;y++)for(int x=0;x<TextureSide;x++)
            {
                Vector2 p=new Vector2((x+.5f)/TextureSide-.5f,(y+.5f)/TextureSide-.5f);
                float d=Mathf.Min(SegmentDistance(p,new Vector2(-.245f,-.245f),new Vector2(.245f,.245f)),
                    SegmentDistance(p,new Vector2(-.245f,.245f),new Vector2(.245f,-.245f)))-.075f;
                pixels[y*TextureSide+x]=new Color(1f,1f,1f,Mathf.Clamp01(.5f-d*TextureSide));
            }
            crossTexture.SetPixels32(pixels); crossTexture.Apply(true,true);
            crossSprite=Sprite.Create(crossTexture,new Rect(0,0,TextureSide,TextureSide),Vector2.one*.5f,100f,0,SpriteMeshType.FullRect);
            crossSprite.name="Board shared thick rounded cross";crossSprite.hideFlags=HideFlags.DontSave;
            return crossSprite;
        }
    }
    private static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
    {
        Vector2 ab=b-a;return (p-a-ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude)).magnitude;
    }
    private static Texture2D NewTexture(string name)
    {
        return new Texture2D(TextureSide,TextureSide,TextureFormat.RGBA32,true)
            {name=name,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOwnedAssets()
    {
        if(roundedSprite!=null)Object.Destroy(roundedSprite);if(crossSprite!=null)Object.Destroy(crossSprite);
        if(roundedTexture!=null)Object.Destroy(roundedTexture);if(crossTexture!=null)Object.Destroy(crossTexture);
        roundedSprite=crossSprite=null;roundedTexture=crossTexture=null;
    }
}
