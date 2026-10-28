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
    public sealed class MeshAuditResult
    {
        public readonly List<MeshRecord> Eligible = new List<MeshRecord>();
        public readonly Dictionary<string, string> Retained = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> PrefabFiles = new List<string>();
    }

    // Read-only audit. The caller must relocate exact eligible Mesh assets out of Resources
    // and clear only their approved renderer bindings after building the original payloads.
    public static class MeshReferenceAudit
    {
        static readonly Regex GuidPattern = new Regex(@"\bguid:\s*([a-fA-F0-9]{32})\b", RegexOptions.Compiled);
        sealed class Source
        {
            public string Path;
            public bool Binary;
            public readonly Dictionary<string, int> Guids = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        public static MeshAuditResult Scan(IList<MeshRecord> records, ISet<string> approvedPrefabPaths, IEnumerable<string> paths = null)
        {
            if (records == null) throw new ArgumentNullException("records");
            if (approvedPrefabPaths == null) throw new ArgumentNullException("approvedPrefabPaths");
            var result = new MeshAuditResult();
            var candidates = new Dictionary<string, MeshRecord>(StringComparer.Ordinal);
            // Use an ordinal copy: callers cannot make a different path eligible through a
            // case-insensitive/custom set comparer or by supplying a scene in the allowlist.
            var approved = new HashSet<string>(approvedPrefabPaths, StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record == null || String.IsNullOrEmpty(record.guid))
                    throw new ArgumentException("Mesh audit requires records with stable GUIDs", "records");
                MeshRecord existing;
                if (candidates.TryGetValue(record.guid, out existing) && (existing.mesh != record.mesh || existing.path != record.path))
                    Retain(result, record.guid, "conflicting candidate records share a GUID");
                else candidates[record.guid] = record;
                string actualGuid; long actualId;
                if (!record.mesh || String.IsNullOrEmpty(record.path) || !File.Exists(record.path) ||
                    AssetDatabase.GetAssetPath(record.mesh) != record.path ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(record.mesh, out actualGuid, out actualId) || actualGuid != record.guid)
                    Retain(result, record.guid, "candidate Mesh identity cannot be verified");
            }
            if (candidates.Count == 0) return result;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in paths ?? AssetDatabase.GetAllAssetPaths())
            {
                if (String.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || !seen.Add(path) || !File.Exists(path)) continue;
                // Do not skip Mesh/Resources folders or asset types. A source Mesh in Resources
                // is itself a planned move target, but every incoming consumer still needs audit.
                var source = ReadSource(path, candidates);
                if (source.Guids.Count == 0) continue;
                if (source.Binary)
                {
                    foreach (string guid in source.Guids.Keys)
                        Retain(result, guid, path + ": binary direct dependency requires explicit review");
                    continue;
                }
                var observed = new Dictionary<string, int>(StringComparer.Ordinal);
                bool hasBinding = false;
                try
                {
                    VisitObjects(path, obj =>
                    {
                        if (!obj) return;
                        var serialized = new SerializedObject(obj);
                        var property = serialized.GetIterator();
                        while (property.Next(true))
                        {
                            if (property.propertyType != SerializedPropertyType.ObjectReference || !property.objectReferenceValue) continue;
                            string guid; long id;
                            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(property.objectReferenceValue, out guid, out id) || !candidates.ContainsKey(guid)) continue;
                            int count; observed.TryGetValue(guid, out count); observed[guid] = count + 1;
                            var record = candidates[guid];
                            bool allowed = approved.Contains(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                                (obj is MeshFilter || obj is SkinnedMeshRenderer) && property.propertyPath == "m_Mesh" &&
                                property.objectReferenceValue is Mesh && property.objectReferenceValue == record.mesh;
                            if (allowed) hasBinding = true;
                            else Retain(result, guid, path + ": " + obj.name + "." + property.propertyPath);
                        }
                    });
                }
                catch (Exception error)
                {
                    // A broken/missing script or unreadable document cannot establish eligibility.
                    foreach (string guid in source.Guids.Keys)
                        Retain(result, guid, path + ": could not inspect serialized consumer (" + error.GetType().Name + ")");
                }
                foreach (var pair in source.Guids)
                {
                    int count; observed.TryGetValue(pair.Key, out count);
                    if (count < pair.Value) Retain(result, pair.Key, path + ": unexplained serialized GUID " + pair.Key);
                }
                if (hasBinding) result.PrefabFiles.Add(path);
            }
            foreach (var record in records)
                if (!result.Retained.ContainsKey(record.guid) && !result.Eligible.Contains(record)) result.Eligible.Add(record);
            result.PrefabFiles.Sort(StringComparer.Ordinal);
            return result;
        }

        static Source ReadSource(string path, Dictionary<string, MeshRecord> candidates)
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
                            string guid = match.Groups[1].Value.ToLowerInvariant();
                            if (!candidates.ContainsKey(guid)) continue;
                            int occurrences; source.Guids.TryGetValue(guid, out occurrences); source.Guids[guid] = occurrences + 1;
                        }
                    return source;
                }
            }
            source.Binary = true;
            // Direct edges only. A binary lighting/wrapper asset -> scene/prefab -> Mesh is
            // explained by auditing that scene/prefab, not by retaining a transitive edge.
            foreach (string dependency in AssetDatabase.GetDependencies(path, false))
            {
                if (dependency == path) continue;
                string guid = AssetDatabase.AssetPathToGUID(dependency);
                if (candidates.ContainsKey(guid)) source.Guids[guid] = 1;
            }
            return source;
        }

        static void Retain(MeshAuditResult result, string guid, string reason)
        {
            if (!result.Retained.ContainsKey(guid)) result.Retained[guid] = reason;
        }

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
                try { foreach (var root in scene.GetRootGameObjects()) VisitHierarchy(root, visit); }
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
