using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TCG.SwitchStreaming.Editor
{
    public sealed class ReferenceAuditResult
    {
        public readonly List<ArtRecord> Eligible = new List<ArtRecord>();
        public readonly Dictionary<string, string> RetainedTextures = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> ImageFiles = new List<string>();
    }

    // Only call on an isolated preparation project. Scan is read-only; StripImages saves authored files.
    public static class ReferenceAudit
    {
        static readonly Regex GuidPattern = new Regex(@"\bguid:\s*([a-fA-F0-9]{32})\b", RegexOptions.Compiled);
        sealed class Source
        {
            public string Path;
            public bool Binary;
            public readonly Dictionary<string, int> Guids = new Dictionary<string, int>(StringComparer.Ordinal);
        }
        sealed class Index
        {
            public readonly Dictionary<string, HashSet<string>> Textures = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            public readonly Dictionary<string, HashSet<string>> ResourceTextures = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            public readonly List<Source> Sources = new List<Source>();
            public readonly HashSet<string> SpriteSources = new HashSet<string>(StringComparer.Ordinal);
        }

        public static ReferenceAuditResult Scan(string cataloguePath, IList<ArtRecord> records, IEnumerable<string> paths = null)
        {
            return Audit(cataloguePath, records, paths, true, null);
        }

        public static List<string> FindEagerReferences(IList<ArtRecord> records, IEnumerable<string> paths = null)
        {
            var unexplained = new List<string>();
            Audit(null, records, paths, false, unexplained);
            return unexplained;
        }

        static ReferenceAuditResult Audit(string cataloguePath, IList<ArtRecord> records, IEnumerable<string> paths, bool allowImages, List<string> unexplained)
        {
            var result = new ReferenceAuditResult();
            var index = BuildIndex(records, paths);
            // Resources is a build root even when its source Sprite/Texture has no incoming GUID.
            foreach (var resident in index.ResourceTextures)
            {
                foreach (string texture in resident.Value)
                    if (!result.RetainedTextures.ContainsKey(texture)) result.RetainedTextures[texture] = resident.Key + ": implicit Resources residency";
                if (unexplained != null && !unexplained.Contains(resident.Key)) unexplained.Add(resident.Key);
            }
            foreach (var source in index.Sources)
            {
                if (index.SpriteSources.Contains(source.Path)) continue;
                var relevant = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var pair in source.Guids) if (index.Textures.ContainsKey(pair.Key)) relevant[pair.Key] = pair.Value;
                if (relevant.Count == 0) continue;
                var observed = new Dictionary<string, int>(StringComparer.Ordinal);
                bool hasImage = false;
                Action<string, string> retain = (guid, reason) =>
                {
                    foreach (string texture in index.Textures[guid]) if (!result.RetainedTextures.ContainsKey(texture)) result.RetainedTextures[texture] = source.Path + ": " + reason;
                    if (unexplained != null && !unexplained.Contains(source.Path)) unexplained.Add(source.Path);
                };
                if (source.Binary)
                {
                    foreach (var guid in relevant.Keys) retain(guid, "binary asset dependency requires explicit review");
                    continue;
                }
                VisitObjects(source.Path, obj =>
                {
                    if (!obj || obj is Mesh) return;
                    var serialized = new SerializedObject(obj); var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference || !property.objectReferenceValue) continue;
                        string guid; long id;
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(property.objectReferenceValue, out guid, out id) || !index.Textures.ContainsKey(guid)) continue;
                        int count; observed.TryGetValue(guid, out count); observed[guid] = count + 1;
                        bool allowed = false;
                        if (source.Path == cataloguePath && AssetDatabase.IsMainAsset(obj))
                            foreach (var record in records)
                                if (record.property == property.propertyPath && record.spriteGuid == guid && record.spriteId == id) { allowed = true; break; }
                        if (!allowed && allowImages && IsDefaultCardImage(obj, property.propertyPath)) { allowed = true; hasImage = true; }
                        if (!allowed) retain(guid, obj.name + "." + property.propertyPath);
                    }
                });
                // Missing scripts, unrecognized serialized documents and unresolved objects cannot be excused by hierarchy inspection.
                foreach (var pair in relevant)
                {
                    int count; observed.TryGetValue(pair.Key, out count);
                    if (pair.Value > count) retain(pair.Key, "unexplained serialized GUID " + pair.Key);
                }
                if (hasImage) result.ImageFiles.Add(source.Path);
            }
            foreach (var record in records) if (!result.RetainedTextures.ContainsKey(record.textureGuid)) result.Eligible.Add(record);
            result.ImageFiles.Sort(StringComparer.Ordinal);
            if (unexplained != null) unexplained.Sort(StringComparer.Ordinal);
            return result;
        }

        static Index BuildIndex(IList<ArtRecord> records, IEnumerable<string> paths)
        {
            var index = new Index();
            foreach (var record in records)
            {
                Add(index.Textures, record.textureGuid, record.textureGuid);
                Add(index.Textures, record.spriteGuid, record.textureGuid);
                index.SpriteSources.Add(record.spritePath);
                if (IsResourcesPath(record.texturePath)) Add(index.ResourceTextures, record.texturePath, record.textureGuid);
                if (IsResourcesPath(record.spritePath)) Add(index.ResourceTextures, record.spritePath, record.textureGuid);
            }
            // Read GUID tokens only: never deserialize every component or huge Mesh payloads to discover candidates.
            foreach (string path in paths ?? AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || IsMeshPath(path) || !File.Exists(path)) continue;
                index.Sources.Add(ReadSource(path));
            }
            // Discover standalone aliases even when the caller restricts the consumer paths.
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsMeshPath(path) || AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(Sprite)) continue;
                index.SpriteSources.Add(path);
                var source = ReadSource(path);
                foreach (var pair in source.Guids)
                {
                    HashSet<string> textures;
                    if (index.Textures.TryGetValue(pair.Key, out textures))
                        foreach (string texture in new List<string>(textures))
                        {
                            Add(index.Textures, guid, texture);
                            if (IsResourcesPath(path)) Add(index.ResourceTextures, path, texture);
                        }
                }
            }
            return index;
        }

        static Source ReadSource(string path)
        {
            var source = new Source { Path = path };
            using (var reader = File.OpenText(path))
            {
                var prefix = new char[5]; int count = reader.Read(prefix, 0, prefix.Length);
                if (count == 5 && new string(prefix) == "%YAML")
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        foreach (Match match in GuidPattern.Matches(line))
                        {
                            string guid = match.Groups[1].Value.ToLowerInvariant(); int occurrences;
                            source.Guids.TryGetValue(guid, out occurrences); source.Guids[guid] = occurrences + 1;
                        }
                    return source;
                }
            }
            // Binary serialized/imported assets cannot pass a text-only prefilter. Dependency queries
            // retain direct art edges; wrappers/catalogues are audited at their own serialized source.
            source.Binary = true;
            foreach (string dependency in AssetDatabase.GetDependencies(path, false))
                if (dependency != path) source.Guids[AssetDatabase.AssetPathToGUID(dependency)] = 1;
            return source;
        }

        static void Add(Dictionary<string, HashSet<string>> map, string guid, string texture)
        {
            if (String.IsNullOrEmpty(guid) || String.IsNullOrEmpty(texture)) throw new InvalidOperationException("Artwork audit requires stable source and texture GUIDs");
            HashSet<string> textures;
            if (!map.TryGetValue(guid, out textures)) map[guid] = textures = new HashSet<string>(StringComparer.Ordinal);
            textures.Add(texture);
        }
        static bool IsResourcesPath(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            foreach (string part in path.Split('/')) if (part.Equals("Resources", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        static bool IsMeshPath(string path)
        {
            foreach (string part in path.Split('/')) if (part.Equals("Mesh", StringComparison.OrdinalIgnoreCase) || part.Equals("Meshes", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        static bool IsDefaultCardImage(UnityEngine.Object obj, string property)
        {
            var image = obj as Image;
            if (!image || property != "m_Sprite") return false;
            for (var parent = image.transform; parent; parent = parent.parent)
                foreach (var component in parent.GetComponents<Component>())
                    if (component && component.GetType().Name == "CardUI") return true;
            return false;
        }

        public static void StripImages(IEnumerable<string> paths, ISet<string> eligibleTextureGuids, Sprite placeholder)
        {
            if (!placeholder) throw new ArgumentNullException("placeholder", "An authored resident placeholder is required");
            if (eligibleTextureGuids.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(placeholder.texture)))) throw new InvalidOperationException("Placeholder cannot use a streamed texture");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (!seen.Add(path)) continue;
                VisitObjects(path, obj =>
                {
                    var image = obj as Image;
                    if (!image || !IsDefaultCardImage(image, "m_Sprite") || !image.sprite) return;
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(image.sprite.texture));
                    if (!eligibleTextureGuids.Contains(guid)) return;
                    var serialized = new SerializedObject(image);
                    serialized.FindProperty("m_Sprite").objectReferenceValue = placeholder;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(image);
                }, true);
            }
            AssetDatabase.SaveAssets();
        }

        static void VisitObjects(string path, Action<UnityEngine.Object> visit, bool save = false)
        {
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try { VisitHierarchy(root, visit); if (save) PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            else if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (var root in scene.GetRootGameObjects()) VisitHierarchy(root, visit);
                    if (save) { EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Failed to save audited scene: " + path); }
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            else foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path)) visit(obj);
        }
        static void VisitHierarchy(GameObject root, Action<UnityEngine.Object> visit)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                visit(transform.gameObject);
                foreach (var component in transform.GetComponents<Component>()) if (component) visit(component);
            }
        }
    }
}
