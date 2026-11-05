using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>M2's three text fields only. This is not a chapter-node graph or screenplay importer.</summary>
    [Serializable]
    public sealed class StoryText
    {
        public const string Generator = "ActionDice.StoryTextImporter/v1";
        public string title = "[标题待填写]";
        public string introduction = "[剧情待填写]";
        public string ending = "[剧情待填写]";

        static StoryText cached;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetCache(){cached=null;}
        public static StoryText Load()
        {
            if(cached!=null)return cached;
            var resource = Resources.Load<TextAsset>("StoryText");
            if (!resource) throw new InvalidOperationException("Missing imported StoryText resource. Run Action Dice/Import Author Text (M2), or copy the already imported resource into the compatibility project.");
            return cached=ParseImported(resource.text);
        }

        public static StoryText ParseAuthorText(string json)
        {
            var root = ReadObject(json);
            RequireKeys(root, new[] { "title", "introduction", "ending" });
            return ReadText(root);
        }

        public static StoryText ParseImported(string json)
        {
            var root = ReadObject(json);
            RequireKeys(root, new[] { "title", "introduction", "ending", "_generatedBy", "_source", "_sourceSha256" });
            if (ReadString(root, "_generatedBy") != Generator) throw new InvalidDataException("StoryText resource is not marked as owned by the author-text importer. It will not be overwritten.");
            if (ReadString(root, "_source") != "StorySource/story-text.json") throw new InvalidDataException("StoryText source must be the current project's StorySource/story-text.json.");
            var hash = ReadString(root, "_sourceSha256");
            if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("StoryText source hash must be a SHA-256 hex digest.");
            return ReadText(root);
        }

        static JObject ReadObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Story text must be a JSON object with three explicit text fields.");
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
            {
                var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new InvalidDataException("Trailing content after the story JSON object is not allowed.");
                return result;
            }
        }

        static void RequireKeys(JObject root, string[] allowed)
        {
            foreach (var property in root.Properties())
                if (Array.IndexOf(allowed, property.Name) < 0) throw new InvalidDataException("Unknown story-text field: " + property.Name + ". No field is silently ignored.");
            foreach (var key in allowed)
                if (root.Property(key) == null) throw new InvalidDataException("Missing required story-text field: " + key + ". Use [剧情待填写] when the author has not supplied this text.");
        }

        static string ReadString(JObject root, string key)
        {
            var value = root[key];
            if (value == null || value.Type != JTokenType.String || string.IsNullOrWhiteSpace(value.Value<string>()))
                throw new InvalidDataException("Story field " + key + " must be a nonempty string; use an explicit placeholder for missing author text.");
            return value.Value<string>();
        }

        static StoryText ReadText(JObject root)
        {
            return new StoryText { title = ReadString(root, "title"), introduction = ReadString(root, "introduction"), ending = ReadString(root, "ending") };
        }
    }
}

