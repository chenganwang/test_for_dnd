using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed class PresentationButtonHover : MonoBehaviour, IPointerEnterHandler
    {
        public BattleWorld World;
        public void OnPointerEnter(PointerEventData data)
        {
            var button = GetComponent<Button>();
            if (button != null && button.interactable && World != null) World.PresentationUICue("UiHover");
        }
    }
}
