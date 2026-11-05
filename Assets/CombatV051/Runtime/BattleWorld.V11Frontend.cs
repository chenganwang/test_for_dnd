using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        ProfileSession v11InitializedProfile;
        int v11RosterPage, v11PreparationSlot;
        string v11SelectedNode;
        public bool V11FrontendEnabled => p6RuntimeRules;
        public System.Collections.Generic.IReadOnlyList<V11NodeDefinition> V13TreeNodes=>V13Trees.For(V11CurrentCharacter,Profile?.V11PreparationDraft.finnTree??"growth");
        public int V11RosterPage => v11RosterPage;
        public int V11PreparationSlot => v11PreparationSlot;
        public string V11SelectedNode => v11SelectedNode;
        public string V11CurrentCharacter => V11Catalog.FindCharacter(growthRole)!=null?growthRole:V11Catalog.Characters[0].id;
        public int V11RosterPages => Math.Max(1,(V11Catalog.Characters.Count(c=>c.category=="Companion")+3)/4);
        string V11FrontTitle => !V11FrontendEnabled?null:FrontPage=="characters"?"角色":FrontPage=="character-tree"?V11Catalog.FindCharacter(V11CurrentCharacter).displayName:FrontPage=="prepare"?"探索准备":null;
        string V11FrontBody
        {
            get
            {
                if(!V11FrontendEnabled)return null;
                if(FrontPage=="campaign")return "正式剧情开发中，由作者继续填写。\n现有 Demo 可先在训练中练习，再进入探索。";
                if(FrontPage=="characters")return "点击角色查看养成。编队与核心选择在探索准备中进行。";
                if(FrontPage=="character-tree")return "Demo 成长额度："+(Profile?.V11Progress?.points??0)+"  ·  "+(V11Catalog.FindCharacter(V11CurrentCharacter).category=="Main"?"新探索双主动默认开放；永久节点另存":"永久解锁下次出发时生效");
                if(FrontPage=="prepare")return "选择战斗模式与本局核心；出发后锁定。此处配置只影响下一局。";
                return null;
            }
        }
        void EnsureV11FrontendProgress()
        {
            if(!V11FrontendEnabled||Profile==null||ReferenceEquals(v11InitializedProfile,Profile))return;
            Profile.EnableV14Departures();
            var result=Profile.EnsureV11Progress("v11-frontend-initialize/"+Profile.State.profileInstanceId);
            if(result.success)v11InitializedProfile=Profile;
            else Message="角色记录未初始化："+V11Reason(result.reason);
        }
        public string V11CharacterInfo(string id)
        {
            var c=V11Catalog.FindCharacter(id);if(c==null)return "未知角色，原记录保留。";
            return c.displayName+" · "+(c.category=="Main"?"主线角色":"同行者")+"\n"+(c.coreSupported?"已支持核心构筑":"专属核心池未完成，可作为基础队友出战")+"\n"+
                string.Join("\n",V11Catalog.NodesFor(id).Select(n=>(n.kind=="Passive"?"被动 · ":"主动 · ")+n.displayName));
        }
        bool TryV11FrontChoices(out List<DungeonChoice> choices)
        {
            choices=null;if(!V11FrontendEnabled)return false;
            if(FrontPage=="campaign") {choices=new List<DungeonChoice>{new DungeonChoice("training","圣武士练习"),new DungeonChoice("training-main","三主角练习"),new DungeonChoice("main","返回")};return true;}
            if(Profile==null)return false;
            var c=new List<DungeonChoice>();
            if(FrontPage=="characters")
            {
                foreach(var character in V11Catalog.Characters.Where(d=>d.category=="Main"))c.Add(new DungeonChoice("role:"+character.id,character.displayName));
                foreach(var character in V11Catalog.Characters.Where(d=>d.category=="Companion").Skip(v11RosterPage*4).Take(4))c.Add(new DungeonChoice("role:"+character.id,character.displayName));
                if(V11RosterPages>1){c.Add(new DungeonChoice("roster-prev","上一页",v11RosterPage>0));c.Add(new DungeonChoice("roster-next","下一页",v11RosterPage<V11RosterPages-1));}
                c.Add(new DungeonChoice("main","返回主界面"));c.Add(new DungeonChoice("explore","探索编队"));
            }
            else if(FrontPage=="character-tree")
            {
                if(V11CurrentCharacter==CombatSession.V12Finn)c.Add(new DungeonChoice("finn-tree","菲恩 · "+(Profile.V11PreparationDraft.finnTree=="guard"?"守息":"生长")+"  [切换树]"));
                foreach(var n in V13TreeNodes)
                {
                    var q=Profile.QuoteV11Node(V11CurrentCharacter,n.id);
                    c.Add(new DungeonChoice("node:"+n.id,(n.kind=="Passive"?"被动":"主动")+"\n"+n.displayName+"\n"+V11NodeState(q.status)));
                }
                var selected=V11Catalog.FindNode(v11SelectedNode);
                var quote=selected==null?null:Profile.QuoteV11Node(V11CurrentCharacter,selected.id);
                bool mainActive=selected?.kind=="Active"&&V11Catalog.FindCharacter(V11CurrentCharacter).category=="Main";
                c.Add(new DungeonChoice("node-confirm",quote?.status=="Unlocked"?(mainActive?"永久已解锁":"已解锁"):quote!=null?(mainActive?"保存永久解锁 · ":"确认解锁 · ")+quote.cost+" Demo 点":"选择一个节点",quote?.canUnlock==true));
                c.Add(new DungeonChoice("tree-prev","←"));c.Add(new DungeonChoice("tree-next","→"));c.Add(new DungeonChoice("characters","返回角色名册"));
            }
            else if(FrontPage=="prepare")
            {
                var draft=Profile.V11PreparationDraft;
                c.Add(new DungeonChoice("mode:PersistentTrio",(draft.partyCombatMode==PartyCombatMode.PersistentTrio?"◆ ":"")+"小队战术 · 三人实体，自由切换当前角色"));
                c.Add(new DungeonChoice("mode:CoreAndAssist",(draft.partyCombatMode==PartyCombatMode.CoreAndAssist?"◆ ":"")+"主控连携 · 一人走位，两位短暂出招"));
                for(int i=0;i<3;i++)c.Add(new DungeonChoice("slot:"+i,(i+1)+"  "+V11Name(draft.teamIds[i])+(i==v11PreparationSlot?"  ·  选择中":"")));
                foreach(var definition in V11Catalog.Characters)c.Add(new DungeonChoice("member:"+definition.id,definition.displayName,!draft.teamIds.Where((id,index)=>index!=v11PreparationSlot).Contains(definition.id)));
                foreach(var id in draft.teamIds){var d=V11Catalog.FindCharacter(id);c.Add(new DungeonChoice("core:"+id,(draft.coreId==id?"◆ 当前核心 · ":"设为核心 · ")+d.displayName,d.coreSupported));}
                c.Add(new DungeonChoice("finn-tree","菲恩："+(draft.finnTree=="guard"?"守息 · 护生 / 回春":"生长 · 寄生芽固定")+"  [切换树]"));
                c.Add(new DungeonChoice("finn-slot",draft.finnTree=="guard"?"守息双主动已锁定":"第二槽："+(draft.finnGrowthAction=="v12_finn_grove"?"森息领域 · 持续回复":"寄生爆绽 · 结算自己的剩余寄生")+"  [切换]",draft.finnTree!="guard"));
                var legal=Profile.QuoteV11Departure();c.Add(new DungeonChoice("depart","出发",CanStartExploration&&legal.legal));
                c.Add(new DungeonChoice("characters","角色养成"));c.Add(new DungeonChoice("explore","返回地图"));
            }
            else return false;
            choices=c;return true;
        }
        bool TryChooseV11Frontend(string id)
        {
            if(!V11FrontendEnabled)return false;
            if(id=="training-main"){StartMainCharacterTraining();return true;}
            if(id=="characters")
            {
                if(!EnsureFrontendProfile())return true;
                if(FrontPage!="character-tree"&&FrontPage!="characters")frontReturn=FrontPage;
                FrontPage="characters";FrontendCue("character");
            }
            else if(id.StartsWith("role:",StringComparison.Ordinal))
            {growthRole=id.Substring(5);v11SelectedNode=V11Catalog.NodesFor(growthRole).First(n=>n.kind=="Passive").id;FrontPage="character-tree";FrontendCue("character");}
            else if(id=="tree-prev"||id=="tree-next")
            {
                var all=V11Catalog.Characters;int index=all.ToList().FindIndex(d=>d.id==V11CurrentCharacter);
                growthRole=all[(index+all.Count+(id=="tree-prev"?-1:1))%all.Count].id;v11SelectedNode=V11Catalog.NodesFor(growthRole).First(n=>n.kind=="Passive").id;
            }
            else if(id.StartsWith("node:",StringComparison.Ordinal))v11SelectedNode=id.Substring(5);
            else if(id=="node-confirm")
            {
                var result=Profile.UnlockV11Node(V11CurrentCharacter,v11SelectedNode,Guid.NewGuid().ToString("N"));
                Message=result.success?"已永久保存。下次出发应用；进行中的探索保持原配置。":V11Reason(result.reason);
            }
            else if(id=="finn-tree"){var draft=Profile.V11PreparationDraft;draft.finnTree=draft.finnTree=="guard"?"growth":"guard";Profile.SaveV11Preparation(draft,Guid.NewGuid().ToString("N"));Message="只改变下一局；进行中的核心、树与祈愿库存不变。";}
            else if(id=="finn-slot"){var draft=Profile.V11PreparationDraft;draft.finnGrowthAction=draft.finnGrowthAction=="v12_finn_grove"?"v14_finn_detonate":"v12_finn_grove";Profile.SaveV11Preparation(draft,Guid.NewGuid().ToString("N"));Message="寄生芽固定，第二主动二选一；下次出发生效。";}
            else if(id.StartsWith("mode:",StringComparison.Ordinal)){var draft=Profile.V11PreparationDraft;draft.partyCombatMode=id=="mode:PersistentTrio"?PartyCombatMode.PersistentTrio:PartyCombatMode.CoreAndAssist;Profile.SaveV11Preparation(draft,Guid.NewGuid().ToString("N"));Message="已保存下一局模式；当前冒险保持原快照。";}
            else if(id=="roster-prev"||id=="roster-next")v11RosterPage=Math.Max(0,Math.Min(V11RosterPages-1,v11RosterPage+(id=="roster-prev"?-1:1)));
            else if(id.StartsWith("slot:",StringComparison.Ordinal))v11PreparationSlot=int.Parse(id.Substring(5));
            else if(id.StartsWith("member:",StringComparison.Ordinal)||id.StartsWith("core:",StringComparison.Ordinal)||id=="active"&&FrontPage=="prepare")
            {
                var draft=Profile.V11PreparationDraft;
                if(id.StartsWith("member:",StringComparison.Ordinal))
                {
                    string replacement=id.Substring(7);draft.teamIds[v11PreparationSlot]=replacement;
                    if(!draft.teamIds.Contains(draft.coreId))draft.coreId=null;
                    if(!draft.teamIds.Contains(draft.defaultActiveActorId))draft.defaultActiveActorId=replacement;
                }
                else if(id.StartsWith("core:",StringComparison.Ordinal))draft.coreId=id.Substring(5);
                else {int i=draft.teamIds.IndexOf(draft.defaultActiveActorId);draft.defaultActiveActorId=draft.teamIds[(i+1)%3];}
                var result=Profile.SaveV11Preparation(draft,Guid.NewGuid().ToString("N"));
                Message=result.success?(string.IsNullOrEmpty(draft.coreId)?"核心已移出队伍，请重新选择一个已支持构筑的核心。":"已保存下一局准备。") : V11Reason(result.reason);
            }
            else return false;
            ApplyTime();Hud.RefreshView();return true;
        }
        public static string V11Name(string id)=>V11Catalog.FindCharacter(id)?.displayName??id??"未指定";
        public static string V11NodeState(string status)=>status=="Unlocked"?"已解锁":status=="Available"?"可解锁":status=="Locked"?"前置未满足":"不可用";
        public static string V11Reason(string reason)
        {
            if(reason!=null&&reason.StartsWith("v11_prerequisite_required:",StringComparison.Ordinal))return "前置能力尚未解锁："+(V11Catalog.FindNode(reason.Substring("v11_prerequisite_required:".Length))?.displayName??"未知节点");
            switch(reason)
            {
                case "v11_prerequisite_locked":case "prerequisite_locked":return "前置节点尚未解锁。";
                case "v11_points_unavailable":case "v11_demo_points_unavailable":return "Demo 成长点数不足。";
                case "v11_core_required":case "v11_core_not_in_team":return "请指定一名队内核心。";
                case "v11_core_unsupported":case "v11_core_pool_unavailable":return "此角色的专属核心池尚未完成，可作为基础队友出战。";
                case "v11_duplicate_character":case "v11_three_distinct_characters_required":return "三名出战角色不能重复。";
                default:return string.IsNullOrEmpty(reason)?"当前配置可用。":"当前条件不满足："+reason;
            }
        }
    }
}
