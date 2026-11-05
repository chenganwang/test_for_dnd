using System.Collections.Generic;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleAudio
    {
        public sealed class DiceSoundStamp
        {public string cue,ticket;public ulong draw;public float progress,realSeconds;}
        readonly List<DiceSoundStamp> diceSoundTimeline=new List<DiceSoundStamp>(256);
        public IReadOnlyList<DiceSoundStamp> DiceSoundTimeline=>diceSoundTimeline;
        AudioSource diceRattle;
        bool diceThrowPlayed,diceRattlePaused;
        int diceBounceIndex;
        public bool DiceRattlePlaying=>diceRattle&&diceRattle.isPlaying;
        public bool DiceRattlePaused=>diceRattlePaused;
        void InitializeDiceSound()
        {
            diceRattle=NewSource("Dedicated wooden dice roll",uiGroup,false);
            diceRattle.loop=true;diceRattle.clip=ClipFor(AudioCue.DiceRoll);
        }
        void ResetDiceSoundDraw()
        {if(diceRattle)diceRattle.Stop();diceThrowPlayed=false;diceRattlePaused=false;diceBounceIndex=0;}
        void StampDiceSound(AudioCue cue,RollTicket roll,float progress)
        {
            if(diceSoundTimeline.Count==256)diceSoundTimeline.RemoveAt(0);
            diceSoundTimeline.Add(new DiceSoundStamp{cue=cue.ToString(),ticket=roll.id,draw=roll.rngAfter,progress=progress,realSeconds=Time.realtimeSinceStartup});
        }
        void DiceOneShot(AudioCue cue,RollTicket roll,float progress,string suffix)
        {if(PlayCue(cue,sourceId:animationCueRoot+suffix))StampDiceSound(cue,roll,progress);}
        void AdvanceDiceSound(RollTicket roll,float progress)
        {
            if(!diceThrowPlayed){DiceOneShot(AudioCue.DiceThrow,roll,progress,"/throw");diceThrowPlayed=true;}
            float firstContact=DicePresentationLayer.BounceFractions[0];
            if(progress>=firstContact&&!animationRollPlayed)
            {
                if(diceRattle&&diceRattle.clip&&MixerReady&&Remember(AudioCue.DiceRoll+"/"+animationCueRoot+"/roll"))
                {diceRattle.pitch=1.05f;diceRattle.volume=config.uiGain*.46f;diceRattle.Play();played[AudioCue.DiceRoll]=PlayedCount(AudioCue.DiceRoll)+1;StampDiceSound(AudioCue.DiceRoll,roll,progress);}
                animationRollPlayed=true;
            }
            while(diceBounceIndex<DicePresentationLayer.BounceFractions.Length&&progress>=DicePresentationLayer.BounceFractions[diceBounceIndex])
            {DiceOneShot(AudioCue.DiceBounce,roll,progress,"/bounce/"+diceBounceIndex);diceBounceIndex++;}
            if(diceRattle&&progress<DicePresentationLayer.SettleFraction)
            {
                float friction=Mathf.InverseLerp(firstContact,DicePresentationLayer.SettleFraction,progress);
                diceRattle.pitch=Mathf.Lerp(1.15f,.68f,friction);diceRattle.volume=config.uiGain*Mathf.Lerp(.46f,.16f,friction);
            }
            if(progress>=DicePresentationLayer.SettleFraction&&!animationLandPlayed)
            {if(diceRattle)diceRattle.Stop();diceRattlePaused=false;DiceOneShot(AudioCue.DiceLand,roll,progress,"/land");animationLandPlayed=true;}
            if(progress>=DicePresentationLayer.BreakdownFraction&&!animationOutcomePlayed)
            {DiceOneShot(roll.selectedFace==1?AudioCue.NaturalOne:roll.selectedFace==20?AudioCue.Critical:roll.hit?AudioCue.Success:AudioCue.Failure,roll,progress,"/outcome");animationOutcomePlayed=true;}
        }
        void UpdateDiceSoundPause()
        {
            if(!diceRattle||world?.Session==null)return;
            bool pause=world.Menu||world.DungeonModal||world.State.mode==CombatMode.CombatPaused||(!world.IgnoreApplicationFocus&&!Application.isFocused);
            if(pause)
            {if(!diceRattlePaused&&diceRattle.isPlaying){diceRattle.Pause();diceRattlePaused=true;}return;}
            if(world.State.mode!=CombatMode.RollFreeze||world.State.pendingRoll?.id!=animatedTicket)
            {diceRattle.Stop();diceRattlePaused=false;return;}
            if(diceRattlePaused){diceRattle.UnPause();diceRattlePaused=false;}
        }
    }
}
