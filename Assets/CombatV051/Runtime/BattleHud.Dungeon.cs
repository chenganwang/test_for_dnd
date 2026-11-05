using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        GameObject dungeonInfoPanel, dungeonModalPanel;
        Text dungeonInfoText, dungeonDialogTitle, dungeonDialogBody;
        readonly List<string> renderedDungeonChoices = new List<string>();
        bool dungeonChoicesVisible, dungeonChoicesArmed;
        string dungeonChoiceTitle;
        float dungeonChoiceNotBefore;
        const float DungeonChoiceOpeningGuard = .18f;

        static bool DungeonPointerIsBusy()
        {
            var press = Pointer.current?.press;
            return press != null && (press.isPressed || press.wasPressedThisFrame || press.wasReleasedThisFrame);
        }

        void RefreshDungeonChoiceGate(bool visible, List<DungeonChoice> choices)
        {
            if (!visible)
            { dungeonChoicesVisible = false; dungeonChoicesArmed = false; return; }
            bool changed = !dungeonChoicesVisible || dungeonChoiceTitle != world.DungeonDialogTitle || renderedDungeonChoices.Count != choices.Count;
            for (int i = 0; !changed && i < choices.Count; i++) changed = renderedDungeonChoices[i] != choices[i].id;
            if (changed)
            {
                dungeonChoicesArmed = false;
                dungeonChoiceNotBefore = Time.realtimeSinceStartup + DungeonChoiceOpeningGuard;
                dungeonChoiceTitle = world.DungeonDialogTitle;
            }
            dungeonChoicesVisible = true;
            // A battle click can arrive after a kill replaces the scene with a reward.
            // Require a quiet released frame after opening before accepting a fresh press.
            // Release itself cannot arm a Button that recorded its down while disabled.
            if (!dungeonChoicesArmed && Time.realtimeSinceStartup >= dungeonChoiceNotBefore && !DungeonPointerIsBusy())
                dungeonChoicesArmed = true;
        }

        void ChooseRenderedDungeonOption(int slot)
        {
            if (!dungeonChoicesArmed || !world.DungeonModal || slot >= renderedDungeonChoices.Count) return;
            var choices = world.DungeonChoices;
            if (slot >= choices.Count || choices[slot].id != renderedDungeonChoices[slot] || !choices[slot].enabled) return;
            world.ChooseDungeonOption(renderedDungeonChoices[slot]);
        }

        void CreateDungeonPanels(Transform root)
        {
            dungeonInfoPanel = Panel(root, "Dungeon guidance", 354, 108, 548, 130, Ink);
            dungeonInfoText = Label(dungeonInfoPanel.transform, "", 14, 10, 520, 110, 17, Color.white);
            dungeonModalPanel = Panel(root, "Dungeon decision blocker", 0, 0, 1280, 720, new Color(.015f,.03f,.05f,.91f));
            var dialog = Panel(dungeonModalPanel.transform, "Dungeon decision", 286, 102, 708, 496, Ink);
            dungeonDialogTitle = Label(dialog.transform, "", 26, 20, 656, 42, 27, Teal);
            dungeonDialogBody = Label(dialog.transform, "", 26, 77, 656, 190, 18, Color.white);
            for (int i = 0; i < 6; i++)
            {
                int slot = i;
                Button(dialog.transform, "dungeon-choice-" + i, "", 26 + (i % 2) * 338, 291 + (i / 2) * 62, 318, 51,
                    () => ChooseRenderedDungeonOption(slot));
            }
        }

        void RefreshDungeonPanels()
        {
            if (dungeonInfoPanel == null) return;
            bool dungeon = world.DungeonPresentation;
            if (dungeon)
            {
                phaseLabel.text = "v0.5.1 · P2 地牢切片";
                menuTitle.text = "ACTION / CRPG · 故事与地牢";
                menuInfo.text = world.DungeonMenuText;
                SetButtonTitle("new", "新故事档（归档现有进度）");
                SetButtonTitle("continue", "继续此档案");
            }
            bool active = world.Profile != null && !world.Menu;
            dungeonInfoPanel.SetActive(active && !world.State.inCombat && !world.DungeonModal);
            dungeonInfoText.text = world.DungeonSummary;
            dungeonModalPanel.SetActive(active && world.DungeonModal);
            if (active)
            {
                endPanel.SetActive(false); // The profile's explicit story/run settlement owns this screen.
                if (!world.State.inCombat) stateLabel.text = "探索 / " + world.DungeonModeName + "\n世界时间 × " + Time.timeScale.ToString("0.00");
            }
            if (!active || !world.DungeonModal) { RefreshDungeonChoiceGate(false, null); return; }
            pausePanel.SetActive(false);
            dungeonDialogTitle.text = world.DungeonDialogTitle;
            dungeonDialogBody.text = world.DungeonDialogBody;
            var choices = world.DungeonChoices;
            RefreshDungeonChoiceGate(true, choices);
            bool buildReward=world.Profile.ActiveUsesV11&&world.DungeonDialogTitle.StartsWith("本局构筑");
            Rect(dungeonDialogTitle.transform.parent.GetComponent<RectTransform>(),286,buildReward?76:102,708,buildReward?562:496);
            // The existing body is a scroll viewport; only its viewport changes size.
            var bodyViewport=dungeonDialogBody.rectTransform.parent.GetComponent<RectTransform>();
            if(bodyViewport.name=="Scrollable author text")bodyViewport.sizeDelta=new Vector2(bodyViewport.sizeDelta.x,buildReward?118:190);
            renderedDungeonChoices.Clear();
            for (int i = 0; i < 6; i++)
            {
                var button = buttons["dungeon-choice-" + i];
                bool exists = i < choices.Count;
                button.gameObject.SetActive(exists);
                if (!exists) continue;
                renderedDungeonChoices.Add(choices[i].id);
                SetButtonTitle("dungeon-choice-" + i, choices[i].label);
                button.interactable = choices[i].enabled && dungeonChoicesArmed;
                MoveButton("dungeon-choice-"+i,buildReward?26:26+(i%2)*338,buildReward?211+i*79:291+(i/2)*62,buildReward?656:318,buildReward?70:51);
                button.GetComponentInChildren<Text>().fontSize=buildReward?15:16;
            }
        }
    }
}
