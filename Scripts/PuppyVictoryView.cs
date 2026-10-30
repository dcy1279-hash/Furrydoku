using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Native Unity UI port of white_dog_victory_ui_v4. Presentation only: never
// creates/moves a board, reads answers, deducts lives, unlocks levels or saves.
// The puppy itself is a pre-rendered frame sequence (VictoryDogV5): an intro
// that plays once, then a loop that repeats seamlessly (see dog_victory_v3).
public sealed class PuppyVictoryView : MonoBehaviour
{
    private const string ResourcePath = "VictoryDogV4/";
    private const string DogFramesPath = "VictoryDogV5/";
    // Second dance (cheer + side-to-side wiggle), same frame size and paw line as V5.
    // Wins alternate between the two; the choice is remembered across sessions.
    private const string AltDogFramesPath = "VictoryDogV6/";
    private const string VariantKey = "PetPuzzle.VictoryVariant";
    private Texture2D[] introA, loopA, introB, loopB;
    private const float DogFps = 24f, DogStart = .12f;
    // Frame texture is 592x808; shown at 304x415 design units on the 780-high
    // layout. Paws touch the ground at y=409 inside it.
    private const float DogW = 304f, DogH = 415f, DogFeet = 409f;
    private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    private RectTransform safeParent, design, dogSpace, shadow;
    private RawImage dogFrame;
    private Texture2D[] introFrames, loopFrames;
    private RectTransform auraRoot, rayPivot, titleRect, descriptionRect, subtitleRect, buttonRect;
    private CanvasGroup auraGroup, dogGroup, titleGroup, copyGroup, buttonGroup;
    private readonly RawImage[] stars = new RawImage[3];
    private readonly CanvasGroup[] starGroups = new CanvasGroup[3];
    private readonly RawImage[] glints = new RawImage[10];
    private readonly RectTransform[] glintRects = new RectTransform[10];
    private readonly CanvasGroup[] glintGroups = new CanvasGroup[10];
    private RawImage description, warmAccent;
    private Button nextButton;
    private PuppyVictoryGlyphText buttonText;
    private PuppyVictoryParticles particles;
    private Sprite buttonSprite;
    private Action onNext;
    private float elapsed, designHeight = 780f;
    private Vector2 previousSize = new Vector2(-1,-1);
    private bool running, appFocused = true, appPaused, allowAction = true;

    // Share the presentation clock with the full-screen dimmer. Focus/pause
    // must not finish one entrance while leaving the other frozen halfway.
    public float EntranceAlpha => Smooth(elapsed / .36f);

    public static PuppyVictoryView TryCreate(RectTransform safeParent)
    {
        if (safeParent == null) return null;
        string[] names = { "GlowCore", "GlowRays", "SoftDot",
            "Title", "Description", "BatchComplete", "Subtitle", "Button", "ButtonGlyphs", "Star", "Chevron" };
        var loaded = new Dictionary<string, Texture2D>();
        foreach (string name in names)
        {
            Texture2D texture = Resources.Load<Texture2D>(ResourcePath + name);
            if (texture == null)
            {
                Debug.LogWarning("PuppyVictoryView：缺少 " + ResourcePath + name + "，保留原结算界面。");
                return null;
            }
            loaded.Add(name, texture);
        }
        TextAsset metadata = Resources.Load<TextAsset>(ResourcePath + "ButtonGlyphMetrics");
        if (metadata == null) return null;
        // Only the first frames now; the full clips stream in via DogFrameLibrary and are taken in Show().
        Texture2D introFirst = DogFrameLibrary.First(DogFramesPath + "intro"), loopFirst = DogFrameLibrary.First(DogFramesPath + "loop");
        if (introFirst == null || loopFirst == null)
        {
            Debug.LogWarning("PuppyVictoryView：缺少 " + DogFramesPath + " 小狗序列帧，保留原结算界面。");
            return null;
        }
        RectTransform rect = MakeRect("PuppyVictoryV4", safeParent);
        Stretch(rect);
        PuppyVictoryView view = rect.gameObject.AddComponent<PuppyVictoryView>();
        view.safeParent = safeParent;
        view.introFrames = new[] { introFirst }; view.loopFrames = new[] { loopFirst };
        foreach (var item in loaded) view.textures.Add(item.Key,item.Value);
        try { view.Build(metadata); }
        catch (Exception error)
        {
            rect.gameObject.SetActive(false); Destroy(rect.gameObject);
            Debug.LogException(error); return null;
        }
        view.RefreshLayout(true);
        rect.gameObject.SetActive(false);
        return view;
    }

