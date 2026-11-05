using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    /// <summary>Unscaled, modest 180 ms paper-card response; gameplay commands occur only after confirm.</summary>
    public sealed class P47FrontendCard : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler
    {
        BattleWorld world;Button button;Image background;P47FrontendArtwork border;
        Action action;float weight,flash;bool selected,focused;string id;
        public bool Hovered {get;private set;}
        public bool Confirming=>flash>0;
        public float Highlight=>weight;
        public void Initialize(BattleWorld owner,Button target,P47FrontendArtwork edge)
        {world=owner;button=target;border=edge;background=target.GetComponent<Image>();button.transition=Selectable.Transition.None;button.onClick.AddListener(Confirm);}
        public void Bind(string key,Action callback,bool activeSelection=false)
        {if(id!=key){id=key;Hovered=false;focused=false;flash=0;}action=callback;selected=activeSelection;}
        public void Confirm()
        {
            if(!button.interactable||flash>0)return;
            world.FrontendCue("confirm");flash=.18f;
        }
        public void OnPointerEnter(PointerEventData e){if(!button.interactable)return;Hovered=true;world.FrontendCue("hover");}
        public void OnPointerExit(PointerEventData e){Hovered=false;}
        public void OnSelect(BaseEventData e){focused=true;if(button.interactable)world.FrontendCue("select");}
        public void OnDeselect(BaseEventData e){focused=false;}
        void Update()
        {
            float target=button.interactable&&(Hovered||focused||selected||flash>0)?1:0;
            float next=Mathf.MoveTowards(weight,target,Time.unscaledDeltaTime/.18f);
            if(Mathf.Abs(next-weight)>.0001f||flash>0)
            {
                weight=next;border.SetGlow(weight+(flash>0?.22f*Mathf.Sin(Mathf.PI*(1-flash/.18f)):0));
                background.color=Color.Lerp(new Color(.10f,.095f,.078f,.96f),new Color(.18f,.15f,.10f,.98f),weight);
                transform.localScale=Vector3.one*(1+weight*.012f);
            }
            if(flash<=0)return;flash-=Time.unscaledDeltaTime;
            if(flash<=0){border.SetGlow(weight);var callback=action;callback?.Invoke();}
        }
        void OnDisable(){Hovered=false;flash=0;weight=0;transform.localScale=Vector3.one;if(border)border.SetGlow(0);}
    }
}
