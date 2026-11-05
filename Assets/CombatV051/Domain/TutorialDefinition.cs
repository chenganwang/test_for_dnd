using System;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    [Serializable] public sealed class TutorialDefinition
    {
        public string id="v051-p3-onboarding";
        public int version=1;
        public List<TutorialPhaseDefinition> phases=new List<TutorialPhaseDefinition>
        {
            new TutorialPhaseDefinition{id="rolling",requiredContextFacts=new List<string>{"roll.active"},prioritySteps=new List<string>{"reveal"},fallbackPrompt="掷骰时世界完全暂停，等待结果",highlightKey="roll",waiting=true},
            new TutorialPhaseDefinition{id="decision",requiredContextFacts=new List<string>{"decision.active"},prioritySteps=new List<string>{"commit"},fallbackPrompt="接受并提交，或放弃本次行动",highlightKey="commit"},
            new TutorialPhaseDefinition{id="queued-focus",requiredContextFacts=new List<string>{"focus.active","queue.active"},prioritySteps=new List<string>{"execute"},fallbackPrompt="Tab 返回实时，执行已提交的行动",highlightKey="exit_focus",targetKey="enemy"},
            new TutorialPhaseDefinition{id="focus-empty",requiredContextFacts=new List<string>{"focus.active"},excludedContextFacts=new List<string>{"dice.ready"},fallbackPrompt="战术次数已用尽。Tab 返回实时，继续积累动量",highlightKey="exit_focus",targetKey="enemy",waiting=true},
            new TutorialPhaseDefinition{id="focus-ready",requiredContextFacts=new List<string>{"focus.active","dice.ready"},prioritySteps=new List<string>{"select_actor","select_action","select_target","confirm"},fallbackPrompt="选择合法行动与目标，再确认掷骰",highlightKey="confirm",targetKey="enemy"},
            new TutorialPhaseDefinition{id="queued-realtime",requiredContextFacts=new List<string>{"realtime.active","queue.active"},prioritySteps=new List<string>{"execute"},promptOverride="队员正在执行已提交的行动",fallbackPrompt="队员正在执行已提交的行动",targetKey="enemy",waiting=true}
        };
        public List<TutorialFactDefinition> facts=new List<TutorialFactDefinition>
        {
            new TutorialFactDefinition{key="move.distance",threshold=2},
            new TutorialFactDefinition{key="entry.approached"},new TutorialFactDefinition{key="entry.interacted",implies=new List<string>{"entry.approached"}},
            new TutorialFactDefinition{key="basic.hit"},new TutorialFactDefinition{key="guard.started"},new TutorialFactDefinition{key="dodge.started"},new TutorialFactDefinition{key="active.switched"},
            new TutorialFactDefinition{key="dice.available",allowSystem=true},new TutorialFactDefinition{key="dice.earned",allowSystem=true,implies=new List<string>{"dice.available"}},
            new TutorialFactDefinition{key="focus.entered"},
            new TutorialFactDefinition{key="selection.actor"},new TutorialFactDefinition{key="selection.action"},new TutorialFactDefinition{key="selection.target"},
            new TutorialFactDefinition{key="roll.confirmed",implies=new List<string>{"dice.available","focus.entered","selection.actor","selection.action","selection.target"}},
            new TutorialFactDefinition{key="roll.revealed",allowSystem=true,implies=new List<string>{"roll.confirmed"}},
            new TutorialFactDefinition{key="roll.committed",implies=new List<string>{"roll.revealed"}},
            new TutorialFactDefinition{key="action.executed",allowSystem=true,implies=new List<string>{"roll.committed"}},
            new TutorialFactDefinition{key="encounter.r1_started",allowSystem=true,recoverable=true},
            new TutorialFactDefinition{key="encounter.r1_clear",allowSystem=true,recoverable=true,implies=new List<string>{"encounter.r1_started"}},
            new TutorialFactDefinition{key="room.r2_entered",allowSystem=true,recoverable=true,implies=new List<string>{"encounter.r1_clear"}}
        };
        public List<TutorialStepDefinition> steps=new List<TutorialStepDefinition>
        {
            new TutorialStepDefinition{id="move",prompt="WASD 移动",highlightKey="move",targetKey="entry",requiredFacts=new List<string>{"move.distance"}},
            new TutorialStepDefinition{id="approach",prompt="靠近前方的入口装置",highlightKey="interact",targetKey="entry",requiredFacts=new List<string>{"entry.approached"},retireOnFacts=new List<string>{"encounter.r1_started","room.r2_entered"}},
            new TutorialStepDefinition{id="interact",prompt="[E] 调查入口装置",waitingPrompt="靠近入口装置",highlightKey="interact",targetKey="entry",waitingTargetKey="entry",requiredFacts=new List<string>{"entry.interacted"},availableFacts=new List<string>{"entry.near"},retireOnFacts=new List<string>{"encounter.r1_started","room.r2_entered"}},
            CombatStep("attack","左键：基础攻击","basic.hit","basic"),
            CombatStep("guard","右键：格挡","guard.started","guard"),
            CombatStep("dodge","空格：闪避","dodge.started","dodge"),
            new TutorialStepDefinition{id="switch",prompt="1 / 2 / 3 切换队员",highlightKey="party",requiredFacts=new List<string>{"active.switched"}},
            CombatStep("dice_available","积累动量，获得可用战术行动","dice.available","dice"),
            new TutorialStepDefinition{id="focus",prompt="Tab：进入战术专注",waitingPrompt="用基础攻击与格挡积累动量",highlightKey="focus",targetKey="enemy",waitingTargetKey="enemy",requiredFacts=new List<string>{"focus.entered"},availableFacts=new List<string>{"combat.active","dice.ready"},retireOnFacts=new List<string>{"encounter.r1_clear","room.r2_entered"},retireUnlessFacts=new List<string>{"combat.active"}},
            FocusStep("select_actor","选择要指挥的队员","selection.actor","actor"),FocusStep("select_action","选择该队员的行动","selection.action","action"),FocusStep("select_target","选择合法目标","selection.target","target"),
            FocusStep("confirm","确认掷骰：消耗一次战术行动","roll.confirmed","confirm"),
            new TutorialStepDefinition{id="reveal",prompt="掷骰时世界完全暂停",waitingPrompt="确认本次检定",highlightKey="roll",requiredFacts=new List<string>{"roll.revealed"},availableFacts=new List<string>{"roll.active"},retireOnFacts=new List<string>{"encounter.r1_clear","room.r2_entered"},retireUnlessFacts=new List<string>{"combat.active"}},
            new TutorialStepDefinition{id="commit",prompt="结果已定。接受并提交行动",waitingPrompt="选择合法行动并确认掷骰",highlightKey="commit",requiredFacts=new List<string>{"roll.committed"},availableFacts=new List<string>{"decision.active"},retireOnFacts=new List<string>{"encounter.r1_clear","room.r2_entered"},retireUnlessFacts=new List<string>{"combat.active"}},
            new TutorialStepDefinition{id="execute",prompt="Tab 返回实时，让队员执行行动",waitingPrompt="提交行动后返回实时",highlightKey="exit_focus",targetKey="enemy",requiredFacts=new List<string>{"action.executed"},availableFacts=new List<string>{"queue.active"},retireOnFacts=new List<string>{"encounter.r1_clear","room.r2_entered"},retireUnlessFacts=new List<string>{"combat.active"}},
            new TutorialStepDefinition{id="clear_r1",prompt="完成当前遭遇",waitingPrompt="前往东侧房间",targetKey="enemy",waitingTargetKey="R1",requiredFacts=new List<string>{"encounter.r1_clear"},availableFacts=new List<string>{"combat.active"}},
            new TutorialStepDefinition{id="reach_r2",prompt="领取或跳过奖励，穿过东侧出口",targetKey="R2",requiredFacts=new List<string>{"room.r2_entered"}}
        };
        static TutorialStepDefinition CombatStep(string id,string prompt,string fact,string highlight)
        {return new TutorialStepDefinition{id=id,prompt=prompt,waitingPrompt="前往东侧房间",highlightKey=highlight,targetKey="enemy",waitingTargetKey="R1",requiredFacts=new List<string>{fact},availableFacts=new List<string>{"combat.active","enemy.available"},retireOnFacts=new List<string>{"encounter.r1_clear","room.r2_entered"},retireUnlessFacts=new List<string>{"combat.active"}};}
        static TutorialStepDefinition FocusStep(string id,string prompt,string fact,string highlight)
        {return new TutorialStepDefinition{id=id,prompt=prompt,waitingPrompt="Tab 进入战术专注",highlightKey=highlight,targetKey="enemy",requiredFacts=new List<string>{fact},availableFacts=new List<string>{"focus.active"},retireOnFacts=new List<string>{"encounter.r1_clear","room.r2_entered"},retireUnlessFacts=new List<string>{"combat.active"}};}
    }
}
