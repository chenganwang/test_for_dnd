using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CombatV051.Runtime
{
    public sealed class BattleInput : IDisposable
    {
        public readonly InputActionMap Map = new InputActionMap("BattleV051");
        public readonly InputAction Move, Pointer, Attack, Guard, Dodge, Aim, Focus, Pause, Confirm, Interact, Reaction, Debug, Followup, Smite;
        public readonly InputAction[] Characters = new InputAction[3];
        public InputAction CoreBasic=>Followup;
        public InputAction CoreSkill=>Smite;
        public InputAction Assist1=>Characters[0];
        public InputAction Assist2=>Characters[1];
        public InputAction TacticalFocus=>Focus;
        public BattleInput()
        {
            Move = Map.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Pointer = Map.AddAction("Pointer", InputActionType.Value, "<Pointer>/position");
            Attack = Button("Attack", "<Mouse>/leftButton");
            Guard = Button("Guard", "<Mouse>/rightButton");
            Dodge = Button("Dodge", "<Keyboard>/space");
            Aim = Button("Aim", "<Keyboard>/leftShift");
            Focus = Button("Focus", "<Keyboard>/tab");
            Smite = Button("Smite", "<Keyboard>/q");
            Pause = Button("Pause", "<Keyboard>/escape");
            Confirm = Button("Confirm", "<Keyboard>/enter");
            Interact = Button("Interact", "<Keyboard>/e");
            Reaction = Button("Reaction", "<Keyboard>/r");
            Followup = Button("Followup", "<Keyboard>/f");
            Debug = Button("Debug", "<Keyboard>/f1");
            for (var i = 0; i < 3; i++) Characters[i] = Button("Character" + (i + 1), "<Keyboard>/" + (i + 1));
            Map.Enable();
        }
        InputAction Button(string name, string path) => Map.AddAction(name, InputActionType.Button, path);
        public void Dispose() => Map.Dispose();
    }
}
