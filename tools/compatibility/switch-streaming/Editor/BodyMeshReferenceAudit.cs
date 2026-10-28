using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TCG.SwitchStreaming.Editor
{
    [Serializable] public sealed class BodySlot
    {
        public string ownerPath, rendererPath, meshGuid, ownerId, rendererId;
    }
    [Serializable] public sealed class BodySource
    {
        public string path, guid, sha256;
        public List<BodySlot> slots = new List<BodySlot>();
    }
    [Serializable] public sealed class BodyAudit
    {
        public List<BodySource> sources = new List<BodySource>();
    }
    // Separate policy from choice meshes: scenes remain forbidden by MeshReferenceAudit.
    public static class BodyMeshReferenceAudit
    {
        static readonly Regex GuidPattern = new Regex(@"\bguid:\s*([a-fA-F0-9]{32})\b");
        public static string Route(Transform transform)
        {
            return (transform.parent ? Route(transform.parent) + "/" : "") + transform.GetSiblingIndex();
        }
        static string Guid(UnityEngine.Object obj)
        {
            string guid; long id;
            if (!obj || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out guid, out id)) throw new InvalidOperationException("Unresolved body identity");
            return guid;
        }
        public static List<BodySlot> Inspect(IEnumerable<GameObject> roots, IList<MeshRecord> records, int slotsPerOwner)
        {
            var byGuid = records.ToDictionary(r => r.guid, StringComparer.Ordinal);
            var result = new List<BodySlot>(); var ownerObjects = new Dictionary<string, Component>(StringComparer.Ordinal);
            foreach (var root in roots)
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            foreach (var component in transform.GetComponents<Component>())
            {
                if (!component) throw new InvalidOperationException("Missing script in body hierarchy");
                var property = new SerializedObject(component).GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || !(property.objectReferenceValue is Mesh)) continue;
                    string guid = Guid(property.objectReferenceValue);
                    if (!byGuid.ContainsKey(guid)) continue;
                    var skin = component as SkinnedMeshRenderer;
                    if (!skin || property.propertyPath != "m_Mesh" || skin.sharedMesh != byGuid[guid].mesh)
                        throw new InvalidOperationException("Unapproved body consumer " + component.name + "." + property.propertyPath);
                    var owners = new List<Component>();
                    for (var parent = skin.transform; parent; parent = parent.parent)
                        owners.AddRange(parent.GetComponents<Component>().Where(c => c && c.GetType().FullName == "CC.CharacterCustomization"));
                    if (owners.Count != 1) throw new InvalidOperationException("Body must have exactly one original customizer owner");
                    var owner = owners[0];
                    var mainProperty = new SerializedObject(owner).FindProperty("MainMesh");
                    var main = mainProperty == null ? null : mainProperty.objectReferenceValue as SkinnedMeshRenderer;
                    if (!main || !main.sharedMesh || !byGuid.ContainsKey(Guid(main.sharedMesh)) || !main.transform.IsChildOf(owner.transform))
                        throw new InvalidOperationException("Original MainMesh no longer identifies this body's renderer");
                    string route = Route(owner.transform); ownerObjects[route] = owner;
                    result.Add(new BodySlot { ownerPath = route, rendererPath = Route(skin.transform), meshGuid = guid,
                        ownerId = GlobalObjectId.GetGlobalObjectIdSlow(owner).ToString(), rendererId = GlobalObjectId.GetGlobalObjectIdSlow(skin).ToString() });
                }
            }
            foreach (var group in result.GroupBy(s => s.ownerPath))
            {
                if (group.Count() != slotsPerOwner || group.Select(s => s.meshGuid).Distinct().Count() != slotsPerOwner ||
                    group.Select(s => s.rendererPath).Distinct().Count() != slotsPerOwner)
                    throw new InvalidOperationException("Body LOD/renderer ownership changed: " + group.Key);
                var main = new SerializedObject(ownerObjects[group.Key]).FindProperty("MainMesh").objectReferenceValue as SkinnedMeshRenderer;
                if (!group.Any(s => s.rendererPath == Route(main.transform))) throw new InvalidOperationException("MainMesh outside audited body slots");
            }
            return result.OrderBy(s => s.ownerPath, StringComparer.Ordinal).ThenBy(s => s.rendererPath, StringComparer.Ordinal).ToList();
        }
        public static BodyAudit Scan(IList<MeshRecord> records, IEnumerable<string> prefabPaths, string scenePath, int slotsPerOwner, IEnumerable<string> paths = null)
        {
            var audit = new BodyAudit(); var byGuid = records.ToDictionary(r => r.guid, StringComparer.Ordinal);
            if (records.Count == 0 || records.Any(r => !r.mesh || AssetDatabase.GetAssetPath(r.mesh) != r.path || Guid(r.mesh) != r.guid || !AssetDatabase.IsMainAsset(r.mesh)))
                throw new InvalidOperationException("Body mesh identity unavailable");
            var approved = new HashSet<string>(prefabPaths, StringComparer.Ordinal); approved.Add(scenePath);
            foreach (string path in (paths ?? AssetDatabase.GetAllAssetPaths()).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !File.Exists(path) || path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                var raw = new Dictionary<string, int>(StringComparer.Ordinal); bool yaml;
                using (var reader = File.OpenText(path))
                {
                    string line = reader.ReadLine(); yaml = line != null && line.StartsWith("%YAML", StringComparison.Ordinal);
                    if (yaml) while ((line = reader.ReadLine()) != null) foreach (Match match in GuidPattern.Matches(line))
                    {
                        string guid = match.Groups[1].Value.ToLowerInvariant(); if (!byGuid.ContainsKey(guid)) continue;
                        int count; raw.TryGetValue(guid, out count); raw[guid] = count + 1;
                    }
                }
                var direct = AssetDatabase.GetDependencies(path, false).Where(p => p != path).Select(AssetDatabase.AssetPathToGUID).Where(byGuid.ContainsKey).Distinct().ToArray();
                if (raw.Count == 0 && direct.Length == 0) continue;
                if (!yaml || !approved.Contains(path) || (path != scenePath && !path.EndsWith(".prefab", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Unapproved incoming body reference: " + path);
                var source = new BodySource { path = path, guid = AssetDatabase.AssetPathToGUID(path), sha256 = BodyMeshBuild.Hash(path) };
                if (path == scenePath)
                {
                    var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    try { source.slots = Inspect(scene.GetRootGameObjects(), records, slotsPerOwner); }
                    finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
                }
                else source.slots = Inspect(new[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) }, records, slotsPerOwner);
                var observed = source.slots.GroupBy(s => s.meshGuid).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                if (raw.Count != observed.Count || raw.Any(p => !observed.ContainsKey(p.Key) || observed[p.Key] != p.Value) || direct.Any(g => !observed.ContainsKey(g)))
                    throw new InvalidOperationException("Unexplained body serialization occurrences: " + path);
                if (HashChanged(source)) throw new InvalidOperationException("Body audit modified source: " + path);
                audit.sources.Add(source);
            }
            if (!approved.SetEquals(audit.sources.Select(s => s.path))) throw new InvalidOperationException("Expected body source missing from audit");
            return audit;
        }
        static bool HashChanged(BodySource source) { return BodyMeshBuild.Hash(source.path) != source.sha256; }
    }
}