    private void Build(TextAsset glyphs)
    {
        design = MakeRect("VictoryDesign", transform);
        design.anchorMin = design.anchorMax = new Vector2(.5f,.5f);
        design.pivot = new Vector2(.5f,.5f);

        auraRoot = MakeRect("AuraOrigin",design);
        auraGroup = auraRoot.gameObject.AddComponent<CanvasGroup>();
        ImageAt("WarmAndColorHalo",auraRoot,"GlowCore",-195,-250,390,500);
        warmAccent = ImageAt("LandingGlowAccent",auraRoot,"SoftDot",-171,-218,342,436);
        rayPivot = MakeRect("SlowRadialBeams",auraRoot);
        ImageAt("Beams",rayPivot,"GlowRays",-195,-250,390,500);
        for(int i=0;i<glints.Length;i++)
        {
            glintRects[i] = MakeRect("Glint_"+i,auraRoot);
            glintGroups[i] = glintRects[i].gameObject.AddComponent<CanvasGroup>();
            glints[i] = ImageAt("SoftGlint",glintRects[i],"SoftDot",-13,-13,26,26);
            ImageAt("Star",glintRects[i],"Star",-4,-4,8,8);
        }

        dogSpace = MakeRect("DogFrames",design);
        dogSpace.sizeDelta = new Vector2(DogW,DogH);
        dogSpace.pivot = new Vector2(.5f,.5f);
        dogGroup = dogSpace.gameObject.AddComponent<CanvasGroup>();
        // Soft contact shadow under the paws (the frames themselves carry none).
        RawImage ground = ImageAt("GroundShadow",dogSpace,"SoftDot",DogW*.5f-78,DogFeet-9,156,18);
        ground.color = new Color(157f/255f,104f/255f,80f/255f,.12f);
        shadow = ground.rectTransform;
        shadow.pivot = new Vector2(.5f,.5f); SetPoint(shadow,DogW*.5f,DogFeet);
        RectTransform frameRect = MakeRect("DogFrame",dogSpace);
        Place(frameRect,0,0,DogW,DogH);
        dogFrame = frameRect.gameObject.AddComponent<RawImage>();
        dogFrame.texture = introFrames[0]; dogFrame.raycastTarget = false;

        RectTransform confetti = MakeRect("CelebrationRibbons",design);
        particles = confetti.gameObject.AddComponent<PuppyVictoryParticles>();
        particles.Initialize();

        titleRect = ImageAt("VictoryTitle",design,"Title",0,93.6f,390,62).rectTransform;
        titleRect.pivot = new Vector2(.5f,.5f);
        titleGroup = titleRect.gameObject.AddComponent<CanvasGroup>();
        for(int i=0;i<3;i++)
        {
            stars[i]=ImageAt("LifeStar_"+(i+1),design,"Star",0,0,48,48);
            stars[i].rectTransform.pivot=new Vector2(.5f,.5f);
            starGroups[i]=stars[i].gameObject.AddComponent<CanvasGroup>();
        }
        RectTransform copy = MakeRect("VictoryCopy",design);
        copy.sizeDelta=new Vector2(390,68);
        copyGroup=copy.gameObject.AddComponent<CanvasGroup>();
        description = ImageAt("Description",copy,"Description",0,0,390,32);
        descriptionRect=copy;
        subtitleRect=ImageAt("Subtitle",copy,"Subtitle",0,35,390,24).rectTransform;

        buttonRect = MakeRect("NextLevelButton",design);
        buttonRect.pivot=new Vector2(.5f,.5f);
        buttonRect.sizeDelta=new Vector2(304,68);
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        Texture2D buttonTexture = textures["Button"];
        buttonSprite = Sprite.Create(buttonTexture,new Rect(0,0,buttonTexture.width,buttonTexture.height),new Vector2(.5f,.5f),100f);
        buttonSprite.name="PuppyVictoryButtonV4";
        buttonImage.sprite=buttonSprite;buttonImage.raycastTarget=true;
        nextButton=buttonRect.gameObject.AddComponent<Button>(); nextButton.targetGraphic=buttonImage;
        Navigation nav=nextButton.navigation;nav.mode=Navigation.Mode.None;nextButton.navigation=nav;
        ColorBlock colors=nextButton.colors;colors.normalColor=Color.white;colors.highlightedColor=Color.white;
        colors.selectedColor=Color.white;colors.pressedColor=new Color(.92f,.92f,.92f,1);colors.disabledColor=new Color(1,1,1,.75f);nextButton.colors=colors;
        RectTransform label=MakeRect("NextLevelGlyphs",buttonRect);
        Place(label,16,9,240,38);
        buttonText=label.gameObject.AddComponent<PuppyVictoryGlyphText>();
        buttonText.Configure(textures["ButtonGlyphs"],glyphs);
        ImageAt("Chevron",buttonRect,"Chevron",224,8,40,40);
        buttonGroup=buttonRect.gameObject.AddComponent<CanvasGroup>();
        nextButton.onClick.AddListener(HandleNext);
        ButtonPressFeedback.Attach(nextButton);
    }

