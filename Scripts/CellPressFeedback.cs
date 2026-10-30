using UnityEngine;
using UnityEngine.UI;

// A separate rounded shade plus a jelly 'squash': the cell's visual group dips to
// ~0.93 on press and springs back with a small overshoot on release. Never changes
// the cell's root size/hit area, region colour, stable state or the root Image.
public sealed class CellPressFeedback : MonoBehaviour
{
    private const float PressedScale = .93f, Stiffness = 900f, Damping = 24f;
    private Image shade;
    private CellView cell;
    private float opacity, scale = 1f, velocity;
    private bool pressed;
    public void Bind(RectTransform visual,float cellSide)
    {
        if(visual==null)return;
        if(cell==null)cell=GetComponent<CellView>();
        if(shade==null)
        {
            RectTransform rect=PropBarView.MakeRect("CellPressShade",visual);
            PropBarView.Stretch(rect);rect.SetSiblingIndex(Mathf.Min(1,visual.childCount-1));
            shade=rect.gameObject.AddComponent<Image>();shade.raycastTarget=false;
        }
        BoardArtStyle.ApplyRounded(shade,cellSide*BoardArtStyle.CellCornerRatio);
        Apply();
    }
    public void SetPressed(bool value)
    {
        pressed=value;
        if(value)
        {
            opacity=Mathf.Max(opacity,.045f); // Visible immediately on down.
            velocity=Mathf.Min(velocity,-3.5f); // start the dip right away
        }
        Apply();
    }
    private void Update()
    {
        float dt=Mathf.Min(Time.unscaledDeltaTime,.033f);
        if(shade!=null)
        {
            float next=Mathf.MoveTowards(opacity,pressed?.085f:0f,dt*(pressed?1.7f:.9f));
            if(!Mathf.Approximately(opacity,next)){opacity=next;Apply();}
        }
        // Under-damped spring towards the target -> quick dip, bouncy release.
        float target=pressed?PressedScale:1f;
        if(Mathf.Abs(scale-target)<.0008f&&Mathf.Abs(velocity)<.004f)
        {
            if(scale!=target){scale=target;PushScale();}
            return;
        }
        for(int i=0;i<2;i++)
        {
            float h=dt*.5f;
            velocity+=(Stiffness*(target-scale)-Damping*velocity)*h;
            scale+=velocity*h;
        }
        PushScale();
    }
    private void PushScale(){if(cell!=null)cell.SetPressScale(scale);}
    private void Apply(){if(shade!=null)shade.color=new Color(.20f,.15f,.10f,opacity);}
    public void ResetFeedback(){pressed=false;opacity=0f;scale=1f;velocity=0f;Apply();PushScale();}
    private void OnDisable()=>ResetFeedback();
    private void OnApplicationFocus(bool focused){if(!focused)ResetFeedback();}
    private void OnApplicationPause(bool paused){if(paused)ResetFeedback();}
}
