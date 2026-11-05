using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        GameObject frontPanel;
        Text frontTitle,frontBody,frontNotice,frontEnglish;
        readonly List<Button> frontButtons=new List<Button>();
        readonly List<Text> frontLabels=new List<Text>();
        readonly List<P47FrontendArtwork> frontArt=new List<P47FrontendArtwork>();
        readonly List<P47FrontendCard> frontCards=new List<P47FrontendCard>();
        Button frontSettingsButton,frontExitButton,frontHelpButton;
        Font frontLatin;
        void CreateFrontend(Transform root)
        {
            frontPanel=Panel(root,"Journey frontend",0,0,1280,720,new Color(.055f,.065f,.075f,1));
            FrontArt(frontPanel.transform,FrontArtworkKind.Background,0,0,1280,720);
            frontTitle=Label(frontPanel.transform,"",100,77,1080,58,38,new Color(.90f,.79f,.57f));
            frontLatin=Font.CreateDynamicFontFromOSFont(new[]{"Georgia","Times New Roman","Arial"},24);
            frontPanel.AddComponent<P47FrontendFontOwner>().Font=frontLatin;
            frontEnglish=Label(frontPanel.transform,"",102,137,1060,30,20,new Color(.63f,.53f,.35f));frontEnglish.font=frontLatin;
            frontBody=Label(frontPanel.transform,"",100,160,1080,70,18,new Color(.80f,.78f,.68f));
            for(int i=0;i<8;i++)
            {
                var b=CreateFrontButton(frontPanel.transform,"front-slot-"+i,"",out var text,out var card);
                frontButtons.Add(b);frontLabels.Add(text);frontCards.Add(card);
                var art=FrontArt(b.transform,FrontArtworkKind.Map,14,14,180,120);frontArt.Add(art);
                text.transform.SetAsLastSibling();
            }
            frontSettingsButton=CreateFrontButton(frontPanel.transform,"front-settings","设置",out _,out var settings);
            Rect(frontSettingsButton.GetComponent<RectTransform>(),1084,27,96,36);settings.Bind("settings",()=>world.ChooseFrontend("settings"));
            frontExitButton=CreateFrontButton(frontPanel.transform,"front-exit-app","退出",out _,out var quit);
            Rect(frontExitButton.GetComponent<RectTransform>(),38,668,104,32);quit.Bind("exit-app",()=>world.ChooseFrontend("exit-app"));
            frontHelpButton=CreateFrontButton(frontPanel.transform,"front-help","手册与许可",out _,out var help);
            Rect(frontHelpButton.GetComponent<RectTransform>(),1098,668,145,32);help.Bind("help",()=>world.ChooseFrontend("help"));
            frontNotice=Label(frontPanel.transform,"",170,666,900,40,15,new Color(.86f,.71f,.46f),TextAnchor.MiddleCenter);

            Rect(pausePanel.GetComponent<RectTransform>(),365,30,550,660);
            MoveButton("save",32,586,486,30);
            Button(pausePanel.transform,"reaction-mode","",32,447,486,34,()=>world.CycleReactionMode());
            Button(pausePanel.transform,"reaction-assistance","",32,492,486,34,()=>world.CycleReactionAssistance());
            MoveButton("tutorial-reset",32,397,232,28);MoveButton("tutorial-skip-settings",279,397,239,28);
            MoveButton("reaction-mode",32,435,486,30);MoveButton("reaction-assistance",32,474,486,30);
            Button(pausePanel.transform,"screen-shake","",32,513,232,28,()=>world.ConfigureImpactSettings((FeedbackIntensity)(((int)world.ShakeSetting+1)%3),world.HitStopSetting));
            Button(pausePanel.transform,"hit-stop","",279,513,239,28,()=>world.ConfigureImpactSettings(world.ShakeSetting,(FeedbackIntensity)(((int)world.HitStopSetting+1)%3)));
            Button(pausePanel.transform,"reduce-motion","",32,548,486,28,()=>world.ConfigureReduceMotion(!world.ReduceMotion));
            foreach(var text in pausePanel.GetComponentsInChildren<Text>())
                if(text.text.StartsWith("Esc 返回"))Rect(text.rectTransform,32,624,486,23);
            CreateV11Frontend(root);
        }
        P47FrontendArtwork FrontArt(Transform parent,FrontArtworkKind kind,float x,float y,float w,float h)
        {
            var obj=new GameObject("Original ink "+kind,typeof(RectTransform),typeof(CanvasRenderer),typeof(P47FrontendArtwork));
            obj.transform.SetParent(parent,false);Rect(obj.GetComponent<RectTransform>(),x,y,w,h);
            var art=obj.GetComponent<P47FrontendArtwork>();art.Kind=kind;art.raycastTarget=false;return art;
        }
        Button CreateFrontButton(Transform parent,string name,string label,out Text text,out P47FrontendCard card)
        {
            var obj=Panel(parent,name,0,0,300,48,new Color(.10f,.095f,.078f,.96f));
            var button=obj.AddComponent<Button>();button.targetGraphic=obj.GetComponent<Image>();
            button.navigation=new Navigation{mode=Navigation.Mode.None};
            var border=FrontArt(obj.transform,FrontArtworkKind.Border,0,0,300,48);
            var br=border.rectTransform;br.anchorMin=Vector2.zero;br.anchorMax=Vector2.one;br.offsetMin=br.offsetMax=Vector2.zero;
            text=Label(obj.transform,label,8,4,284,40,18,new Color(.88f,.82f,.68f),TextAnchor.MiddleCenter);
            // Auxiliary controls keep a stretched text region; card labels are explicitly laid out below.
            var tr=text.rectTransform;tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=new Vector2(8,4);tr.offsetMax=new Vector2(-8,-4);
            card=obj.AddComponent<P47FrontendCard>();card.Initialize(world,button,border);return button;
        }
        static void FrontText(Text target,string value){if(target.text!=value)target.text=value;}
        void RefreshFrontend()
        {
            if(frontPanel==null)return;
            bool visible=world.DungeonPresentation&&world.Menu&&!world.FrontSettings;
            bool fantasySettings=world.V11FrontendEnabled&&world.Menu&&world.FrontSettings;
            if(v11SettingsBackdrop.activeSelf!=fantasySettings)v11SettingsBackdrop.SetActive(fantasySettings);
            if(fantasySettings)v11SettingsBackdrop.transform.SetAsLastSibling();
            buttons["save"].gameObject.SetActive(!world.FrontSettings);
            var reactions=world.ReactionPreferences;
            SetButtonTitle("screen-shake","Screen Shake · "+world.ShakeSetting);
            SetButtonTitle("hit-stop","Hit Stop · "+world.HitStopSetting);
            SetButtonTitle("reduce-motion","减少界面动态 · "+(world.ReduceMotion?"开启":"关闭"));
            SetButtonTitle("reaction-mode","自动反应："+(reactions.autoMode==CombatV051.Domain.ReactionAutoMode.ImportantLegal?"关键合法（默认）":reactions.autoMode==CombatV051.Domain.ReactionAutoMode.AllLegal?"全部合法":"仅手动"));
            SetButtonTitle("reaction-assistance","反应 "+world.State.reactionSettings.decisionSeconds.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"秒 · 超时"+(reactions.timeoutAcceptPaidRoll?"接受已付费结果":"放弃（默认）"));
            if(frontPanel.activeSelf!=(visible&&!world.V11FrontendEnabled))frontPanel.SetActive(visible&&!world.V11FrontendEnabled);
            if(v11FrontPanel.activeSelf!=(visible&&world.V11FrontendEnabled))v11FrontPanel.SetActive(visible&&world.V11FrontendEnabled);
            if(world.DungeonPresentation&&world.Menu)
            {
                menuPanel.SetActive(false);pausePanel.SetActive(world.FrontSettings);
                if(world.FrontSettings){pausePanel.transform.SetAsLastSibling();buttons["save"].gameObject.SetActive(false);}
            }
            if(!visible)return;
            if(world.V11FrontendEnabled){RefreshV11Frontend();return;}
            if(frontPanel.transform.GetSiblingIndex()!=frontPanel.transform.parent.childCount-1)frontPanel.transform.SetAsLastSibling();
            FrontText(frontTitle,world.FrontTitle);FrontText(frontBody,world.FrontBody);FrontText(frontNotice,world.Message);
            string page=world.FrontPage;
            frontHelpButton.gameObject.SetActive(page!="credits"&&page!="legacy");
            FrontText(frontEnglish,page=="main"?"ASHEN RUINS":page=="campaign"?"CAMPAIGN":page=="characters"||page=="growth"?"COMPANIONS":page=="prepare"?"PREPARE THE COMPANY":page.StartsWith("explore")?"ASHEN RUINS":"THE TRAVELLER'S BOOK");
            frontBody.gameObject.SetActive(page!="main");
            Rect(frontBody.rectTransform,100,page=="characters"||page=="growth"?390:page=="campaign"?260:page=="explore-run"?260:160,
                page=="characters"||page=="growth"?655:1080,page=="characters"||page=="growth"?210:page=="campaign"?190:page=="explore-run"?110:90);
            var choices=world.FrontChoices;
            for(int i=0;i<frontButtons.Count;i++)
            {
                var b=frontButtons[i];bool show=i<choices.Count;
                if(b.gameObject.activeSelf!=show)b.gameObject.SetActive(show);if(!show)continue;
                var choice=choices[i];string id=choice.id;b.name="front-"+id;b.interactable=choice.enabled;
                frontCards[i].Bind(id,()=>world.ChooseFrontend(id),id=="role:"+world.FrontSelectedRole);
                FrontText(frontLabels[i],choice.label);frontLabels[i].color=choice.enabled?new Color(.88f,.82f,.68f):new Color(.49f,.46f,.39f);
                LayoutFrontChoice(i,id,page);
            }
        }
        void LayoutFrontChoice(int i,string id,string page)
        {
            float x=390,y=330+i*57,w=500,h=46;bool art=false;var kind=FrontArtworkKind.Map;
            bool roleCard=id.StartsWith("role:"),prepareCard=id.StartsWith("cycle:");
            if(page=="main")
            {x=100+i*370;y=228;w=340;h=352;art=true;kind=i==0?FrontArtworkKind.Campaign:i==1?FrontArtworkKind.Map:FrontArtworkKind.Company;}
            else if(id=="map-ashen"){x=325;y=226;w=630;h=355;art=true;kind=FrontArtworkKind.Map;}
            else if(roleCard||prepareCard)
            {
                x=100+i*370;y=prepareCard?240:203;w=340;h=prepareCard?230:165;art=true;
                kind=id.EndsWith("mage")?FrontArtworkKind.Mage:id.EndsWith("rogue")?FrontArtworkKind.Rogue:FrontArtworkKind.Fighter;
            }
            else if(page=="prepare")
            {
                int n=i-3;x=100+(n%3)*370;y=n<3?498:560;w=340;h=id=="depart"?48:42;
            }
            else if(page=="characters"||page=="growth")
            {
                if(id.StartsWith("unlock:")){x=805;y=398+(i-3)*54;w=375;h=44;}
                else {x=id=="main"?100:805;y=586;w=id=="main"?180:375;h=42;}
            }
            else if(page=="explore")
            {x=id=="main"?100:515;y=id=="main"?609:594;w=id=="main"?180:250;h=42;}
            else if(page=="campaign"){x=490;y=540;w=300;h=46;}
            else if(page=="explore-run"){x=390;y=390+i*64;w=500;h=48;}
            var rt=frontButtons[i].GetComponent<RectTransform>();Rect(rt,x,y,w,h);
            var visual=frontArt[i];if(visual.gameObject.activeSelf!=art)visual.gameObject.SetActive(art);
            if(art)
            {
                if(visual.Kind!=kind){visual.Kind=kind;visual.SetVerticesDirty();}
                if(roleCard){Rect(visual.rectTransform,9,8,122,h-16);Rect(frontLabels[i].rectTransform,136,28,w-146,h-56);}
                else {Rect(visual.rectTransform,18,14,w-36,h*.60f);Rect(frontLabels[i].rectTransform,12,h*.65f,w-24,h*.31f);}
                frontLabels[i].fontSize=page=="main"?29:id=="map-ashen"?19:19;
            }
            else{Rect(frontLabels[i].rectTransform,10,4,w-20,h-8);frontLabels[i].fontSize=18;}
        }
    }
    public sealed class P47FrontendFontOwner:MonoBehaviour
    {
        public Font Font;
        void OnDestroy(){if(Font)Destroy(Font);}
    }
}
