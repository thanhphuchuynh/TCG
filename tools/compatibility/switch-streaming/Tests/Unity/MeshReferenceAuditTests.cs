using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TCG.SwitchStreaming.Editor;

namespace TCG.SwitchStreaming.Tests
{
    public sealed class MeshReferenceAuditTests : ScriptableObject
    {
        public Mesh DirectMesh;
        public UnityEngine.Object Other;
        const string Root = "Assets/MeshAuditFixtures";
        static int passed;
        public static void Run()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            AssetDatabase.DeleteAsset(Root); Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            int failures = 0;
            foreach (Action scenario in new Action[] { ApprovedSharedRendererMeshIsEligible, UnapprovedPrefabRetains,
                ApprovedColliderIsNotAnAllowedField, SceneRendererRetains, ScriptableObjectRetains,
                UnresolvedGuidRetains, MaterialGuidRetains, BinaryDirectReferenceRetains, BinaryWrapperUsesDirectEdges,
                ResourcesMeshItselfCanMove, ResourcesConsumerRetains, NullAndNonMeshFieldsAreUntouched,
                NoPathOrTypeBlanketExemption, NonMeshGuidReferenceRetains, InvalidRecordRetains })
            {
                try { scenario(); Debug.Log("MESH_AUDIT_SCENARIO_PASS " + scenario.Method.Name); passed++; }
                catch (Exception error) { failures++; Debug.LogError(error); }
                finally { EditorSettings.serializationMode = SerializationMode.ForceText; }
            }
            if (failures != 0) throw new Exception("MESH_AUDIT_FAILED_SCENARIOS " + failures);
            Debug.Log("MESH_AUDIT_PASS " + passed + " scenarios");
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception("MESH_AUDIT_FAIL: " + message); }
        static MeshRecord MeshAsset(string name, bool resources = false)
        {
            string folder = Root + (resources ? "/Resources" : ""); Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var mesh = new Mesh { name = name, vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            string path = folder + "/" + name + ".asset"; AssetDatabase.CreateAsset(mesh, path); AssetDatabase.SaveAssets();
            string guid = AssetDatabase.AssetPathToGUID(path);
            return new MeshRecord { key = "mesh:" + guid, guid = guid, path = path, name = name, bytes = new FileInfo(path).Length, mesh = mesh };
        }
        static string Prefab(string name, Mesh mesh, bool collider = false, string folder = null)
        {
            var root = new GameObject(name); root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var child = new GameObject("skinned"); child.transform.SetParent(root.transform); child.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            if (collider) root.AddComponent<MeshCollider>().sharedMesh = mesh;
            string dir = folder ?? Root; Directory.CreateDirectory(dir); AssetDatabase.Refresh(); string path = dir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path); UnityEngine.Object.DestroyImmediate(root); return path;
        }
        static MeshReferenceAuditTests Reference(string name, UnityEngine.Object value, bool binary = false, string folder = null)
        {
            string dir = folder ?? Root; Directory.CreateDirectory(dir); AssetDatabase.Refresh();
            if (binary) EditorSettings.serializationMode = SerializationMode.ForceBinary;
            var other = CreateInstance<MeshReferenceAuditTests>(); other.Other = value;
            string path = dir + "/" + name + ".asset";
            AssetDatabase.CreateAsset(other, path); AssetDatabase.SaveAssets();
            return other;
        }
        static MeshAuditResult Scan(MeshRecord record, string approved, params string[] paths)
        {
            return MeshReferenceAudit.Scan(new[] { record }, new HashSet<string>(StringComparer.Ordinal) { approved }, paths);
        }
        static void Retained(MeshAuditResult result, MeshRecord record, string message)
        { Check(result.Eligible.Count == 0 && result.Retained.ContainsKey(record.guid), message); }
        static void ApprovedSharedRendererMeshIsEligible()
        {
            var mesh = MeshAsset("shared"); string prefab = Prefab("shared", mesh.mesh);
            string before = File.ReadAllText(prefab); var result = Scan(mesh, prefab, prefab, mesh.path);
            Check(result.Eligible.Count == 1 && result.Retained.Count == 0, "shared MeshFilter and skinned references in approved prefab are eligible once");
            Check(result.PrefabFiles.Count == 1 && result.PrefabFiles[0] == prefab, "one exact prefab reported");
            Check(File.ReadAllText(prefab) == before, "scan never edits prefab");
        }
        static void UnapprovedPrefabRetains()
        {
            var mesh = MeshAsset("unknown-prefab"); string prefab = Prefab("unknown-prefab", mesh.mesh);
            Retained(Scan(mesh, Root + "/unrelated.prefab", prefab), mesh, "unapproved prefab renderer retains mesh");
        }
        static void ApprovedColliderIsNotAnAllowedField()
        {
            var mesh = MeshAsset("collider"); string prefab = Prefab("collider", mesh.mesh, true);
            string before = File.ReadAllText(prefab); Retained(Scan(mesh, prefab, prefab), mesh, "MeshCollider m_Mesh is not approved renderer property");
            Check(File.ReadAllText(prefab) == before, "unknown collider reference is never cleared");
        }
        static void SceneRendererRetains()
        {
            var mesh = MeshAsset("scene"); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("unknown scene mesh").AddComponent<MeshFilter>().sharedMesh = mesh.mesh;
            string path = Root + "/scene.unity"; EditorSceneManager.SaveScene(scene, path);
            Retained(Scan(mesh, path, path), mesh, "even approved scene path cannot authorize renderer refs");
        }
        static void ScriptableObjectRetains()
        {
            var mesh = MeshAsset("scriptable"); var other = Reference("scriptable-other", mesh.mesh);
            Retained(Scan(mesh, AssetDatabase.GetAssetPath(other), AssetDatabase.GetAssetPath(other)), mesh, "arbitrary ScriptableObject mesh ref retains");
        }
        static void UnresolvedGuidRetains()
        {
            var mesh = MeshAsset("unresolved"); string path = Prefab("unresolved", mesh.mesh);
            string text = File.ReadAllText(path); string token = "guid: " + mesh.guid;
            int position = text.IndexOf(token, StringComparison.Ordinal); int start = text.LastIndexOf("fileID:", position, StringComparison.Ordinal); int end = text.IndexOf(',', start);
            text = text.Substring(0, start) + "fileID: 999999999" + text.Substring(end); File.WriteAllText(path, text); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Retained(Scan(mesh, path, path), mesh, "unresolved raw GUID may not pass as approved reference");
        }
        static void MaterialGuidRetains()
        {
            var mesh = MeshAsset("material"); var material = new Material(Shader.Find("Unlit/Color")); string path = Root + "/mesh-direct.mat";
            AssetDatabase.CreateAsset(material, path); AssetDatabase.SaveAssets();
            File.AppendAllText(path, "\n  unknownMesh: {fileID: 4300000, guid: " + mesh.guid + ", type: 2}\n"); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Retained(Scan(mesh, "", path), mesh, "unexplained direct material GUID retains even if importer drops field");
        }
        static void BinaryDirectReferenceRetains()
        {
            var mesh = MeshAsset("binary"); var other = Reference("binary-other", mesh.mesh, true); string path = AssetDatabase.GetAssetPath(other);
            Check(!File.ReadAllText(path).StartsWith("%YAML", StringComparison.Ordinal), "binary fixture is binary");
            Retained(Scan(mesh, "", path), mesh, "binary direct Mesh edge retains");
        }
        static void BinaryWrapperUsesDirectEdges()
        {
            var mesh = MeshAsset("wrapper"); string prefab = Prefab("wrapper", mesh.mesh);
            string textPrefab = File.ReadAllText(prefab);
            var wrapper = Reference("wrapper-other", AssetDatabase.LoadAssetAtPath<GameObject>(prefab), true); string path = AssetDatabase.GetAssetPath(wrapper);
            // ForceBinary converts existing fixtures too. Restore only the approved prefab's
            // identical text representation; its imported identity/references stay unchanged.
            File.WriteAllText(prefab, textPrefab);
            Check(File.ReadAllText(prefab).StartsWith("%YAML", StringComparison.Ordinal), "approved consumer stays text");
            Check(!File.ReadAllText(path).StartsWith("%YAML", StringComparison.Ordinal), "wrapper fixture binary");
            var result = Scan(mesh, prefab, path, prefab);
            Check(result.Eligible.Count == 1 && result.Retained.Count == 0, "binary wrapper to approved prefab does not invent transitive direct Mesh edge");
        }
        static void ResourcesMeshItselfCanMove()
        {
            var mesh = MeshAsset("resources-mesh", true); string prefab = Prefab("resource-approved", mesh.mesh);
            var result = Scan(mesh, prefab, mesh.path, prefab);
            Check(result.Eligible.Count == 1, "exact source Mesh in Resources is a planned GUID move target");
        }
        static void ResourcesConsumerRetains()
        {
            var mesh = MeshAsset("resources-consumer"); var other = Reference("resident-consumer", mesh.mesh, false, Root + "/Resources");
            Retained(Scan(mesh, "", AssetDatabase.GetAssetPath(other)), mesh, "Resources consumer is still audited");
        }
        static void NullAndNonMeshFieldsAreUntouched()
        {
            var mesh = MeshAsset("null"); var other = Reference("null-other", null);
            var material = new Material(Shader.Find("Unlit/Color")); AssetDatabase.CreateAsset(material, Root + "/nonmesh.mat"); other.Other = material; EditorUtility.SetDirty(other); AssetDatabase.SaveAssets();
            string path = AssetDatabase.GetAssetPath(other), before = File.ReadAllText(path);
            var result = Scan(mesh, "", path);
            Check(result.Eligible.Count == 1 && result.Retained.Count == 0 && File.ReadAllText(path) == before && other.DirectMesh == null && other.Other == material, "null/non-Mesh refs unaffected");
        }
        static void NoPathOrTypeBlanketExemption()
        {
            var mesh = MeshAsset("mesh-folder"); var other = Reference("mesh-other", mesh.mesh, false, Root + "/Meshes");
            Retained(Scan(mesh, "", AssetDatabase.GetAssetPath(other)), mesh, "consumer under Meshes folder is not ignored");
        }
        static void NonMeshGuidReferenceRetains()
        {
            var mesh = MeshAsset("nonmesh-subasset"); var sub = CreateInstance<MeshReferenceAuditTests>(); sub.name = "nonMesh"; AssetDatabase.AddObjectToAsset(sub, mesh.mesh); AssetDatabase.SaveAssets();
            var other = Reference("nonmesh-subasset-other", sub);
            Retained(Scan(mesh, "", AssetDatabase.GetAssetPath(other)), mesh, "unknown subasset reference sharing candidate Mesh GUID retains conservatively");
        }
        static void InvalidRecordRetains()
        {
            var mesh = MeshAsset("invalid"); mesh.mesh = null;
            Retained(Scan(mesh, "", mesh.path), mesh, "missing live candidate Mesh cannot be eligible");
        }
    }
}
