using System;
using UnityEngine;
using UnityEngine.UI;

// UI mesh particles: same deterministic trajectories as victory UI v4.
// No ParticleSystem camera/sorting dependency, no per-particle GameObjects,
// no physics or changes to board state. Bounded mesh, no per-frame textures.
[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PuppyVictoryParticles : MaskableGraphic
{
    private struct Seed { public float x, y, speed, turn, phase; public int side; public Color tint; }
    private Seed[] seeds;
    private float time;
    private readonly Color[] palette = {
        new Color32(241,151,177,255), new Color32(137,203,182,255),
        new Color32(236,193,95,255), new Color32(185,162,223,255),
        new Color32(242,155,104,255), new Color32(142,201,226,255)
    };

    public void Initialize()
    {
        raycastTarget = false;
        seeds = new Seed[132];
        for (int i = 0; i < seeds.Length; i++)
            seeds[i] = new Seed { x=Random(i,1), y=Random(i,2), speed=Random(i,3),
                turn=Random(i,4), phase=Random(i,5), side=i%2==0?-1:1, tint=palette[i%palette.Length] };
    }
    private static float Random(int i, int n)
    {
        double x = Math.Sin((i + 1) * 127.1d + n * 311.7d) * 43758.5453d;
        return (float)(x - Math.Floor(x));
    }
    public void RenderAt(float seconds) { time = Mathf.Max(0f, seconds); SetVerticesDirty(); }
    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x*x*(3f-2f*x); }

    private Vector3 Point(Vector2 p)
    {
        Rect r = rectTransform.rect;
        return new Vector3(r.xMin + p.x * r.width / 390f, r.yMax - p.y * r.height / 780f, 0f);
    }
    private void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(Point(a), tint, Vector2.zero); vh.AddVert(Point(b), tint, Vector2.zero);
        vh.AddVert(Point(c), tint, Vector2.zero); vh.AddVert(Point(d), tint, Vector2.zero);
        vh.AddTriangle(i,i+1,i+2); vh.AddTriangle(i,i+2,i+3);
    }
    private static Vector2 Rotate(Vector2 p, float angle)
    {
        float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
        return new Vector2(p.x*c-p.y*s,p.x*s+p.y*c);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (seeds == null) return;
        for (int i=0;i<24;i++)
        {
            Seed p=seeds[i]; bool burst=i<14; float age,alpha;
            if (burst)
            {
                age=time-.42f-p.phase*.25f;
                if(age<0f||age>3.8f)continue;
                alpha=Mathf.Min(1f,(3.8f-age)*2f);
            }
            else
            {
                if(time<1.2f)continue;
                age=((time-1.2f)*.65f+p.phase*3.8f)%3.8f;
                alpha=.58f*Smooth((time-1.2f)/.5f);
            }
            float x=burst?195f+p.side*(54f+p.x*75f)*age:p.x*390f+Mathf.Sin(time+p.phase*8f)*14f;
            float y=burst?360f-(185f+p.y*120f)*age+115f*age*age:age*245f-125f;
            if(x>110f&&x<280f&&y>230f&&y<520f)alpha*=.18f;
            Color tint=p.tint;tint.a=alpha*Mathf.Clamp01((time-.42f)*5f);
            float length=42f+p.speed*50f, half=(2.1f+p.turn*1.5f)*.5f;
            float rotation=Mathf.Sin(time*1.4f+p.phase*6f)*.32f;
            Vector2 origin=new Vector2(x,y);
            for(int j=0;j<20;j++)
            {
                float u=j/20f,v=(j+1)/20f;
                Vector2 a=new Vector2(Mathf.Sin(u*7f+time*2f+p.phase*8f)*(5f+p.turn*6f),u*length);
                Vector2 b=new Vector2(Mathf.Sin(v*7f+time*2f+p.phase*8f)*(5f+p.turn*6f),v*length);
                Vector2 normal=new Vector2(-(b-a).y,(b-a).x).normalized*half;
                Quad(vh,origin+Rotate(a-normal,rotation),origin+Rotate(a+normal,rotation),
                    origin+Rotate(b+normal,rotation),origin+Rotate(b-normal,rotation),tint);
            }
        }
        for(int i=0;i<seeds.Length;i++)
        {
            Seed p=seeds[i];float x,y,alpha;
            if(time<2.65f)
            {
                float age=time-.46f-p.phase*.18f;if(age<0f)continue;
                x=195f+p.side*(95f+165f*p.x)*age;y=365f-(175f+210f*p.y)*age+230f*age*age;
                alpha=Mathf.Clamp01(age*7f)*Mathf.Clamp01((2.65f-time)*2.5f);
            }
            else
            {
                if(i>57)continue;
                x=p.x*390f+Mathf.Sin(time*1.5f+p.phase*7f)*20f;
                y=((time-2.65f)*(28f+p.speed*26f)+p.y*900f)%930f-100f;alpha=.63f;
                if(x>103f&&x<287f&&y>235f&&y<510f)alpha*=.12f;
            }
            if(x>103f&&x<287f&&y>235f&&y<510f)alpha*=.4f;
            Color tint=p.tint;tint.a=Mathf.Clamp01(alpha);
            float halfW=(5f+i%3)*.5f*(Mathf.Cos(time*5f+p.turn*10f)*.6f+.65f);
            float halfH=(8f+i%4)*.5f,rotation=time*(1f+p.turn*4f)+p.phase*7f;
            Vector2 origin=new Vector2(x,y);
            Quad(vh,origin+Rotate(new Vector2(-halfW,-halfH),rotation),origin+Rotate(new Vector2(-halfW,halfH),rotation),
                origin+Rotate(new Vector2(halfW,halfH),rotation),origin+Rotate(new Vector2(halfW,-halfH),rotation),tint);
        }
    }
}
