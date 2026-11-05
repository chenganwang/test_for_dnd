using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CombatV051.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CombatV051.Editor
{
    public static class StoryTextImporter
    {
        public const string SourceRelativePath = "StorySource/story-text.json";
        public const string OutputAssetPath = "Assets/CombatV051/Resources/StoryText.json";
        const string ReportRelativePath = "Reports/V051-author-import.json";

        [MenuItem("Action CRPG/Import Author Text")]
        public static void Import()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var sourcePath = Path.Combine(projectRoot, SourceRelativePath);
            var outputPath = Path.Combine(projectRoot, OutputAssetPath);
            var report = NewReport(projectRoot);
            try
            {
                // The only source is this exact path. Never search parents, sibling projects, or old stories.
                if (!File.Exists(sourcePath))
                {
                    if (!File.Exists(outputPath)) throw new FileNotFoundException("This project has neither StorySource/story-text.json nor a previously imported StoryText resource.");
                    StoryText.ParseImported(File.ReadAllText(outputPath, Encoding.UTF8));
                    report["status"] = "SKIPPED_SOURCE_ABSENT_USING_IMPORTED_RESOURCE";
                    report["sourceRead"] = false;
                    report["outputSha256"] = Hash(File.ReadAllBytes(outputPath));
                    report["message"] = "当前工程没有作者文本源。明确跳过导入并保留已导入资源；未搜索上级目录或旧工程。兼容验证副本可使用此路径。";
                    WriteReport(projectRoot, report);
                    Debug.LogWarning((string)report["message"]);
                    return;
                }

                var originalBytes = File.ReadAllBytes(sourcePath);
                var sourceHash = Hash(originalBytes);
                report["sourceRead"] = true; report["sourceSha256Before"] = sourceHash;
                var sourceText = new UTF8Encoding(false, true).GetString(originalBytes);
                if (sourceText.Length > 0 && sourceText[0] == '\uFEFF') sourceText = sourceText.Substring(1);
                var parsed = StoryText.ParseAuthorText(sourceText);

                // A missing generated marker means the destination may be a hand-authored file.
                if (File.Exists(outputPath)) StoryText.ParseImported(File.ReadAllText(outputPath, Encoding.UTF8));
                var generated = new JObject
                {
                    ["_generatedBy"] = StoryText.Generator,
                    ["_source"] = SourceRelativePath,
                    ["_sourceSha256"] = sourceHash,
                    ["title"] = parsed.title,
                    ["introduction"] = parsed.introduction,
                    ["ending"] = parsed.ending
                };
                var output = generated.ToString(Formatting.Indented) + Environment.NewLine;
                StoryText.ParseImported(output);
                // Detect an author editing the source during the import, rather than publishing a stale read.
                if (Hash(File.ReadAllBytes(sourcePath)) != sourceHash) throw new IOException("The author source changed during import. No generated resource was written; retry the import.");
                WriteOwnedFile(outputPath, output);
                AssetDatabase.ImportAsset(OutputAssetPath, ImportAssetOptions.ForceSynchronousImport);

                var sourceHashAfter = Hash(File.ReadAllBytes(sourcePath));
                report["status"] = "IMPORTED_AUTHOR_TEXT_ONLY";
                report["sourceSha256After"] = sourceHashAfter;
                report["sourceUnchanged"] = sourceHashAfter == sourceHash;
                report["outputSha256"] = Hash(File.ReadAllBytes(outputPath));
                report["containsExplicitPlaceholders"] = parsed.title.Contains("待填写") || parsed.introduction.Contains("待填写") || parsed.ending.Contains("待填写");
                report["message"] = "三个文本字段已逐字导入。作者源未被写入；这不是完整chapter节点图导入。";
                WriteReport(projectRoot, report);
                Debug.Log((string)report["message"] + " Report: " + ReportRelativePath);
            }
            catch (Exception exception)
            {
                report["status"] = "REJECTED"; report["error"] = exception.Message;
                WriteReport(projectRoot, report);
                throw new InvalidOperationException("Author text import rejected: " + exception.Message, exception);
            }
        }

        static JObject NewReport(string root)
        {
            return new JObject
            {
                ["reportVersion"] = 1,
                ["utc"] = DateTime.UtcNow.ToString("O"),
                ["evidenceClass"] = "AUTHOR_IMPORT_MAPPING",
                ["unityVersion"] = Application.unityVersion,
                ["projectRoot"] = root,
                ["source"] = SourceRelativePath,
                ["destination"] = OutputAssetPath,
                ["scope"] = "Title, introduction and ending text only",
                ["chapterContractImported"] = false,
                ["sourceRead"] = false,
                ["mappings"] = new JArray
                {
                    new JObject { ["from"] = "title", ["to"] = "StoryText.title", ["transform"] = "none; exact text" },
                    new JObject { ["from"] = "introduction", ["to"] = "StoryText.introduction", ["transform"] = "none; exact text" },
                    new JObject { ["from"] = "ending", ["to"] = "StoryText.ending", ["transform"] = "none; exact text" }
                },
                ["chapterFieldsNotImported"] = new JArray("schemaVersion", "id", "title", "isPlaceholder", "startNode", "nodes[].id", "nodes[].type", "nodes[].speaker", "nodes[].text", "nodes[].next", "nodes[].rooms", "nodes[].grantId", "nodes[].unlocks", "nodes[].eventId", "nodes[].onResolved", "nodes[].abilities", "nodes[].checkpointId", "nodes[].nextChapter", "authorProtectedFacts", "authoringNotice"),
                ["chapterNodeTypesNotImported"] = new JArray("dialogue", "choice", "room_sequence", "grant", "check_event", "checkpoint", "chapter_end"),
                ["mechanicsNote"] = "The battle slice implements mechanics in code; no chapter graph, ability grant, reward, condition or world flag is inferred from this text file."
            };
        }

        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static void WriteOwnedFile(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".import-tmp";
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            // The resource's .meta stays untouched, retaining its GUID.
        }
        static void WriteReport(string projectRoot, JObject report)
        {
            WriteOwnedFile(Path.Combine(projectRoot, ReportRelativePath), report.ToString(Formatting.Indented) + Environment.NewLine);
        }
    }
}

