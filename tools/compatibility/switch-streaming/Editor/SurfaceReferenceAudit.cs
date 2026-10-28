using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TCG.SwitchStreaming.Editor
{
    public sealed class SurfaceAuditResult
    {
        public readonly List<SurfaceRecord> EligibleRecords = new List<SurfaceRecord>();
        public readonly Dictionary<string, string> RetainedTextures = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // Read-only, conservative audit of original Texture2D identities. Only the exact main
    // catalogue object/property is authorized; a folder, material, Sprite or importer is not.
    public static class SurfaceReferenceAudit
    {
        static readonly Regex GuidPattern = new Regex(@"\bguid:\s*([a-fA-F0-9]{32})\b", RegexOptions.Compiled);
        sealed class Candidate
        {
            public SurfaceRecord Record;
            public long LocalId;
        }
        sealed class Source
        {
            public bool Binary;
            public readonly Dictionary<string, int> Guids = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        public static SurfaceAuditResult Scan(string cataloguePath, IList<SurfaceRecord> records, IEnumerable<string> paths = null)
        {
            return Audit(cataloguePath, records, paths, null);
        }

        // No catalogue allowance here: even an original authorized slot must be gone.
        // Identifiers survive bundle builds; callers must reload the Texture2D pointers.
        public static List<string> FindEagerReferences(IList<SurfaceRecord> records, IEnumerable<string> paths = null)
        {
            var offending = new HashSet<string>(StringComparer.Ordinal);
            Audit(null, records, paths, offending);
            var result = new List<string>(offending); result.Sort(StringComparer.Ordinal); return result;
        }

        static SurfaceAuditResult Audit(string cataloguePath, IList<SurfaceRecord> records, IEnumerable<string> paths, HashSet<string> offending)
        {
            if (records == null) throw new ArgumentNullException("records");
            var result = new SurfaceAuditResult();
            Action<string, string, string> retain = (guid, path, reason) =>
            {
                if (!result.RetainedTextures.ContainsKey(guid)) result.RetainedTextures[guid] = path + ": " + reason;
                if (offending != null) offending.Add(path);
            };
            var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record == null || String.IsNullOrEmpty(record.textureGuid))
                    throw new ArgumentException("Surface audit requires stable Texture2D GUIDs", "records");
                string guid = null; long id = 0;
                bool verified = record.texture && !String.IsNullOrEmpty(record.texturePath) && File.Exists(record.texturePath) &&
                    AssetDatabase.GetAssetPath(record.texture) == record.texturePath &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(record.texture, out guid, out id) && guid == record.textureGuid;
                if (!verified || String.IsNullOrEmpty(record.key) || String.IsNullOrEmpty(record.property) ||
                    record.texture.name != record.textureName || !AssetDatabase.IsMainAsset(record.texture))
                    retain(record.textureGuid, record.texturePath ?? "<candidate>", "candidate Texture2D identity cannot be verified");
                Candidate existing;
                if (candidates.TryGetValue(record.textureGuid, out existing))
                {
                    if (existing.Record.texturePath != record.texturePath || existing.Record.texture != record.texture || existing.LocalId != id)
                        retain(record.textureGuid, record.texturePath ?? "<candidate>", "conflicting candidate identities share a GUID");
                }
                else candidates.Add(record.textureGuid, new Candidate { Record = record, LocalId = id });
            }
            if (candidates.Count == 0) return result;
            if (cataloguePath != null)
            {
                var catalogue = File.Exists(cataloguePath) ? AssetDatabase.LoadMainAssetAtPath(cataloguePath) : null;
                if (!catalogue) foreach (string guid in candidates.Keys) retain(guid, cataloguePath, "catalogue identity cannot be verified");
                else
                {
                    var serialized = new SerializedObject(catalogue);
                    foreach (var record in records)
                    {
                        var slot = String.IsNullOrEmpty(record.property) ? null : serialized.FindProperty(record.property);
                        string guid; long id;
                        if (slot == null || slot.propertyType != SerializedPropertyType.ObjectReference ||
                            !slot.objectReferenceValue || slot.objectReferenceValue != record.texture ||
                            !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(slot.objectReferenceValue, out guid, out id) ||
                            guid != record.textureGuid || id != candidates[record.textureGuid].LocalId)
                            retain(record.textureGuid, cataloguePath, "recorded catalogue slot identity has drifted: " + record.property);
                    }
                }
            }

            // A source texture is not a residency root by itself, but Resources and extra
            // subassets are. This applies even if a fixture restricts the consumer paths.
            foreach (var pair in candidates)
            {
                string path = pair.Value.Record.texturePath;
                if (String.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                if (IsResources(path)) retain(pair.Key, path, "implicit Resources residency");
                try
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    if (assets.Length != 1 || assets[0] != pair.Value.Record.texture)
                        retain(pair.Key, path, "source is not exactly one main Texture2D; subassets require review");
                }
                catch (Exception error) { retain(pair.Key, path, "cannot inspect source subassets (" + error.GetType().Name + ")"); }
            }

            var sources = new HashSet<string>(StringComparer.Ordinal);
            bool explicitPaths = paths != null;
            foreach (string path in paths ?? AssetDatabase.GetAllAssetPaths())
            {
                if (String.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || IsStreamingAssets(path) || AssetDatabase.IsValidFolder(path)) continue;
                if (!File.Exists(path))
                {
                    if (explicitPaths) foreach (string guid in candidates.Keys) retain(guid, path, "consumer source is missing");
                    continue;
                }
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) sources.Add(path.Substring(0, path.Length - 5));
                else sources.Add(path);
            }
            foreach (var pair in candidates) if (!String.IsNullOrEmpty(pair.Value.Record.texturePath)) sources.Add(pair.Value.Record.texturePath);
            // A standalone Sprite is a strong serialized reference to its texture, even
            // if no consumer mentions that Sprite. Do not migrate or exempt Sprite aliases.
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsStreamingAssets(path) && AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(Sprite)) sources.Add(path);
            }
            foreach (string path in sources)
            {
                try
                {
                    InspectMetadata(path, candidates, retain);
                    if (!File.Exists(path)) continue;
                    var source = ReadSource(path, candidates);
                    if (source.Guids.Count == 0) continue;
                    if (source.Binary)
                    {
                        foreach (string guid in source.Guids.Keys) retain(guid, path, "binary/imported direct dependency requires review");
                        continue;
                    }
                    var observed = new Dictionary<string, int>(StringComparer.Ordinal);
                    VisitObjects(path, obj =>
                    {
                        if (!obj) return;
                        var serialized = new SerializedObject(obj); var property = serialized.GetIterator();
                        while (property.Next(true))
                        {
                            if (property.propertyType != SerializedPropertyType.ObjectReference || !property.objectReferenceValue) continue;
                            string guid; long id;
                            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(property.objectReferenceValue, out guid, out id) || !candidates.ContainsKey(guid)) continue;
                            int count; observed.TryGetValue(guid, out count); observed[guid] = count + 1;
                            bool allowed = false;
                            if (cataloguePath != null && path == cataloguePath && AssetDatabase.IsMainAsset(obj) && property.objectReferenceValue is Texture2D)
                                foreach (var record in records)
                                    if (record.property == property.propertyPath && record.textureGuid == guid &&
                                        record.texture == property.objectReferenceValue && candidates[guid].LocalId == id) { allowed = true; break; }
                            if (!allowed) retain(guid, path, obj.name + "." + property.propertyPath);
                        }
                    });
                    // Raw GUIDs that deserialize as null/missing scripts/unknown properties
                    // must never disappear from the audit simply because Unity omits them.
                    foreach (var pair in source.Guids)
                    {
                        int count; observed.TryGetValue(pair.Key, out count);
                        if (count < pair.Value) retain(pair.Key, path, "unexplained serialized GUID " + pair.Key);
                    }
                }
                catch (Exception error)
                {
                    // An unreadable source cannot establish absence of a strong reference.
                    foreach (string guid in candidates.Keys) retain(guid, path, "cannot inspect consumer (" + error.GetType().Name + ")");
                }
            }
            foreach (var record in records)
                if (!result.RetainedTextures.ContainsKey(record.textureGuid) && !result.EligibleRecords.Contains(record)) result.EligibleRecords.Add(record);
            return result;
        }

        static void InspectMetadata(string path, Dictionary<string, Candidate> candidates, Action<string, string, string> retain)
        {
            string metadata = path + ".meta";
            if (!File.Exists(metadata))
            {
                // Imported asset identity without its metadata is ambiguous.
                foreach (string guid in candidates.Keys) retain(guid, path, "asset metadata is missing");
                return;
            }
            int identityLines = 0; bool identityMatches = false;
            foreach (string line in File.ReadLines(metadata))
            {
                // Only a verified unindented asset identity is allowed, not arbitrary importer refs.
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                {
                    identityLines++; var identity = GuidPattern.Match(line);
                    identityMatches = identity.Success && identity.Groups[1].Value == AssetDatabase.AssetPathToGUID(path);
                    continue;
                }
                foreach (Match match in GuidPattern.Matches(line))
                {
                    string guid = match.Groups[1].Value.ToLowerInvariant();
                    if (candidates.ContainsKey(guid)) retain(guid, metadata, "importer metadata references texture");
                }
            }
            if (identityLines != 1 || !identityMatches)
                foreach (string guid in candidates.Keys) retain(guid, metadata, "asset metadata identity is ambiguous or has drifted");
        }

        static Source ReadSource(string path, Dictionary<string, Candidate> candidates)
        {
            var source = new Source();
            using (var reader = File.OpenText(path))
            {
                var prefix = new char[5]; int count = reader.Read(prefix, 0, 5);
                if (count == 5 && new string(prefix) == "%YAML")
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        foreach (Match match in GuidPattern.Matches(line))
                        {
                            string guid = match.Groups[1].Value.ToLowerInvariant();
                            if (!candidates.ContainsKey(guid)) continue;
                            int occurrences; source.Guids.TryGetValue(guid, out occurrences); source.Guids[guid] = occurrences + 1;
                        }
                }
                else source.Binary = true;
            }
            // Use direct edges: binary wrapper -> catalogue -> texture is handled by
            // auditing the catalogue, not by inventing a direct wrapper -> texture edge.
            foreach (string dependency in AssetDatabase.GetDependencies(path, false))
            {
                if (dependency == path) continue;
                string guid = AssetDatabase.AssetPathToGUID(dependency);
                if (candidates.ContainsKey(guid) && !source.Guids.ContainsKey(guid)) source.Guids[guid] = 1;
            }
            return source;
        }

        static bool IsResources(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            foreach (string part in path.Split('/')) if (part.Equals("Resources", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        static bool IsStreamingAssets(string path) { return path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase); }
        static void VisitObjects(string path, Action<UnityEngine.Object> visit)
        {
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try { VisitHierarchy(root, visit); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            else if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (var root in scene.GetRootGameObjects()) VisitHierarchy(root, visit);
                    // Scene assets can also embed materials and other serialized objects.
                    foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path)) visit(obj);
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
