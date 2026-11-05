using System.IO;
using UnityEditor;
using UnityEngine;

namespace CombatV051.Editor
{
    public static class P3PresentationBuilder
    {
        [MenuItem("Combat v051/P3/Prepare Presentation")]
        public static void Prepare()
        {
            // These existing builders retain their ownership checks and preserve authored assets.
            P3AudioBuilder.PrepareAudio();
            CombatPrototypeBuilder.BuildPrototype();
            Directory.CreateDirectory("Reports");
            File.WriteAllText("Reports/P3_ENGINE_PresentationPreparation.txt",
                "PASS: P3 audio import/mixer validation and owned prototype preparation completed.\n" +
                "Audio entry: CombatV051.Editor.P3AudioBuilder.PrepareAudio\n" +
                "Prototype entry: CombatV051.Editor.CombatPrototypeBuilder.BuildPrototype\n" +
                "Editor version: " + Application.unityVersion + "\n" +
                "This is preparation only. Gameplay, audible output, window layout, Windows build and human play require separate evidence.\n");
            Debug.Log("P3_PRESENTATION_PREPARED: actual audio assets and owned prototype. Separate tests and Player validation remain required.");
        }
    }
}
