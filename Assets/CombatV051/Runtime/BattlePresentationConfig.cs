using System;
using UnityEngine;

namespace CombatV051.Runtime
{
    [Serializable]
    public sealed class BattlePresentationConfig
    {
        public float moveSpeed = 4.2f;
        public float dodgeSpeed = 10f;
        public float dodgeDuration = .28f;
        public float dodgeCooldown = 1.8f;
        public float rollAnimationSeconds = 2f;
        public float quickRollSeconds = .8f;
        public float dualRollExtraSeconds = 0;
        public float quickDualRollExtraSeconds = 0;
        public float ultraRollSeconds = .3f;
        public float ultraDualRollExtraSeconds = 0;
        public float yaw = 45f;
        public float pitch = 55f;
        public float orthographicSize = 10f;
        public float focusZoom = 1.10f;
        public float enemyMoveSpeed = 1.3f;
        public float enemyTelegraphSeconds = 1.4f;
        public float enemyRecoverySeconds = 2.2f;
        public int fighterHealth = 30;
        public int enemyHealth = 36;
        public int enemyDamage = 3;
        public static BattlePresentationConfig Load()
        {
            var asset = Resources.Load<TextAsset>("BattlePresentationConfig");
            return asset == null ? new BattlePresentationConfig() : JsonUtility.FromJson<BattlePresentationConfig>(asset.text);
        }
    }
}
