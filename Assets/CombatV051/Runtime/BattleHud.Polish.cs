using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    /// <summary>Player-facing layout only. The original command buttons remain the authoritative UI entry points.</summary>
    public sealed partial class BattleHud
    {
        GameObject polishRoot, debugPanel, tutorialPanel, objectivePanel, compactResources, interactionPanel, actionStrip;
        Text objectiveLabel, compactDice, momentumLabel, tutorialLabel, interactionLabel, modeBadge, debugText, toast, edgeArrow;
        readonly Dictionary<string, Image> healthBars = new Dictionary<string, Image>();
        readonly Dictionary<string, GameObject> activeBorders = new Dictionary<string, GameObject>();
        readonly List<Graphic> legacyDecoration = new List<Graphic>();
        Image momentumFill, focusBudgetFill;
        GameObject compactTarget;Text compactTargetName;Image compactTargetHp;
        Text compactOpportunities;
        Text focusBudgetText;
        public FocusBudgetFeedback FocusFeedback {get;private set;}
        Slider masterSlider, musicSlider, sfxSlider;
        Text masterValue, musicValue, sfxValue;
        string previousMessage;
        float toastUntil;
        bool expandedFirstDecision;
        string firstDecisionTicket;
        public bool DebugOverlayVisible => debugPanel != null && debugPanel.activeSelf;
        public string VisibleTutorialText => tutorialPanel != null && tutorialPanel.activeSelf ? tutorialLabel.text : "";
        public string VisibleObjectiveText => objectiveLabel == null ? "" : objectiveLabel.text;

        GameObject Decor(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var obj = Panel(parent,name,x,y,w,h,color); obj.GetComponent<Image>().raycastTarget=false; return obj;
        }
        void MoveButton(string id, float x,float y,float w,float h)
        {
            var b=buttons[id]; Rect(b.GetComponent<RectTransform>(),x,y,w,h);
            var t=b.GetComponentInChildren<Text>(); Rect(t.rectTransform,8,2,w-16,h-4);
        }
        void CreatePolishPanels(Transform root)
        {
            foreach (Transform child in root)
            {
                var graphic=child.GetComponent<Graphic>();
                if (child.name=="Header" || child.name=="Footer") { if(graphic!=null) legacyDecoration.Add(graphic); }
                var text=child.GetComponent<Text>();
                if(text!=null && (text.text=="ACTION / CRPG" || text.text.StartsWith("WASD 移动"))) legacyDecoration.Add(text);
            }
            polishRoot = new GameObject("P3 player HUD",typeof(RectTransform)); polishRoot.transform.SetParent(root,false);
            Rect(polishRoot.GetComponent<RectTransform>(),0,0,1280,720);
            compactResources=Decor(root,"Tactical resources",498,14,284,84,new Color(.03f,.08f,.10f,.90f));
            compactDice=Label(compactResources.transform,"",12,4,160,26,23,Gold);
            momentumLabel=Label(compactResources.transform,"",176,7,96,23,13,Teal,TextAnchor.MiddleRight);
            Decor(compactResources.transform,"Momentum track",12,40,260,4,new Color(.17f,.24f,.28f));
            momentumFill=Decor(compactResources.transform,"Momentum earned",12,40,0,4,Teal).GetComponent<Image>();
            Decor(compactResources.transform,"Focus track",12,69,260,5,new Color(.17f,.24f,.28f));
            focusBudgetFill=Decor(compactResources.transform,"Focus budget",12,69,0,5,Gold).GetComponent<Image>();
            focusBudgetText=Label(compactResources.transform,"",12,46,260,21,12,Gold);
            // Only the narrow resource strip animates at display rate; other HUD text
            // remains on the existing state-change / 10 Hz refresh path.
            var resourceCanvas=compactResources.AddComponent<Canvas>();resourceCanvas.overrideSorting=true;resourceCanvas.sortingOrder=3;
            var focusRecovery=Label(compactResources.transform,"",178,76,94,23,14,Gold,TextAnchor.MiddleRight);
            FocusFeedback=compactResources.AddComponent<FocusBudgetFeedback>();FocusFeedback.Initialize(world,focusBudgetFill,focusRecovery,Gold);
            compactTarget=Decor(polishRoot.transform,"Current target compact",269,14,211,57,new Color(.03f,.08f,.10f,.86f));
            compactTargetName=Label(compactTarget.transform,"",10,5,191,33,16,Color.white);
            Decor(compactTarget.transform,"Target health track",10,45,191,4,new Color(.17f,.24f,.28f));
            compactTargetHp=Decor(compactTarget.transform,"Target health",10,45,191,4,Gold).GetComponent<Image>();
            compactOpportunities=Label(compactTarget.transform,"",10,57,191,22,13,Teal);
            objectivePanel=Decor(polishRoot.transform,"Current objective",936,14,326,65,new Color(.03f,.08f,.10f,.86f));
            objectiveLabel=Label(objectivePanel.transform,"",14,8,298,48,17,Color.white);
            Button(polishRoot.transform,"settings","设置",1188,86,74,28,()=>world.Command(CommandKind.Pause));
            modeBadge=Label(polishRoot.transform,"",488,101,304,27,16,Teal,TextAnchor.MiddleCenter);
            toast=Label(polishRoot.transform,"",340,113,540,52,17,Gold,TextAnchor.MiddleCenter);
            tutorialPanel=Decor(polishRoot.transform,"One contextual lesson",380,595,520,69,new Color(.035f,.095f,.12f,.96f));
            Decor(tutorialPanel.transform,"Tutorial accent",0,0,3,69,Teal);
            tutorialLabel=Label(tutorialPanel.transform,"",16,10,403,48,19,Color.white,TextAnchor.MiddleLeft);
            Button(tutorialPanel.transform,"tutorial-skip","跳过教学",425,19,82,30,()=>world.SkipPresentationTutorial());
            interactionPanel=Decor(polishRoot.transform,"Nearby interaction",505,541,270,38,new Color(.16f,.12f,.055f,.94f));
            interactionLabel=Label(interactionPanel.transform,"",10,6,250,26,18,Gold,TextAnchor.MiddleCenter);
            edgeArrow=Label(polishRoot.transform,"◆",0,0,100,45,24,Gold,TextAnchor.MiddleCenter);
            actionStrip=Decor(polishRoot.transform,"Realtime keycaps",383,674,514,34,new Color(.03f,.08f,.10f,.93f));
            string[] caps={"左键  /  攻击","右键  /  格挡","空格  /  闪避","左Shift  /  准备"};
            for(int i=0;i<caps.Length;i++)Label(actionStrip.transform,caps[i],i*128+5,7,122,21,13,Color.white,TextAnchor.MiddleCenter);

            foreach(var id in V11Catalog.Characters.Select(c=>c.id))
            {
                var row=buttons["active-"+id];
                var border=Decor(row.transform,"Active border",0,0,3,36,Teal); activeBorders[id]=border;
                Decor(row.transform,"Health track",10,31,210,3,new Color(.1f,.14f,.18f));
                healthBars[id]=Decor(row.transform,"Health",10,31,210,3,Teal).GetComponent<Image>();
            }
            // Keep the battlefield center free: detailed spatial information belongs under the party frames.
            Rect(tacticalSpatialPanel.GetComponent<RectTransform>(),16,427,300,157);
            var spatialHeading=tacticalSpatialPanel.GetComponentsInChildren<Text>()[0];
            spatialHeading.text="小队位置与预计落点"; Rect(spatialHeading.rectTransform,12,8,276,23); spatialHeading.fontSize=15;
            Rect(tacticalSpatialText.rectTransform,12,37,276,112); tacticalSpatialText.fontSize=13;
            Rect(rollPanel.GetComponent<RectTransform>(),421,91,438,134);
            foreach(var t in rollPanel.GetComponentsInChildren<Text>()) { var r=t.rectTransform; r.sizeDelta=new Vector2(410,r.sizeDelta.y); }
            Rect(pausePanel.GetComponent<RectTransform>(),365,93,550,535);
            MoveButton("resume",32,76,486,38); MoveButton("quick",32,123,486,34);
            MoveButton("scale15",32,166,151,32); MoveButton("scale10",196,166,151,32); MoveButton("scale00",360,166,158,32);
            foreach(var t in pausePanel.GetComponentsInChildren<Text>())if(t.text.StartsWith("Esc 继续"))t.gameObject.SetActive(false);
            masterSlider=VolumeSlider(pausePanel.transform,"总音量",224,(v)=>SetVolumes(),out masterValue);
            musicSlider=VolumeSlider(pausePanel.transform,"音乐",281,(v)=>SetVolumes(),out musicValue);
            sfxSlider=VolumeSlider(pausePanel.transform,"音效",338,(v)=>SetVolumes(),out sfxValue);
            Button(pausePanel.transform,"tutorial-reset","重置教程提示",32,402,232,34,()=>world.ResetPresentationTutorial());
            Button(pausePanel.transform,"tutorial-skip-settings","跳过教程",279,402,239,34,()=>world.SkipPresentationTutorial());
            MoveButton("save",32,452,486,38);
            Label(pausePanel.transform,"Esc 返回   ·   音量与教程设置自动保存",32,499,486,23,13,new Color(.65f,.77f,.8f));
            MakeScrollable(dungeonDialogBody,26,77,656,190);
            Button(postPanel.transform,"post-more","更多调整",18,286,312,35,()=>expandedFirstDecision=!expandedFirstDecision);
            debugPanel=Panel(root,"F1 developer overlay",16,175,720,402,new Color(0,0,0,.96f));
            debugText=Label(debugPanel.transform,"",12,12,696,378,13,new Color(.7f,1,.75f));
            debugPanel.SetActive(false);
        }
        Slider VolumeSlider(Transform parent,string title,float y,Action<float> changed,out Text value)
        {
            Label(parent,title,32,y,115,24,16,Color.white);
            value=Label(parent,"",453,y,65,24,15,Gold,TextAnchor.MiddleRight);
            var go=Decor(parent,title+" slider",150,y+3,294,20,new Color(.10f,.18f,.22f));
            go.GetComponent<Image>().raycastTarget=true;
            var fill=Decor(go.transform,"Fill",0,0,294,20,Teal).GetComponent<RectTransform>();
            fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;
            var slider=go.AddComponent<Slider>();slider.fillRect=fill;slider.minValue=0;slider.maxValue=1;slider.direction=Slider.Direction.LeftToRight;
            slider.onValueChanged.AddListener(v=>changed(v));return slider;
        }
        void SetVolumes()
        {
            if(masterSlider!=null&&musicSlider!=null&&sfxSlider!=null)world.SetPresentationVolumes(masterSlider.value,musicSlider.value,sfxSlider.value);
        }
        void MakeScrollable(Text body,float x,float y,float w,float h)
        {
            var viewport=Panel(body.transform.parent,"Scrollable author text",x,y,w,h,new Color(0,0,0,0));
            viewport.AddComponent<RectMask2D>();body.transform.SetParent(viewport.transform,false);Rect(body.rectTransform,0,0,w-16,h);
            body.verticalOverflow=VerticalWrapMode.Overflow;
            var fitter=body.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=viewport.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;
            scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=body.rectTransform;scroll.scrollSensitivity=22;
        }
        void RefreshPolishPanels()
        {
            if(polishRoot==null)return;
            bool enabled=world.DungeonPresentation || world.P6Enabled;
            bool playing=enabled&&!world.Menu;
            bool reacting=world.State.activePendingEvent!=null;
            compactResources.SetActive(playing&&!reacting&&((!world.DungeonModal&&world.State.mode!=CombatMode.CombatPaused&&world.State.inCombat)||(FocusFeedback!=null&&FocusFeedback.RecoveryVisible)));
            polishRoot.SetActive(playing&&!world.DungeonModal&&world.State.mode!=CombatMode.CombatPaused);
            foreach(var g in legacyDecoration)if(g!=null)g.gameObject.SetActive(!enabled);
            resources.gameObject.SetActive(!enabled);stateLabel.gameObject.SetActive(!enabled);message.gameObject.SetActive(!enabled);phaseLabel.gameObject.SetActive(!enabled);
            actorInfo.gameObject.SetActive(!reacting&&(!enabled || playing&&world.IsFocus));
            enemyInfo.gameObject.SetActive(!reacting&&(!enabled || playing&&world.IsFocus));
            if(enabled)
            {
                partyRoster.SetActive(playing&&!world.DungeonModal);
                Rect(partyRoster.GetComponent<RectTransform>(),16,14,240,137);
                int row=0;
                foreach(var actor in world.State.actors)
                {
                    string key="active-"+actor.id;if(!buttons.ContainsKey(key))continue;
                    MoveButton(key,6,5+row++*43,228,38);
                    var label=buttons[key].GetComponentInChildren<Text>(); label.fontSize=15;label.alignment=TextAnchor.MiddleLeft;
                    bool logical=world.V13Enabled&&!world.IsPhysical(actor);
                    bool criticalHealth=!logical&&actor.Alive && actor.hp < actor.maxHp*.30f;
                    label.text=logical?ActorName(actor.id)+" · 连携 "+(actor.v12.basicCooldown>0?actor.v12.basicCooldown.ToString("0.0")+"s":"就绪"):(world.State.activeActorId==actor.id?"● ":"  ")+ActorName(actor.id)+"   "+actor.hp+" / "+actor.maxHp+(criticalHealth?"  危险":"");
                    label.color=criticalHealth?new Color(1f,.45f,.32f):Color.white;
                    if(healthBars.TryGetValue(actor.id,out var bar)){bar.gameObject.SetActive(!logical);var track=bar.transform.parent.Find("Health track");if(track!=null)track.gameObject.SetActive(!logical);bar.rectTransform.sizeDelta=new Vector2(210*Mathf.Clamp01((float)actor.hp/actor.maxHp),3);bar.color=criticalHealth?new Color(1,.25f,.18f,actor.hp<actor.maxHp*.15f?.5f+.5f*LowHealthOverlay.Pulse:1):Teal;}
                    if(activeBorders.TryGetValue(actor.id,out var border))border.SetActive(world.State.activeActorId==actor.id);
                }
                Rect(actorInfo.rectTransform,20,181,282,142);actorInfo.fontSize=15;
                Rect(enemyInfo.rectTransform,20,333,282,89);enemyInfo.fontSize=14;
                dungeonInfoPanel.SetActive(false);
                menuTitle.text="战术地牢  /  DEMO";
                menuInfo.text=StoryText.Load().title+"\n\n三人小队 · 实时行动 · 骰子决策\n\n开始后跟随短提示探索。\n可随时在设置中跳过或重置教程。";
                compactDice.text=new string('◆',world.State.charges)+new string('◇',Mathf.Max(0,world.State.chargeCap-world.State.charges));
                momentumLabel.text="战机 "+Mathf.FloorToInt(world.State.momentum)+" / 100";
                momentumFill.rectTransform.sizeDelta=new Vector2(260*Mathf.Clamp01(world.State.momentum/100),4);
                var budget=world.State.focusBudget;
                focusBudgetText.text="专注 "+budget.secondsRemaining.ToString("0.0")+"s"+(budget.active&&budget.entryDrainMultiplier>1?"  ×2":"")+"    反应 "+Mathf.Max(0,FocusBudgetRules.ReactionOpportunitiesPerEncounter-world.State.reactionOpportunityRoots.Count);
                var currentTarget=world.State.Actor(world.SelectedTarget);
                string opportunities=world.P5Enabled&&currentTarget!=null&&world.State.p5!=null?string.Join(" · ",world.State.p5.opportunities.Where(t=>t.targetId==currentTarget.id&&!t.consumed&&t.remaining>0&&t.p5.status=="Available").Select(t=>BattleWorld.OpportunityName(t.p5.kind)+Mathf.CeilToInt(t.remaining)+"s").Take(3)):"";
                compactOpportunities.text=opportunities;compactOpportunities.gameObject.SetActive(opportunities.Length>0);
                Rect(compactTarget.GetComponent<RectTransform>(),269,14,211,opportunities.Length>0?82:57);
                compactTarget.SetActive(!reacting&&!world.IsFrozen&&currentTarget!=null&&currentTarget.Alive&&(!world.V13Enabled||world.IsPhysical(currentTarget)));
                if(currentTarget!=null){compactTargetName.text=world.DisplayTarget(currentTarget.id)+"  "+currentTarget.hp+" / "+currentTarget.maxHp;compactTargetHp.rectTransform.sizeDelta=new Vector2(191*Mathf.Clamp01((float)currentTarget.hp/currentTarget.maxHp),4);}
                objectiveLabel.text="当前目标\n"+world.PresentationObjective;
                bool rolled=world.State.mode==CombatMode.RollFreeze;
                buttons["settings"].gameObject.SetActive(!rolled&&!reacting);
                objectivePanel.SetActive(!reacting);
                if(rolled&&world.Tutorial!=null&&world.Tutorial.State.status=="Active"&&world.State.pendingRoll!=null)
                {
                    var roll=world.State.pendingRoll;
                    rollKindText.text += roll.rollType=="Save"?"\n敌方总值 < DC 时，我方效果生效":"\n检定结果 + 修正 ≥ 目标值即可成功";
                    rollKindText.fontSize=13;Rect(rollKindText.rectTransform,14,83,410,45);
                }
                modeBadge.text=rolled?"掷骰中 · 世界完全暂停":world.IsFocus?(world.Session.Scale==0?"战术专注 · 暂停辅助":"战术专注 · 子弹时间"):world.State.inCombat?"":"探索";
                modeBadge.gameObject.SetActive(!rolled&&!reacting&&world.State.mode!=CombatMode.PostRollDecision);
                interactionPanel.SetActive(!world.IsFocus&&!world.IsFrozen&&world.NearbyPresentationPrompt!=null);
                interactionLabel.text=world.NearbyPresentationPrompt??"";
                tutorialPanel.SetActive(world.TutorialPromptText.Length>0&&!rolled&&!reacting);
                tutorialLabel.text=world.TutorialPromptText;
                actionStrip.SetActive(!world.IsFocus&&!rolled&&world.State.inCombat&&world.Tutorial!=null&&world.Tutorial.State.status=="Active");
                if(previousMessage!=world.Message){previousMessage=world.Message;toastUntil=Time.unscaledTime+2.8f;}
                toast.text=world.PresentationToast;
                toast.gameObject.SetActive(Time.unscaledTime<toastUntil&&!rolled&&toast.text.Length>0&&!world.IsFocus);
                SetButtonTitle("focus",world.IsFocus?"Tab  返回实时":world.State.focusBudget.secondsRemaining<=0?"专注已耗尽":"Tab  战术专注");
                MoveButton("focus",1053,674,210,34);buttons["focus"].gameObject.SetActive(playing&&world.State.inCombat&&!rolled&&!reacting&&!world.DungeonModal&&world.State.mode!=CombatMode.CombatPaused);
                UpdateDirectionHint();
                HighlightTutorialControl();
            }
            bool firstDecision=!world.P6Enabled&&enabled&&world.Tutorial!=null&&world.Tutorial.State.status=="Active"&&
                !world.Tutorial.State.facts.Any(f=>f.key=="roll.committed")&&world.State.pendingRoll?.purpose=="Tactical";
            if(firstDecisionTicket!=world.State.pendingRoll?.id){firstDecisionTicket=world.State.pendingRoll?.id;expandedFirstDecision=false;}
            bool folded=firstDecision&&!expandedFirstDecision;
            buttons["modify"].gameObject.SetActive(!folded);buttons["precision"].gameObject.SetActive(!folded);
            buttons["post-more"].gameObject.SetActive(firstDecision);
            MoveButton("reroll",folded?18:180,233,folded?312:150,40);
            MoveButton("post-more",18,folded?286:450,312,folded?35:22);
            SetButtonTitle("post-more",folded?"更多调整 · 灵感加值 / 职业资源":"收起额外调整");
            debugPanel.SetActive(world.DebugPresentation&&!reacting);
            if(world.DebugPresentation)
            {
                var s=world.State;
                debugText.text="F1 DEBUG · v0.5.1 / P3 (P2 map)\nMode "+s.mode+"  world timescale "+Time.timeScale+"\n"+
                    "Story node: "+(world.Profile?.ActiveRoom.templateId??"arena")+"\n"+world.Message+"\n"+world.PresentationLastReason+"\n"+
                    "Actor "+s.activeActorId+"  command "+s.commandActorId+"\n"+string.Join("\n",s.enemies.Select(e=>e.id+"  AI="+e.aiState+" timer="+e.aiRemaining.ToString("0.00")))+
                    "\nRNG "+CombatJson.Write(s.random)+"\n"+(s.pendingRoll==null?"No pending roll":CombatJson.Write(s.pendingRoll));
            }
            if(world.Audio!=null&&pausePanel.activeSelf)
            {
                masterSlider.SetValueWithoutNotify(world.Audio.MasterVolume);musicSlider.SetValueWithoutNotify(world.Audio.MusicVolume);sfxSlider.SetValueWithoutNotify(world.Audio.SfxVolume);
                masterValue.text=Mathf.RoundToInt(masterSlider.value*100)+"%";musicValue.text=Mathf.RoundToInt(musicSlider.value*100)+"%";sfxValue.text=Mathf.RoundToInt(sfxSlider.value*100)+"%";
            }
        }
        void UpdateDirectionHint()
        {
            bool show=world.TryPresentationGoal(out var goal)&&!world.State.inCombat;
            edgeArrow.gameObject.SetActive(show); if(!show)return;
            var v=world.BattleCamera.WorldToViewportPoint(goal);
            bool outside=v.z<0 || v.x<.12f || v.x>.86f || v.y<.19f || v.y>.78f;
            edgeArrow.gameObject.SetActive(outside);if(!outside)return;
            var center=new Vector2(640,360);var projected=new Vector2(v.x*1280,(1-v.y)*720);var dir=(projected-center).normalized;
            var point=center+dir*Mathf.Min(490/Mathf.Max(.01f,Mathf.Abs(dir.x)),215/Mathf.Max(.01f,Mathf.Abs(dir.y)));
            Rect(edgeArrow.rectTransform,point.x-50,point.y-20,100,40);
            edgeArrow.text=(Mathf.Abs(dir.x)>Mathf.Abs(dir.y)?(dir.x>0?"→":"←"):(dir.y>0?"↓":"↑"))+" 目标";
        }
        void HighlightTutorialControl()
        {
            string key=world.TutorialHighlightKey;
            foreach(var pair in buttons)
            {
                var outline=pair.Value.GetComponent<Outline>();
                bool on=pair.Key==key&&pair.Value.interactable&&pair.Value.gameObject.activeInHierarchy;
                if(on&&outline==null){outline=pair.Value.gameObject.AddComponent<Outline>();outline.effectColor=Gold;outline.effectDistance=new Vector2(2,-2);}
                if(outline!=null)outline.enabled=on;
            }
        }
    }
}