    public void Show(int remainingLives, string nextLevelId, bool batchComplete, Action handler)
    {
        // Immutable display snapshot. A later new run restoring 3 lives cannot
        // change this rating; restoring a won save passes its saved life count.
        int earned=Mathf.Clamp(remainingLives,0,3);
        for(int i=0;i<stars.Length;i++)
            stars[i].color=i<earned?new Color32(255,195,74,255):new Color32(137,146,163,255);
        description.texture=textures[batchComplete?"BatchComplete":"Description"];
        buttonText.SetText(!string.IsNullOrEmpty(nextLevelId)
            ?"Next level":"Replay"); // always "Next level" (no level number); "Replay" after the last level
        // Alternate the two dances from win to win (falls back to V5 if V6 is missing).
        introA=DogFrameLibrary.Get(DogFramesPath+"intro")??introFrames;loopA=DogFrameLibrary.Get(DogFramesPath+"loop")??loopFrames;
        introB=DogFrameLibrary.Get(AltDogFramesPath+"intro");loopB=DogFrameLibrary.Get(AltDogFramesPath+"loop");
        bool useB=introB!=null&&loopB!=null&&PlayerPrefs.GetInt(VariantKey,0)==1;
        introFrames=useB?introB:introA; loopFrames=useB?loopB:loopA;
        PlayerPrefs.SetInt(VariantKey,useB?0:1);
        if(dogFrame!=null)dogFrame.texture=introFrames[0];
        onNext=handler;elapsed=0f;allowAction=true;running=true;
        appFocused=Application.isFocused;appPaused=false;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        RefreshLayout(true); ApplyAt(0f);
    }

    public void Hide()
    {
        running=false;onNext=null;
        if(nextButton!=null)nextButton.interactable=false;
        gameObject.SetActive(false);
    }
    public void SetInteractable(bool value)
    {
        allowAction=value;
        if(nextButton!=null)nextButton.interactable=value&&running&&onNext!=null&&elapsed>=1.55f;
    }
    private void HandleNext()
    {
        if(!running||!allowAction||elapsed<1.55f||onNext==null)return;
        // The existing ResultPanelView owns double-submit protection and the
        // actual next-level/retry callback. No second progression path here.
        Action action=onNext;allowAction=false;nextButton.interactable=false;action();
    }

