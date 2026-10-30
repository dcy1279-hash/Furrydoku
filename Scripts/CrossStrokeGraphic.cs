using UnityEngine;
using UnityEngine.UI;

// A real two-stroke drawing, not a full-X scale/fade. Only the presentation
// progresses over time: placement, cancellation, saves and raycasts never wait.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CrossStrokeGraphic : MaskableGraphic
{
    public const float StrokeDuration = .12f;     // per stroke; a touch slower than the first .09 s
    private const float Reach = .20f, Radius = .095f; // short, chubby strokes (were .245 / .075)
    private float first = 1f, second = 1f, elapsed;
    private bool drawing;
    public void PlayDraw()
    {
        elapsed = 0f; drawing = true; SetProgress(0f,0f);
    }
    public void Complete()
    {
        drawing = false; elapsed = 0f; SetProgress(1f,1f);
    }
    private void SetProgress(float a,float b)
    {
        if(Mathf.Approximately(first,a)&&Mathf.Approximately(second,b))return;
        first=a;second=b;SetVerticesDirty();
    }
    private float lastPixel=-1f;
    // Size of one screen pixel in this graphic's local units.
    private float ScreenPixel()
    {
        Canvas c=canvas;if(c==null)return 1f;
        float scale=c.scaleFactor*Mathf.Abs(rectTransform.lossyScale.x/Mathf.Max(1e-5f,c.transform.lossyScale.x));
        return scale>1e-5f?1f/scale:1f;
    }
    private void Update()
    {
        float px=ScreenPixel();
        if(!Mathf.Approximately(px,lastPixel)){lastPixel=px;SetVerticesDirty();}
        if(!drawing)return;
        elapsed+=Mathf.Min(Time.unscaledDeltaTime,.05f);
        SetProgress(Mathf.SmoothStep(0f,1f,Mathf.Clamp01(elapsed/StrokeDuration)),
            Mathf.SmoothStep(0f,1f,Mathf.Clamp01((elapsed-StrokeDuration)/StrokeDuration)));
        if(elapsed>=StrokeDuration*2f)Complete();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect rect=GetPixelAdjustedRect();float side=Mathf.Min(rect.width,rect.height);
        Vector2 centre=rect.center;
        Stroke(vh,centre+new Vector2(-Reach,Reach)*side,
            centre+new Vector2(Reach,-Reach)*side,first,side);
        Stroke(vh,centre+new Vector2(Reach,Reach)*side,
            centre+new Vector2(-Reach,-Reach)*side,second,side);
    }
    private void Stroke(VertexHelper vh,Vector2 from,Vector2 to,float progress,float side)
    {
        if(progress<=.0001f||side<=0f)return;
        to=Vector2.Lerp(from,to,progress);
        float radius=side*Radius*Mathf.SmoothStep(0f,1f,Mathf.Clamp01(progress/.20f));
        // Anti-aliasing measured in real screen pixels (~1.5 px), centred on the edge, so the
        // diagonals stay smooth whether the canvas is scaled down (editor) or up (phone).
        float feather=Mathf.Min(radius*.9f,Mathf.Max(side*.006f,ScreenPixel()*1.5f));
        Vector2 n=new Vector2(-(to-from).y,(to-from).x).normalized;
        float inner=Mathf.Max(0f,radius-feather*.5f);
        radius+=feather*.5f;
        Color clear=new Color(color.r,color.g,color.b,0f);
        Quad(vh,from-n*inner,from+n*inner,to+n*inner,to-n*inner,color,color,color,color);
        Quad(vh,from-n*radius,from-n*inner,to-n*inner,to-n*radius,clear,color,color,clear);
        Quad(vh,from+n*inner,from+n*radius,to+n*radius,to+n*inner,color,clear,clear,color);
        Cap(vh,from,inner,radius,clear);Cap(vh,to,inner,radius,clear);
    }
    private void Cap(VertexHelper vh,Vector2 centre,float inner,float outer,Color clear)
    {
        const int steps=20;int start=vh.currentVertCount;
        vh.AddVert(centre,color,Vector2.zero);
        for(int i=0;i<=steps;i++)
        {
            float a=i*Mathf.PI*2f/steps;Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
            vh.AddVert(centre+d*inner,color,Vector2.zero);vh.AddVert(centre+d*outer,clear,Vector2.zero);
        }
        for(int i=0;i<steps;i++)
        {
            int a=start+1+i*2,b=a+2;
            vh.AddTriangle(start,a,b);vh.AddTriangle(a,a+1,b+1);vh.AddTriangle(a,b+1,b);
        }
    }
    private static void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color ca,Color cb,Color cc,Color cd)
    {
        int i=vh.currentVertCount;vh.AddVert(a,ca,Vector2.zero);vh.AddVert(b,cb,Vector2.zero);
        vh.AddVert(c,cc,Vector2.zero);vh.AddVert(d,cd,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
    }
    protected override void OnDisable(){drawing=false;first=second=1f;elapsed=0f;base.OnDisable();}
    private void OnApplicationFocus(bool focused){if(!focused)Complete();}
    private void OnApplicationPause(bool paused){if(paused)Complete();}
}
