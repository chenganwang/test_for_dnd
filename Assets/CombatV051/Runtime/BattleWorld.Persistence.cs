using System.Linq;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public void CapturePresentationSnapshot()
        {
            // A title/preparation page has no live world view to publish. Preserve the
            // existing mode snapshot while editing only meta preparation or changing modes.
            // Suspend captures before setting Menu, so a real played pose is still saved.
            if (Menu || !BattleCamera || Session == null) return;
            var n = State.world.numbers;
            // A frozen view is already authoritative. Euler/quaternion round trips must not
            // mutate the stored pose each frame after loading the same paid result.
            if (IsFrozen && n.ContainsKey("cameraX")) return;
            var p = BattleCamera.transform.position; var r = BattleCamera.transform.rotation;
            n["cameraX"] = p.x; n["cameraY"] = p.y; n["cameraZ"] = p.z;
            n["cameraQx"] = r.x; n["cameraQy"] = r.y; n["cameraQz"] = r.z; n["cameraQw"] = r.w;
            n["cameraSize"] = BattleCamera.orthographicSize;
            n["aimX"] = WorldAimPoint.x; n["aimY"] = WorldAimPoint.y; n["aimZ"] = WorldAimPoint.z;
            State.world.strings["selectedAction"] = SelectedAction ?? "attack";
            State.world.strings["selectedTarget"] = SelectedTarget ?? "";
            foreach (var pair in views) n["facing/" + pair.Key] = pair.Value.eulerAngles.y;
            CaptureTacticalPreviewSnapshot();
        }
        void RestorePresentationSnapshot()
        {
            var n = State.world.numbers;
            if (new[] {"cameraX","cameraY","cameraZ","cameraQx","cameraQy","cameraQz","cameraQw","cameraSize"}.All(n.ContainsKey))
                Rig.RestoreView(new Vector3(n["cameraX"],n["cameraY"],n["cameraZ"]),
                    new Quaternion(n["cameraQx"],n["cameraQy"],n["cameraQz"],n["cameraQw"]),n["cameraSize"]);
            else
                Rig.Initialize(BattleCamera, () => Position(ActiveActor), PartyCenter, () => IsFrozen, () => IsFocus);
            if (n.ContainsKey("aimX") && n.ContainsKey("aimY") && n.ContainsKey("aimZ"))
                WorldAimPoint = new Vector3(n["aimX"],n["aimY"],n["aimZ"]);
            foreach (var pair in views)
                if (n.TryGetValue("facing/"+pair.Key,out float yaw)) pair.Value.rotation=Quaternion.Euler(0,yaw,0);
            if (State.world.strings.TryGetValue("selectedAction", out var storedAction)) SelectedAction = storedAction;
            if (State.pendingRoll != null)
            {
                SelectedTarget = State.pendingRoll.targetId;
                if (State.pendingRoll.purpose != "Reaction") SelectedAction = State.pendingRoll.actionId;
            }
            else
            {
                if (State.world.strings.TryGetValue("selectedTarget", out var savedTarget) && State.Actor(savedTarget)?.Alive == true) SelectedTarget = savedTarget;
                var actor = State.Actor(SelectedActor);
                if (actor != null && PartyMode)
                {
                    var options = AvailableActions(actor.id);
                    if (!options.Contains(SelectedAction)) SelectedAction = options.FirstOrDefault() ?? "attack";
                }
            }
            // Rebuild the same target marker before RollFreeze prevents presentation updates.
            var marked = State.Actor(State.pendingRoll != null ? State.pendingRoll.targetId : SelectedTarget);
            bool pausedFocus = State.mode == CombatV051.Domain.CombatMode.CombatPaused &&
                (State.resumeMode == CombatV051.Domain.CombatMode.TacticalFocus || State.resumeMode == CombatV051.Domain.CombatMode.PostRollDecision || State.resumeMode == CombatV051.Domain.CombatMode.ReactionPrompt);
            if (targeting != null && marked != null && marked.Alive && (IsFocus || pausedFocus || State.pendingRoll != null))
            {
                targeting.SetShape(AreaShape.Circle(Position(marked),.8f));
                targeting.gameObject.SetActive(true);
            }
            else if (targeting != null) targeting.gameObject.SetActive(false);
            RestoreTacticalPreviewSnapshot();
        }
    }
}
