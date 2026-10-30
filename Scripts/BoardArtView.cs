using UnityEngine;
using UnityEngine.UI;

// A sibling shadow, not an extra GridLayoutGroup child. Never reparents the
// live board, never masks/clips avatar pop-outs, and never touches gameplay.
public sealed class BoardArtView : MonoBehaviour
{
    private RectTransform board, shadow;
    private Image shadowImage;
    public static void Apply(RectTransform board)
    {
        if(board==null)return;
        float side=Mathf.Min(board.rect.width,board.rect.height);
        Image plate=board.GetComponent<Image>();
        if(plate!=null){BoardArtStyle.ApplyRounded(plate,side*BoardArtStyle.FrameCornerRatio);plate.color=Color.white;}
        var view=board.GetComponent<BoardArtView>();
        if(view==null)view=board.gameObject.AddComponent<BoardArtView>();
        view.board=board;view.RefreshShadow();
    }
    private void RefreshShadow()
    {
        if(board==null||board.parent==null||!isActiveAndEnabled)return;
        if(shadow==null)
        {
            shadow=PropBarView.MakeRect("BoardSoftShadow",board.parent);
            shadow.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
            shadowImage=shadow.gameObject.AddComponent<Image>();
            shadowImage.color=new Color(.50f,.38f,.25f,.09f);shadowImage.raycastTarget=false;
        }
        if(shadow.parent!=board.parent)shadow.SetParent(board.parent,false);
        float side=Mathf.Min(board.rect.width,board.rect.height);
        shadow.anchorMin=board.anchorMin;shadow.anchorMax=board.anchorMax;shadow.pivot=board.pivot;
        shadow.sizeDelta=board.sizeDelta+Vector2.one*(side*.002f);
        shadow.anchoredPosition3D=board.anchoredPosition3D+Vector3.down*(side*.004f);
        shadow.localRotation=board.localRotation;shadow.localScale=board.localScale;
        int boardIndex=board.GetSiblingIndex();
        if(shadow.GetSiblingIndex()!=boardIndex-1)
            shadow.SetSiblingIndex(shadow.GetSiblingIndex()<boardIndex?boardIndex-1:boardIndex);
        BoardArtStyle.ApplyRounded(shadowImage,side*BoardArtStyle.FrameCornerRatio);
        if(!shadow.gameObject.activeSelf)shadow.gameObject.SetActive(true);
    }
    private void LateUpdate()=>RefreshShadow();
    private void OnDisable(){if(shadow!=null)shadow.gameObject.SetActive(false);}
    private void OnDestroy(){if(shadow!=null)Destroy(shadow.gameObject);}
}