    private void LateUpdate()
    {
        if(!running)return;
        RefreshLayout(false);
        if(appFocused&&!appPaused)elapsed+=Mathf.Min(Time.unscaledDeltaTime,.06f);
        ApplyAt(elapsed);
    }
    private void OnApplicationFocus(bool focused) { appFocused=focused; }
    private void OnApplicationPause(bool paused) { appPaused=paused; }
    private void OnDisable() { running=false;onNext=null;if(nextButton!=null)nextButton.interactable=false; }
    private void OnDestroy()
    {
        if(nextButton!=null)nextButton.onClick.RemoveListener(HandleNext);
        if(buttonSprite!=null)Destroy(buttonSprite);
        // Resources textures are imported assets, not owned runtime textures.
    }

    private void RefreshLayout(bool force)
    {
        if(safeParent==null||design==null)return;
        Vector2 size=safeParent.rect.size;if(size.x<=0||size.y<=0)return;
        if(!force&&size==previousSize)return;previousSize=size;
        designHeight=size.y/size.x>2.1f?840f:780f;
        design.sizeDelta=new Vector2(390,designHeight);
        design.localScale=Vector3.one*Mathf.Min(size.x/390f,size.y/designHeight);
        design.anchoredPosition=Vector2.zero;
        ApplyDogEntrance(elapsed);
        SetPoint(auraRoot,195,410f*designHeight/780f);
        RectTransform paper=particles.rectTransform;Place(paper,0,0,390,designHeight);
        SetPoint(titleRect,195,designHeight*.12f+31);
        float starY=designHeight*.12f+62f+7f+20f;
        for(int i=0;i<3;i++)SetPoint(stars[i].rectTransform,195+(i-1)*48,starY);
        SetPoint(descriptionRect,0,designHeight*(designHeight>800f?.725f:.752f));
        // No replay/pause debug controls in the actual game. Leave the same
        // CTA position as the approved preview, with extra bottom breathing room.
        SetPoint(buttonRect,195,designHeight-82f);
    }

    private void ApplyAt(float t)
    {
        ApplyDogEntrance(t);
        // Same presentation clock as everything else (pauses with focus/pause).
        // This offset is presentation-only; level completion is not delayed.
        float dogTime=Mathf.Max(0f,t-DogStart);
        Texture2D frame=DogFrameAt(dogTime);
        if(dogFrame.texture!=frame)dogFrame.texture=frame;

        float breath=1+.025f*Mathf.Sin(t*1.65f);
        auraRoot.localScale=new Vector3(breath,breath*designHeight/780f,1);
        auraGroup.alpha=Smooth(t/.48f);
        rayPivot.localRotation=Quaternion.Euler(0,0,-t*.035f*Mathf.Rad2Deg);
        warmAccent.color=new Color(1f,240f/255f,208f/255f,.17f*Mathf.Exp(-Mathf.Pow((dogTime-.76f)/.32f,2)));
        for(int i=0;i<glints.Length;i++)
        {
            float angle=i*Mathf.PI*2f/10f+.15f;
            SetPoint(glintRects[i],Mathf.Cos(angle)*(126+i%3*13),Mathf.Sin(angle)*(143+i%2*28));
            float twinkle=.3f+.7f*Mathf.Pow(.5f+.5f*Mathf.Sin(t*2.7f+i*2.1f),3);
            glints[i].color=new Color(1,.98f,.91f,.6f);
            glintGroups[i].alpha=twinkle;
            glintRects[i].localScale=Vector3.one*(.8f+i%3*.16f);
        }
        float titleTime=Mathf.Clamp01((t-.18f)/.65f);
        float titleScale=titleTime<.65f?Mathf.Lerp(.6f,1.09f,Ease(titleTime/.65f)):Mathf.Lerp(1.09f,1,Ease((titleTime-.65f)/.35f));
        titleRect.localScale=Vector3.one*titleScale;titleGroup.alpha=Mathf.Clamp01(titleTime/.65f);
        float titleOffset=titleTime<.65f?Mathf.Lerp(12,-3,Ease(titleTime/.65f)):Mathf.Lerp(-3,0,Ease((titleTime-.65f)/.35f));
        SetPoint(titleRect,195,designHeight*.12f+31+titleOffset);
        for(int i=0;i<3;i++)
        {
            float p=Mathf.Clamp01((t-(.48f+i*.09f))/.55f);
            float scale=p<.68f?Mathf.Lerp(.2f,1.15f,Ease(p/.68f)):Mathf.Lerp(1.15f,1,Ease((p-.68f)/.32f));
            stars[i].rectTransform.localScale=Vector3.one*scale;starGroups[i].alpha=Mathf.Clamp01(p/.68f);
        }
        float copy=Mathf.Clamp01((t-.8f)/.5f);copyGroup.alpha=copy;
        SetPoint(descriptionRect,0,designHeight*(designHeight>800f?.725f:.752f)+12*(1-Ease(copy)));
        float action=Mathf.Clamp01((t-1.05f)/.5f);buttonGroup.alpha=action;
        SetPoint(buttonRect,195,designHeight-82f+12*(1-Ease(action)));
        nextButton.interactable=allowAction&&running&&onNext!=null&&t>=1.55f;
        particles.RenderAt(t);
    }

