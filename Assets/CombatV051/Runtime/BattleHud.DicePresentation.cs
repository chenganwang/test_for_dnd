using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        public DicePresentationLayer DicePresentation { get; private set; }
        void CreateDicePresentation()
        {
            var root = new GameObject("Dedicated dice presentation");
            root.transform.SetParent(transform, false);
            DicePresentation = root.AddComponent<DicePresentationLayer>();
            DicePresentation.Initialize(world, font, ActorName, world.P5ActionName, SourceName);
        }
    }
}
