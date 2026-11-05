using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        readonly Color v11Ink=FantasyFrontendArt.Ink,v11Muted=new Color(.67f,.74f,.67f);
        GameObject v11FrontPanel,v11TreeCenter,v11DetailPanel,v11MainEmblem,v11SettingsBackdrop,v11TreeLegend;
        Text v11Title,v11Subtitle,v11Body,v11Notice,v11SectionMain,v11SectionCompanion,v11Detail,v11DetailHint,v11CenterName,v11PrepareHint;
        ScrollRect v11DetailScroll;
        string v11DetailKey;
        FrontendSilhouette v11CenterSilhouette;
        V11TreeConnections v11TreeConnections;
        V12FrontendTransition v12PageTransition;
        readonly List<V11FrontControl> v11FrontControls=new List<V11FrontControl>();
        readonly List<FantasyFrontendButton> v11AuxiliarySkins=new List<FantasyFrontendButton>();
        sealed class V11FrontControl
        {
            public string id;public Button button;public Text label,badge;public FrontendSilhouette silhouette;public FantasyFrontendArt art,seal;public FantasyFrontendButton skin;
        }
        void CreateV11Frontend(Transform root)
        {
            FrontendSilhouette.PrewarmReferences();
            v11SettingsBackdrop=Panel(root,"V11 settings forest",0,0,1280,720,Color.clear);
            FantasyArt(v11SettingsBackdrop.transform,"Night forest",FantasySurface.Forest,0,0,1280,720);v11SettingsBackdrop.SetActive(false);
            var settingsCover=FantasyArt(pausePanel.transform,"Settings book binding",FantasySurface.Button,0,0,550,636);
            StretchFantasyArt(settingsCover);settingsCover.transform.SetAsFirstSibling();
            v11FrontPanel=Panel(root,"V11 character frontend",0,0,1280,720,Color.clear);
            v12PageTransition=v11FrontPanel.AddComponent<V12FrontendTransition>();
            FantasyArt(v11FrontPanel.transform,"Moonlit forest book cover",FantasySurface.Forest,0,0,1280,720);
            v11MainEmblem=FantasyArt(v11FrontPanel.transform,"Journey emblem",FantasySurface.Emblem,440,40,400,66).gameObject;
            v11Title=Label(v11FrontPanel.transform,"",70,35,940,64,36,FantasyFrontendArt.Light);
            v11Subtitle=Label(v11FrontPanel.transform,"",70,93,980,30,16,v11Muted);
            v11Body=Label(v11FrontPanel.transform,"",70,128,1110,80,18,v11Muted);
            v11SectionMain=Label(v11FrontPanel.transform,"主线角色",90,102,500,29,20,FantasyFrontendArt.Light);
            FantasyArt(v11SectionMain.transform,"Main chapter divider",FantasySurface.Divider,138,14,320,2);
            v11SectionCompanion=Label(v11FrontPanel.transform,"同行者",90,429,500,29,20,FantasyFrontendArt.Light);
            FantasyArt(v11SectionCompanion.transform,"Companion chapter divider",FantasySurface.Divider,105,14,353,2);
            v11Notice=Label(v11FrontPanel.transform,"",210,672,860,35,15,v11Muted,TextAnchor.MiddleCenter);
            v11PrepareHint=Label(v11FrontPanel.transform,"",80,576,1100,52,15,v11Muted,TextAnchor.MiddleCenter);
            var lines=new GameObject("Character tree connections",typeof(RectTransform),typeof(CanvasRenderer),typeof(V11TreeConnections));
            lines.transform.SetParent(v11FrontPanel.transform,false);Rect(lines.GetComponent<RectTransform>(),0,0,1280,720);v11TreeConnections=lines.GetComponent<V11TreeConnections>();v11TreeConnections.raycastTarget=false;
            v11TreeCenter=Decor(v11FrontPanel.transform,"Character entry",471,273,148,142,Color.clear);
            FantasyArt(v11TreeCenter.transform,"Character bookplate",FantasySurface.Card,0,0,148,142);
            v11CenterSilhouette=AddV11Silhouette(v11TreeCenter.transform,35,8,78,94);
            v11CenterName=Label(v11TreeCenter.transform,"",4,108,140,28,19,v11Ink,TextAnchor.MiddleCenter);
            v11DetailPanel=Decor(v11FrontPanel.transform,"Node details",925,225,290,310,Color.clear);
            FantasyArt(v11DetailPanel.transform,"Folded skill folio",FantasySurface.Page,0,0,290,310);
            CreateV11DetailScroll();
            v11TreeLegend=Label(v11FrontPanel.transform,"✓ 已解锁    ＋ 可解锁    ▣ 前置未满足    ! 成长点不足",148,582,700,25,14,v11Muted,TextAnchor.MiddleCenter).gameObject;
            for(int i=0;i<24;i++)
            {
                var control=new V11FrontControl();
                var obj=Panel(v11FrontPanel.transform,"v11-front-slot-"+i,0,0,300,48,Color.clear);obj.GetComponent<Image>().raycastTarget=true;
                var visual=V11VisualLayer(obj.transform);
                control.art=FantasyArt(visual,"Hand-cut paper and metal frame",FantasySurface.Button,0,0,300,48);
                StretchFantasyArt(control.art);
                control.button=obj.AddComponent<FantasyFrontendCommandButton>();control.button.targetGraphic=obj.GetComponent<Image>();control.button.navigation=new Navigation{mode=Navigation.Mode.None};control.button.transition=Selectable.Transition.None;
                control.skin=obj.AddComponent<FantasyFrontendButton>();control.skin.Art=control.art;control.skin.Button=control.button;control.skin.Visual=visual;
                control.silhouette=AddV11Silhouette(visual,40,12,140,170);
                control.label=Label(visual,"",10,4,280,40,19,v11Ink,TextAnchor.MiddleCenter);control.skin.Label=control.label;
                control.seal=FantasyArt(visual,"Company wax seal",FantasySurface.Seal,242,12,38,38);
                control.badge=Label(control.seal.transform,"",2,5,34,27,12,FantasyFrontendArt.Light,TextAnchor.MiddleCenter);
                control.button.onClick.AddListener(()=>ChooseV11Control(control));
                obj.AddComponent<PresentationButtonHover>().World=world;v11FrontControls.Add(control);
            }
            V11Auxiliary("v11-settings","设置",1110,35,100,36,"settings");
            V11Auxiliary("v11-help","手册与许可",1090,675,150,28,"help");
            V11Auxiliary("v11-exit","退出",40,675,110,28,"exit-app");
            v11FrontPanel.SetActive(false);
        }
        void V11Auxiliary(string objectName,string title,float x,float y,float w,float h,string id)
        {
            var obj=Panel(v11FrontPanel.transform,objectName,x,y,w,h,Color.clear);var b=obj.AddComponent<FantasyFrontendCommandButton>();var visual=V11VisualLayer(obj.transform);
            var art=FantasyArt(visual,"Metal-edged auxiliary",FantasySurface.Button,0,0,w,h);StretchFantasyArt(art);
            b.targetGraphic=obj.GetComponent<Image>();b.transition=Selectable.Transition.None;b.navigation=new Navigation{mode=Navigation.Mode.None};
            var skin=obj.AddComponent<FantasyFrontendButton>();skin.Art=art;skin.Button=b;skin.Visual=visual;
            skin.Label=Label(visual,title,5,2,w-10,h-4,15,FantasyFrontendArt.Light,TextAnchor.MiddleCenter);v11AuxiliarySkins.Add(skin);
            b.onClick.AddListener(()=>{world.FrontendCue("confirm");world.ChooseFrontend(id);});
        }
        RectTransform V11VisualLayer(Transform parent)
        {
            var obj=new GameObject("Animated paper visual",typeof(RectTransform));obj.transform.SetParent(parent,false);
            var r=obj.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return r;
        }
        void ChooseV11Control(V11FrontControl control)
        {
            string id=control.id,node=world.V11SelectedNode,character=world.V11CurrentCharacter;
            bool purchase=id=="node-confirm"&&world.Profile!=null&&world.Profile.QuoteV11Node(character,node).canUnlock;
            world.FrontendCue("confirm");world.ChooseFrontend(id);
            if(!purchase||world.FrontPage!="character-tree"||world.V11CurrentCharacter!=character||world.Profile.QuoteV11Node(character,node).status!="Unlocked")return;
            var unlocked=v11FrontControls.FirstOrDefault(c=>c.id=="node:"+node);unlocked?.skin.PlayUnlock();
            v11TreeConnections.PlayUnlock(node,world.ReduceMotion);
        }
        FantasyFrontendArt FantasyArt(Transform parent,string name,FantasySurface surface,float x,float y,float w,float h,bool raycast=false)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(FantasyFrontendArt));obj.transform.SetParent(parent,false);
            Rect(obj.GetComponent<RectTransform>(),x,y,w,h);var art=obj.GetComponent<FantasyFrontendArt>();art.raycastTarget=raycast;art.Configure(surface);return art;
        }
        static void StretchFantasyArt(FantasyFrontendArt art)
        {var r=art.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=Vector2.zero;r.offsetMax=Vector2.zero;}
        void CreateV11DetailScroll()
        {
            var viewport=Panel(v11DetailPanel.transform,"Skill folio viewport",26,22,240,244,Color.clear);
            viewport.AddComponent<RectMask2D>();
            v11Detail=Label(viewport.transform,"",0,0,228,244,16,v11Ink);
            v11Detail.name="Complete skill description";v11Detail.verticalOverflow=VerticalWrapMode.Overflow;
            v11DetailScroll=viewport.AddComponent<ScrollRect>();v11DetailScroll.horizontal=false;v11DetailScroll.vertical=true;
            v11DetailScroll.inertia=false;v11DetailScroll.movementType=ScrollRect.MovementType.Clamped;
            v11DetailScroll.viewport=viewport.GetComponent<RectTransform>();v11DetailScroll.content=v11Detail.rectTransform;v11DetailScroll.scrollSensitivity=22;
            var rail=Panel(v11DetailPanel.transform,"Skill folio scrollbar",264,22,4,244,new Color(.29f,.35f,.27f,.16f));
            var handle=Panel(rail.transform,"Book ribbon handle",0,0,4,244,FantasyFrontendArt.Gold);
            var handleRect=handle.GetComponent<RectTransform>();handleRect.anchorMin=Vector2.zero;handleRect.anchorMax=Vector2.one;handleRect.offsetMin=Vector2.zero;handleRect.offsetMax=Vector2.zero;
            var scrollbar=rail.AddComponent<Scrollbar>();scrollbar.handleRect=handleRect;scrollbar.targetGraphic=handle.GetComponent<Image>();
            scrollbar.direction=Scrollbar.Direction.BottomToTop;scrollbar.navigation=new Navigation{mode=Navigation.Mode.None};
            v11DetailScroll.verticalScrollbar=scrollbar;v11DetailScroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            v11DetailHint=Label(v11DetailPanel.transform,"滚轮 / 拖动查看完整说明",26,277,240,20,13,v11Ink,TextAnchor.MiddleCenter);
            v11DetailHint.gameObject.SetActive(false);
        }
        void SetV11Detail(string nodeKey,string content)
        {
            if(v11DetailKey==nodeKey&&v11Detail.text==content)return;
            v11DetailKey=nodeKey;v11Detail.text=content;
            // Only this paper folio grows. The 16-point type and surrounding
            // tree remain unchanged; repeated HUD refreshes preserve the scroll.
            float viewportHeight=v11DetailScroll.viewport.rect.height;
            float contentHeight=Mathf.Max(viewportHeight,Mathf.Ceil(v11Detail.preferredHeight));
            v11Detail.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,contentHeight);
            v11DetailScroll.StopMovement();v11Detail.rectTransform.anchoredPosition=Vector2.zero;
            v11DetailScroll.verticalNormalizedPosition=1;
            v11DetailHint.gameObject.SetActive(contentHeight>viewportHeight+.5f);
        }
        static FantasyNodeState V11VisualState(V11NodeQuote q)=>q.status=="Unlocked"?FantasyNodeState.Unlocked:q.canUnlock?FantasyNodeState.Available:q.reason=="v11_demo_points_unavailable"?FantasyNodeState.Insufficient:FantasyNodeState.Prerequisite;
        static string V11VisualStateName(FantasyNodeState state)=>state==FantasyNodeState.Unlocked?"已解锁":state==FantasyNodeState.Available?"可解锁":state==FantasyNodeState.Insufficient?"成长点不足":"前置未满足";
        FrontendSilhouette AddV11Silhouette(Transform parent,float x,float y,float w,float h)
        {
            var obj=new GameObject("Black character silhouette",typeof(RectTransform),typeof(CanvasRenderer),typeof(FrontendSilhouette));obj.transform.SetParent(parent,false);
            Rect(obj.GetComponent<RectTransform>(),x,y,w,h);var s=obj.GetComponent<FrontendSilhouette>();s.raycastTarget=false;return s;
        }
        void RefreshV11Frontend()
        {
            if(v11FrontPanel.transform.GetSiblingIndex()!=v11FrontPanel.transform.parent.childCount-1)v11FrontPanel.transform.SetAsLastSibling();
            string page=world.FrontPage;bool roster=page=="characters",tree=page=="character-tree",main=page=="main",prepare=page=="prepare";
            v12PageTransition.BeforeRebind(page+(tree?"/"+world.V11CurrentCharacter:roster?"/"+world.V11RosterPage:""),world.ReduceMotion);
            v11MainEmblem.SetActive(main);v11TreeLegend.SetActive(tree);
            FrontText(v11Title,main?"遗迹之旅":world.FrontTitle);v11Title.fontSize=main?48:36;
            Rect(v11Title.rectTransform,main?300:70,main?107:35,main?680:930,main?76:64);v11Title.alignment=main?TextAnchor.MiddleCenter:TextAnchor.UpperLeft;
            FrontText(v11Subtitle,main?"A C T I O N   /   C R P G":tree?world.FrontBody:roster?"主线与同行者 · 所有永久解锁在此保存":"");
            v11Subtitle.gameObject.SetActive(!roster);
            Rect(v11Subtitle.rectTransform,main?300:70,main?177:94,main?680:1110,30);v11Subtitle.alignment=main?TextAnchor.MiddleCenter:TextAnchor.UpperLeft;
            v11Body.gameObject.SetActive(!main&&!roster&&!tree);FrontText(v11Body,world.FrontBody);
            Rect(v11Body.rectTransform,70,prepare?113:145,1110,prepare?32:130);
            v11SectionMain.gameObject.SetActive(roster);v11SectionCompanion.gameObject.SetActive(roster);
            FrontText(v11SectionCompanion,"同行者"+(world.V11RosterPages>1?"  "+(world.V11RosterPage+1)+" / "+world.V11RosterPages:""));
            v11TreeCenter.SetActive(tree);v11TreeConnections.gameObject.SetActive(tree);v11DetailPanel.SetActive(tree);v11PrepareHint.gameObject.SetActive(prepare);
            FrontText(v11Notice,world.Message);
            if(tree)
            {
                var c=V11Catalog.FindCharacter(world.V11CurrentCharacter);v11CenterSilhouette.Key=c.silhouetteId;FrontText(v11CenterName,c.displayName);
                var nodes=world.V13TreeNodes.ToArray();v11TreeConnections.SetNodes(nodes);v11TreeConnections.SetReducedMotion(world.ReduceMotion);
                var n=V11Catalog.FindNode(world.V11SelectedNode);
                if(n==null)SetV11Detail(null,"请选择节点查看详情。");
                else
                {
                    var q=world.Profile.QuoteV11Node(c.id,n.id);
                    string prerequisites=n.prerequisiteIds==null||n.prerequisiteIds.Count==0?"角色入口":string.Join("、",n.prerequisiteIds.Select(p=>V11Catalog.FindNode(p)?.displayName??p));
                    var visual=V11VisualState(q);
                    bool mainActive=c.category=="Main"&&n.kind=="Active";
                    string scope=mainActive?"\nV12探索已开放，无需购买即可使用。":"";
                    string feeState=visual==FantasyNodeState.Insufficient?"现有成长点不足。":visual==FantasyNodeState.Prerequisite?"请先解锁前置能力。":"保存后，下次出发生效。";
                    if(mainActive)feeState=(visual==FantasyNodeState.Insufficient?"永久节点成长点不足。\n":visual==FantasyNodeState.Prerequisite?"请先解锁永久节点的前置。\n":"")+"本次购买仅保存永久解锁记录，不增加本版技能效果。已开始的冒险保持原快照。";
                    SetV11Detail(n.id,n.displayName+"\n"+(n.kind=="Passive"?"被动":n.kind=="Upgrade"?"强化":"主动")+" · "+(mainActive?"永久节点：":"")+V11VisualStateName(visual)+scope+"\n\n"+n.description+"\n\n前置："+prerequisites+"\n消耗："+n.cost+" Demo 点\n"+feeState);
                }
            }
            if(prepare)
            {
                var draft=world.Profile.V11PreparationDraft;var q=world.Profile.QuoteV11Departure();
                FrontText(v11PrepareHint,(q.legal?"本局核心："+BattleWorld.V11Name(draft.coreId):BattleWorld.V11Reason(q.reason))+"\n已开放核心："+string.Join("、",V11Catalog.Characters.Where(c=>c.coreSupported).Select(c=>c.displayName))+" · 未开放同行者可作为队友。");
            }
            foreach(var skin in v11AuxiliarySkins)skin.Bind(skin.name,world.ReduceMotion,false,FantasyFrontendArt.Light);
            var choices=world.FrontChoices;
            for(int i=0;i<v11FrontControls.Count;i++)
            {
                var control=v11FrontControls[i];bool visible=i<choices.Count;control.button.gameObject.SetActive(visible);if(!visible)continue;
                var choice=choices[i];control.id=choice.id;control.button.name="front-"+choice.id;control.button.interactable=choice.enabled;
                ((FantasyFrontendCommandButton)control.button).SuppressDoubleClick=choice.id=="node-confirm"||choice.id=="depart"||choice.id=="main"||choice.id=="characters"||choice.id=="explore"||choice.id=="campaign"||choice.id.StartsWith("role:")||choice.id.StartsWith("map-")||choice.id.StartsWith("tree-")||choice.id.StartsWith("roster-");
                FrontText(control.label,choice.label);
                LayoutV11Control(control,i,page);bool selected=choice.id=="role:"+world.V11CurrentCharacter||choice.id=="node:"+world.V11SelectedNode||choice.id=="slot:"+world.V11PreparationSlot;
                FantasyNodeState nodeState=FantasyNodeState.None;
                if(choice.id.StartsWith("node:"))
                {
                    var n=V11Catalog.FindNode(choice.id.Substring(5));nodeState=V11VisualState(world.Profile.QuoteV11Node(n.characterId,n.id));
                    bool mainActive=n.kind=="Active"&&V11Catalog.FindCharacter(n.characterId).category=="Main";
                    FrontText(control.label,(n.kind=="Passive"?"被动":n.kind=="Upgrade"?"强化":"主动")+" · "+n.displayName+"\n"+(mainActive?"永久 · ":"")+V11VisualStateName(nodeState));
                }
                var surface=main?FantasySurface.Menu:choice.id.StartsWith("role:")?(V11Catalog.FindCharacter(choice.id.Substring(5)).category=="Main"?FantasySurface.Card:FantasySurface.SmallCard):choice.id.StartsWith("node:")?FantasySurface.Node:FantasySurface.Button;
                control.art.Configure(surface,nodeState,selected,i);
                var labelColour=surface==FantasySurface.Button?(choice.enabled?FantasyFrontendArt.Light:new Color(.48f,.55f,.50f)):v11Ink;
                control.skin.Bind(page+"/"+choice.id,world.ReduceMotion,roster&&choice.id.StartsWith("role:")&&!selected,labelColour);
                bool badge=false;
                if(roster&&choice.id.StartsWith("role:"))
                {
                    string actorId=choice.id.Substring(5);var draft=world.Profile.V11PreparationDraft;int team=draft.teamIds.IndexOf(actorId);badge=team>=0;
                    if(badge)FrontText(control.badge,draft.coreId==actorId?"核心":"队"+(team+1));
                }
                control.seal.gameObject.SetActive(badge);
            }
            v12PageTransition.AfterRebind();
        }
        void LayoutV11Control(V11FrontControl c,int index,string page)
        {
            string id=c.id;float x=390,y=310+index*60,w=500,h=47;bool portrait=false;string silhouette=null;int size=19;
            if(page=="main"){x=430;y=280+index*100;w=420;h=72;size=28;}
            else if(page=="characters")
            {
                if(id.StartsWith("role:"))
                {
                    var d=V11Catalog.FindCharacter(id.Substring(5));portrait=true;silhouette=d.silhouetteId;
                    if(d.category=="Main"){int n=V11Catalog.Characters.Where(a=>a.category=="Main").ToList().FindIndex(a=>a.id==d.id);x=145+n*345;y=145;w=300;h=260;}
                    else{int n=V11Catalog.Characters.Where(a=>a.category=="Companion").ToList().FindIndex(a=>a.id==d.id)-world.V11RosterPage*4;x=100+n*275;y=472;w=255;h=151;}
                }
                else if(id=="main"){x=80;y=638;w=180;h=32;size=15;}
                else if(id=="explore"){x=1020;y=638;w=180;h=32;size=15;}
                else{x=id=="roster-prev"?890:1050;y=429;w=140;h=29;size=14;}
            }
            else if(page=="character-tree")
            {
                if(id.StartsWith("node:")){var n=V11Catalog.FindNode(id.Substring(5));Vector2 p=V11TreeConnections.NodeTopLeft(n.id);x=p.x;y=p.y;w=260;h=102;size=18;}
                else if(id=="finn-tree"){x=72;y=138;w=280;h=36;size=17;}
                else if(id=="node-confirm"){x=948;y=548;w=244;h=44;size=17;}
                else if(id=="tree-prev"||id=="tree-next"){x=id=="tree-prev"?935:1025;y=146;w=72;h=42;size=29;}
                else{x=70;y=615;w=230;h=42;size=17;}
            }
            else if(page=="prepare")
            {
                if(id.StartsWith("mode:")){x=id=="mode:PersistentTrio"?95:660;y=156;w=545;h=43;size=16;}
                else if(id.StartsWith("slot:")){int n=int.Parse(id.Substring(5));x=95+n*375;y=207;w=340;h=66;size=21;}
                else if(id.StartsWith("member:")){int n=V11Catalog.Characters.ToList().FindIndex(d=>d.id==id.Substring(7));x=95+(n%4)*275;y=310+(n/4)*71;w=240;h=55;size=18;}
                else if(id.StartsWith("core:")){int n=world.Profile.V11PreparationDraft.teamIds.IndexOf(id.Substring(5));x=95+n*375;y=477;w=340;h=46;size=17;}
                else if(id=="active"||id=="finn-tree"){x=95;y=540;w=405;h=34;size=16;}
                else if(id=="finn-slot"){x=515;y=540;w=690;h=34;size=16;}
                else if(id=="depart"){x=975;y=627;w=230;h=44;size=24;}
                else{x=id=="characters"?320:95;y=632;w=195;h=34;size=16;}
            }
            else if(id=="map-ashen"){x=300;y=277;w=680;h=200;size=25;}
            else if(page=="explore"){x=id=="main"?85:850;y=565;w=340;h=45;}
            else if(page=="campaign"){x=390;y=350+index*76;w=500;h=55;size=22;}
            Rect(c.button.GetComponent<RectTransform>(),x,y,w,h);c.label.fontSize=size;c.silhouette.gameObject.SetActive(portrait);
            if(portrait)
            {
                c.silhouette.Key=silhouette;
                if(h>200){Rect(c.silhouette.rectTransform,72,20,w-144,h-78);Rect(c.label.rectTransform,18,h-47,w-36,32);}
                else{Rect(c.silhouette.rectTransform,22,13,88,h-32);Rect(c.label.rectTransform,116,42,w-132,50);}
                Rect(c.seal.rectTransform,w-49,11,37,37);
            }
            else if(page=="main")Rect(c.label.rectTransform,81,7,w-133,h-20);
            else if(id.StartsWith("node:"))Rect(c.label.rectTransform,58,15,w-75,h-33);
            else Rect(c.label.rectTransform,12,4,w-24,h-10);
        }
    }

    public sealed class V11TreeConnections:MaskableGraphic
    {
        string signature="",unlockedNode;V11NodeDefinition[] nodes;Coroutine pulse;float progress;bool reduced;
        public int UnlockPulseCount {get;private set;}
        public bool Animating=>pulse!=null;
        public string AnimatedNode=>unlockedNode;
        public void SetNodes(V11NodeDefinition[] value)
        {string key=string.Join("|",value.Select(n=>n.id+":"+string.Join(",",n.prerequisiteIds??new List<string>())));if(signature==key)return;CancelPulse();signature=key;nodes=value;SetVerticesDirty();}
        public void SetReducedMotion(bool value){reduced=value;if(value)CancelPulse();}
        public void PlayUnlock(string id,bool reduceMotion)
        {SetReducedMotion(reduceMotion);if(reduced||!isActiveAndEnabled||nodes==null||!nodes.Any(n=>n.id==id))return;CancelPulse();unlockedNode=id;UnlockPulseCount++;pulse=StartCoroutine(Pulse());}
        IEnumerator Pulse()
        {for(float elapsed=0;elapsed<.2f;elapsed+=Time.unscaledDeltaTime){progress=Mathf.Clamp01(elapsed/.2f);SetVerticesDirty();yield return null;}pulse=null;unlockedNode=null;SetVerticesDirty();}
        void CancelPulse(){if(pulse==null&&unlockedNode==null)return;if(pulse!=null)StopCoroutine(pulse);pulse=null;unlockedNode=null;SetVerticesDirty();}
        protected override void OnDisable(){CancelPulse();base.OnDisable();}
        public static Vector2 NodeTopLeft(string id)=>((id.StartsWith("v13/finn/")||id.StartsWith("char_story_finn/"))&&!id.EndsWith("/passive"))?new Vector2(id.EndsWith("1")?115:635,id.Contains("upgrade")?485:350):id.EndsWith("/passive")?new Vector2(415,140):id.EndsWith("/active1")?new Vector2(155,459):new Vector2(635,459);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(nodes==null)return;var center=new Vector2(545,344);
            foreach(var n in nodes)
            {
                var destination=NodeTopLeft(n.id)+new Vector2(130,51);
                if(n.prerequisiteIds==null||n.prerequisiteIds.Count==0)Branch(vh,center,destination,false,n.id==unlockedNode);
                else foreach(var id in n.prerequisiteIds){Vector2 start=NodeTopLeft(id)+new Vector2(130,51);Branch(vh,start,destination,true,n.id==unlockedNode);}
            }
        }
        void Branch(VertexHelper vh,Vector2 start,Vector2 end,bool prerequisite,bool animate)
        {
            // Every root terminates at an actual catalog prerequisite. The slight
            // organic bend decorates that edge without inventing extra tree paths.
            Vector2 control=prerequisite?new Vector2(end.x,start.y):new Vector2(start.x,(start.y+end.y)*.5f),previous=start;
            for(int i=1;i<=18;i++)
            {
                float t=i/18f;Vector2 p=(1-t)*(1-t)*start+2*(1-t)*t*control+t*t*end;
                Segment(vh,previous,p);if(i==8||i==11){Vector2 side=new Vector2(10,-10);Segment(vh,p,p+side);Segment(vh,p+side,p+new Vector2(15,-8));}
                if(animate&&t<=progress)Segment(vh,previous,p,true);previous=p;
            }
        }
        void Segment(VertexHelper vh,Vector2 a,Vector2 b,bool highlighted=false)
        {
            a=new Vector2(a.x,-a.y);b=new Vector2(b.x,-b.y);var n=new Vector2(-(b-a).y,(b-a).x).normalized*1.7f;int i=vh.currentVertCount;
            var c=highlighted?new Color(.93f,.89f,.54f,1-progress*.35f):new Color(.57f,.58f,.38f,.85f);vh.AddVert(a-n,c,Vector2.zero);vh.AddVert(b-n,c,Vector2.zero);vh.AddVert(b+n,c,Vector2.zero);vh.AddVert(a+n,c,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}

