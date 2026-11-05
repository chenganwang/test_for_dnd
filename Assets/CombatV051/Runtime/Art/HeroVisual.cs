using UnityEngine;

namespace CombatV051.Runtime
{
    public enum HeroPose { Idle, Move, Attack, Skill, Hit, Guard, Down }

    /// <summary>Rigid low-poly display rig. Reads simulation time only; never moves a gameplay root or emits a hit.</summary>
    public sealed class HeroVisual : MonoBehaviour
    {
        [SerializeField] string characterId;
        // Hips, chest, head, left upper/lower arm, right upper/lower arm, left upper/lower leg, right upper/lower leg.
        [SerializeField] Transform[] joints;
        [SerializeField] Vector3[] restPositions;
        [SerializeField] Quaternion[] restRotations;
        [SerializeField] Transform weaponSocket, hitSocket;
        public string CharacterId => characterId;
        public Transform WeaponSocket => weaponSocket;
        public Transform HitSocket => hitSocket;
        public HeroPose Pose { get; private set; }
        public float PoseTime { get; private set; }
        public int JointCount => joints == null ? 0 : joints.Length;
        public static string ResourceKey(string id)
        {
            switch (id)
            {
                case "char_story_evan": return "Heroes/Evan";
                case "char_story_mira": return "Heroes/Mira";
                case "char_story_finn": return "Heroes/Finn";
                default: return null;
            }
        }
        public void Configure(string id, Transform[] bones, Transform weapon, Transform hit)
        {
            characterId = id; joints = bones; weaponSocket = weapon; hitSocket = hit;
            restPositions = new Vector3[bones.Length]; restRotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) { restPositions[i] = bones[i].localPosition; restRotations[i] = bones[i].localRotation; }
        }
        void Rotate(int joint, float x, float y = 0, float z = 0) { joints[joint].localRotation = restRotations[joint] * Quaternion.Euler(x, y, z); }
        public void Apply(bool alive, bool moving, bool guarding, string cue, float cueAge, float simulationTime, float flinch = 1)
        {
            if (joints == null || joints.Length != 11) return;
            PoseTime = simulationTime;
            for (int i = 0; i < joints.Length; i++) { joints[i].localPosition = restPositions[i]; joints[i].localRotation = restRotations[i]; }
            if (!alive)
            {
                Pose = HeroPose.Down;
                joints[0].localPosition = new Vector3(0, .18f, -.20f);
                Rotate(0, -82, 0, -8); Rotate(7, 22); Rotate(9, -18); Rotate(3, 0, 0, -28); Rotate(5, 0, 0, 35);
                return;
            }
            float phase = simulationTime * 10f, stride = moving ? Mathf.Sin(phase) * 27 : 0;
            Pose = moving ? HeroPose.Move : HeroPose.Idle;
            joints[0].localPosition += Vector3.up * (moving ? Mathf.Abs(Mathf.Sin(phase)) * .023f : Mathf.Sin(simulationTime * 2.1f) * .006f);
            Rotate(7, stride); Rotate(9, -stride); Rotate(8, Mathf.Max(0, -stride) * 1.25f); Rotate(10, Mathf.Max(0, stride) * 1.25f);
            Rotate(3, -stride * .55f, 0, -8); Rotate(5, stride * .55f, 0, 8);
            Rotate(4, -20); Rotate(6, -20);
            if (characterId == "char_story_evan") Rotate(4, -68, 0, 3);
            if (guarding) { Pose = HeroPose.Guard; Rotate(3, -72, -10, -18); Rotate(4, -48); Rotate(5, -42, 0, 18); }
            if (cueAge < 0 || cueAge >= .36f) return;
            float swing = Mathf.Sin(Mathf.Clamp01(cueAge / .36f) * Mathf.PI);
            if (cue == "attack")
            {
                Pose = HeroPose.Attack;
                if (characterId == "char_story_mira") { Rotate(1, 0, -38 * swing); Rotate(5, -112 * swing, -15, 25 * swing); Rotate(6, -18 - 42 * swing); }
                else { Rotate(5, -85 * swing, 0, 10); Rotate(6, -20 - 25 * swing); Rotate(1, -7 * swing); }
            }
            else if (cue == "spell" || cue == "skill")
            {
                Pose = HeroPose.Skill; Rotate(1, -9 * swing); Rotate(3, -68 * swing, 0, -30 * swing); Rotate(5, -126 * swing, 0, 26 * swing);
                Rotate(4, -28 - 22 * swing); Rotate(6, -24 - 38 * swing); Rotate(2, -12 * swing);
            }
            else if (cue == "hit")
            {
                Pose = HeroPose.Hit; float amount = Mathf.Clamp(flinch, .2f, 2) * (1 - cueAge / .36f);
                Rotate(1, -18 * amount, 0, 5 * amount); Rotate(2, -12 * amount); joints[0].localPosition += Vector3.back * (.07f * amount);
            }
            else if (cue == "block" || cue == "parry") { Pose = HeroPose.Guard; Rotate(3, -80, 0, -24); Rotate(4, -42); }
        }
    }
}