    private void ApplyDogEntrance(float t)
    {
        // The frames already contain the crouch-and-jump entrance, so only a
        // short fade/settle here. Paws stand where the V4 dog's ground was.
        float progress=Mathf.Clamp01(t/.22f);
        float eased=Ease(progress);
        float dogScale=designHeight/780f;
        dogGroup.alpha=Smooth(progress);
        dogSpace.localScale=Vector3.one*(dogScale*Mathf.Lerp(.97f,1f,eased));
        float feetY=designHeight*.707f;
        SetPoint(dogSpace,195f,feetY-(DogFeet-DogH*.5f)*dogScale+6f*(1f-eased));
    }

    private Texture2D DogFrameAt(float dogTime)
    {
        int i=(int)(dogTime*DogFps);
        if(i<introFrames.Length)return introFrames[i];
        return loopFrames[(i-introFrames.Length)%loopFrames.Length];
    }

    private static float Smooth(float x){x=Mathf.Clamp01(x);return x*x*(3-2*x);}
    private static float Ease(float x)
    {
        // CSS cubic-bezier(.2,.8,.2,1), solved in x rather than assuming t=x.
        x=Mathf.Clamp01(x);if(x<=0f)return 0f;if(x>=1f)return 1f;
        float lo=0,hi=1,t=x;
        for(int i=0;i<10;i++){t=(lo+hi)*.5f;float q=1-t;float at=3*q*q*t*.2f+3*q*t*t*.2f+t*t*t;if(at<x)lo=t;else hi=t;}
        float u=1-t;return 3*u*u*t*.8f+3*u*t*t+t*t*t;
    }
    private RawImage ImageAt(string name,Transform parent,string texture,float x,float y,float w,float h)
    {
        RectTransform rect=MakeRect(name,parent);Place(rect,x,y,w,h);
        RawImage image=rect.gameObject.AddComponent<RawImage>();image.texture=textures[texture];image.raycastTarget=false;return image;
    }
    private static RectTransform MakeRect(string name,Transform parent)
    {
        var obj=new GameObject(name,typeof(RectTransform));obj.layer=parent.gameObject.layer;
        RectTransform rect=obj.GetComponent<RectTransform>();rect.SetParent(parent,false);
        rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);return rect;
    }
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
    }
    private static void SetPoint(RectTransform rect,float x,float y){rect.anchoredPosition=new Vector2(x,-y);}
    private static void Place(RectTransform rect,float x,float y,float w,float h){rect.sizeDelta=new Vector2(w,h);SetPoint(rect,x,y);}
}
