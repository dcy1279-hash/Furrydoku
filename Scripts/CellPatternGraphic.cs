using UnityEngine;
using UnityEngine.UI;

// Subtle, colour-independent puppy-theme identifiers. No answer/placement data.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CellPatternGraphic : MaskableGraphic
{
    private int region;
    private Color ink;
    private bool clarity;
    // clarityMode: one distinct pet icon per colour (10 kinds) in the tile centre, white and
    // half see-through so it sits in the tile; drawn under the cross/animal (Settings > Clarity).
    // Paw, bone, heart, star, fish, moon, flower, drop, leaf, cloud (Resources/ClarityIcons/icon_0..9),
    // shared and never copied (copies of imported textures are black on phones).
    private static readonly Texture2D[] icons=new Texture2D[10];
    private static Texture2D Icon(int kind)
    {
        if(icons[kind]==null)icons[kind]=Resources.Load<Texture2D>("ClarityIcons/icon_"+kind);
        return icons[kind];
    }
    public override Texture mainTexture=>clarity&&Icon(region%10)!=null?Icon(region%10):base.mainTexture;
    public void Configure(int id,Color baseColour,bool clarityMode=false)
    {
        region=Mathf.Max(0,id);clarity=clarityMode;
        ink=clarityMode?new Color(baseColour.r*.50f,baseColour.g*.50f,baseColour.b*.50f,.88f)
            :new Color(baseColour.r*.56f,baseColour.g*.56f,baseColour.b*.56f,.15f);
        raycastTarget=false;SetVerticesDirty();SetMaterialDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;float side=Mathf.Min(r.width,r.height);
        // Clarity: one soft, see-through white pet icon per colour in the centre of the tile.
        // A single textured quad, so overlapping icon parts never stack into brighter patches.
        if(clarity)
        {
            if(Icon(region%10)==null)return;
            float half=side*.29f;Color iconTint=new Color(1f,1f,1f,.55f)*color;int i=vh.currentVertCount;
            vh.AddVert(r.center+new Vector2(-half,-half),iconTint,new Vector2(0f,0f));
            vh.AddVert(r.center+new Vector2(-half,half),iconTint,new Vector2(0f,1f));
            vh.AddVert(r.center+new Vector2(half,half),iconTint,new Vector2(1f,1f));
            vh.AddVert(r.center+new Vector2(half,-half),iconTint,new Vector2(1f,0f));
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
            return;
        }
        Vector2 c=r.center;float size=side*.46f;
        Color tint=ink*color;
        switch(region%5)
        {
            case 0:
                Ellipse(vh,c+Vector2.down*size*.12f,new Vector2(.25f,.20f)*size,0f,tint);
                for(int i=0;i<4;i++)Ellipse(vh,c+new Vector2((i-1.5f)*.17f,.20f+(.5f-Mathf.Abs(i-1.5f))*.07f)*size,
                    new Vector2(.074f,.095f)*size,(1.5f-i)*12f,tint);
                break;
            case 1:
                Stroke(vh,c+new Vector2(-.25f,-.25f)*size,c+new Vector2(.25f,.25f)*size,size*.13f,tint);
                foreach(float end in new[]{-1f,1f})foreach(float offset in new[]{-1f,1f})
                    Ellipse(vh,c+new Vector2(end*.25f+offset*.045f,end*.25f-offset*.045f)*size,
                        Vector2.one*size*.102f,0f,tint);
                break;
            case 2:
                Stroke(vh,c+Vector2.down*size*.33f,c+Vector2.up*size*.18f,size*.055f,tint);
                Ellipse(vh,c+new Vector2(-.15f,.12f)*size,new Vector2(.19f,.105f)*size,-30f,tint);
                Ellipse(vh,c+new Vector2(.15f,.20f)*size,new Vector2(.19f,.105f)*size,30f,tint);
                break;
            case 3:
                for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                    Star(vh,c+new Vector2(x,y)*size*.32f,size*(x==0&&y==0?.13f:.075f),tint);
                break;
            default:
                for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                    Triangle(vh,c+new Vector2(x,y)*size*.29f,size*.095f,tint);
                break;
        }
        // A very restrained top glint and lower edge, inside the rounded tile.
        Stroke(vh,new Vector2(r.xMin+side*.14f,r.yMax-side*.020f),new Vector2(r.xMax-side*.14f,r.yMax-side*.020f),side*.009f,new Color(1,1,1,.17f));
        Stroke(vh,new Vector2(r.xMin+side*.14f,r.yMin+side*.018f),new Vector2(r.xMax-side*.14f,r.yMin+side*.018f),side*.008f,new Color(.3f,.23f,.18f,.045f));
    }
    private static void Ellipse(VertexHelper vh,Vector2 p,Vector2 scale,float degrees,Color shade)
    {
        int start=vh.currentVertCount;vh.AddVert(p,shade,Vector2.zero);
        float c=Mathf.Cos(degrees*Mathf.Deg2Rad),s=Mathf.Sin(degrees*Mathf.Deg2Rad);
        for(int i=0;i<20;i++)
        {
            float a=i*Mathf.PI*.1f;Vector2 q=new Vector2(Mathf.Cos(a)*scale.x,Mathf.Sin(a)*scale.y);
            vh.AddVert(p+new Vector2(q.x*c-q.y*s,q.x*s+q.y*c),shade,Vector2.zero);
        }
        for(int i=0;i<20;i++)vh.AddTriangle(start,start+1+i,start+1+(i+1)%20);
    }
    private static void Stroke(VertexHelper vh,Vector2 a,Vector2 b,float width,Color shade)
    {
        Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;
        Quad(vh,a-n,a+n,b+n,b-n,shade);
        Ellipse(vh,a,Vector2.one*width*.5f,0,shade);Ellipse(vh,b,Vector2.one*width*.5f,0,shade);
    }
    private static void Star(VertexHelper vh,Vector2 p,float radius,Color shade)
    {
        Quad(vh,p+Vector2.up*radius,p+Vector2.right*radius*.21f,p+Vector2.down*radius,p+Vector2.left*radius*.21f,shade);
        Quad(vh,p+Vector2.left*radius,p+Vector2.up*radius*.21f,p+Vector2.right*radius,p+Vector2.down*radius*.21f,shade);
    }
    private static void Triangle(VertexHelper vh,Vector2 p,float radius,Color shade)
    {
        int i=vh.currentVertCount;vh.AddVert(p+Vector2.up*radius,shade,Vector2.zero);
        vh.AddVert(p+new Vector2(radius,-radius),shade,Vector2.zero);vh.AddVert(p+new Vector2(-radius,-radius),shade,Vector2.zero);vh.AddTriangle(i,i+1,i+2);
    }
    private static void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color shade)
    {
        int i=vh.currentVertCount;vh.AddVert(a,shade,Vector2.zero);vh.AddVert(b,shade,Vector2.zero);
        vh.AddVert(c,shade,Vector2.zero);vh.AddVert(d,shade,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
    }
}
