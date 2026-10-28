using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TCG.SwitchStreaming;
using TCG.SwitchStreaming.Editor;

[Serializable] public sealed class MeshChoiceFixture { public GameObject Mesh; }
public sealed class MeshTableFixture : ScriptableObject { public List<MeshChoiceFixture> Hairstyles = new List<MeshChoiceFixture>(); }
public static class CharacterBuildTests
{
    public static void Run()
    {
        const string folder = "Assets/CharacterBuildFixtures";
        AssetDatabase.DeleteAsset(folder); AssetDatabase.CreateFolder("Assets", "CharacterBuildFixtures");
        var mesh = new Mesh { name = "synthetic-character-part", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        mesh.AddBlendShapeFrame("expression", 100, new[] { Vector3.forward, Vector3.zero, Vector3.zero }, null, null);
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } };
        AssetDatabase.CreateAsset(mesh, folder + "/source.asset");
        var usage = new SerializedObject(mesh); usage.FindProperty("m_MeshUsageFlags").intValue = 1; usage.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
        var root = new GameObject("part"); var skin = root.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh; skin.rootBone = root.transform; skin.bones = new[] { root.transform };
        skin.localBounds = new Bounds(Vector3.one, Vector3.one * 3); skin.SetBlendShapeWeight(0, 23);
        var child = new GameObject("static"); child.transform.SetParent(root.transform); child.AddComponent<MeshFilter>().sharedMesh = mesh;
        string path = folder + "/part.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); UnityEngine.Object.DestroyImmediate(root);
        var table = ScriptableObject.CreateInstance<MeshTableFixture>();
        table.Hairstyles.Add(new MeshChoiceFixture { Mesh = prefab }); table.Hairstyles.Add(new MeshChoiceFixture()); table.Hairstyles.Add(new MeshChoiceFixture { Mesh = prefab });
        var parts = CharacterMeshBuild.CollectPartPrefabs(new UnityEngine.Object[] { table });
        if (parts.Count != 1 || parts[0] != path) throw new Exception("CC part metadata/null slots changed or duplicate prefab");
        var records = CharacterMeshBuild.CollectMeshes(parts);
        if (records.Count != 1 || records[0].mesh != mesh || records[0].bytes <= 0 || records[0].usageFlags != 1) throw new Exception("Shared original mesh not deduplicated");
        usage = new SerializedObject(mesh); usage.FindProperty("m_MeshUsageFlags").longValue = Int32.MinValue + 5L; usage.ApplyModifiedPropertiesWithoutUndo();
        if (CharacterMeshBuild.CollectMeshes(parts)[0].usageFlags != Int32.MinValue + 5L) throw new Exception("Unknown signed usage bit pattern changed");
        usage = new SerializedObject(mesh); usage.FindProperty("m_MeshUsageFlags").longValue = 1; usage.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
        CharacterMeshBuild.RewritePrefab(path, records);
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var owner = prefab.GetComponent<MeshBundleOwner>();
        if (!owner || owner.Bindings.Length != 2 || prefab.GetComponent<SkinnedMeshRenderer>().sharedMesh || prefab.GetComponentInChildren<MeshFilter>().sharedMesh) throw new Exception("Mesh references not replaced by owned string bindings");
        var binding = Array.Find(owner.Bindings, value => value.skinned);
        if (binding == null || binding.key != records[0].key || binding.defaultWeights.Length != 1 || binding.defaultWeights[0] != 23 || binding.defaultBounds != new Bounds(Vector3.one, Vector3.one * 3)) throw new Exception("Authored mesh defaults changed");
        if (table.Hairstyles.Count != 3 || table.Hairstyles[1].Mesh) throw new Exception("Choice list identity changed");
        string output = Path.GetFullPath("character-flag-fixture"); Directory.CreateDirectory(output);
        string bundleName = "mesh_" + records[0].guid;
        var built = BuildPipeline.BuildAssetBundles(output, new[] { new AssetBundleBuild { assetBundleName = bundleName,
            assetNames = new[] { records[0].path }, addressableNames = new[] { "assets/tcgstreamedmesh/" + records[0].guid + ".asset" } } },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneOSX);
        if (!built) throw new Exception("Synthetic mesh bundle failed");
        string bundlePath = Path.Combine(output, bundleName);
        var originalBundle = AssetBundle.LoadFromFile(bundlePath);
        if (!originalBundle || new SerializedObject(originalBundle.LoadAllAssets<Mesh>()[0]).FindProperty("m_MeshUsageFlags").longValue != 0) throw new Exception("Fixture did not reproduce bundle flag reset");
        originalBundle.Unload(true);
        var preservedCrcs = CharacterMeshBuild.PreserveUsageFlags(output, records, Path.GetFullPath("character-flag-request.json"));
        File.Delete(bundlePath + ".manifest");
        uint crc;
        if (!preservedCrcs.TryGetValue(bundlePath, out crc) || crc == 0) throw new Exception("Missing verified post-restoration bundle CRC");
        var preservedBundle = AssetBundle.LoadFromFile(bundlePath, crc);
        if (!preservedBundle || new SerializedObject(preservedBundle.LoadAllAssets<Mesh>()[0]).FindProperty("m_MeshUsageFlags").longValue != 1) throw new Exception("Exact source flag was not preserved through builder helper/native CRC load");
        preservedBundle.Unload(true);
        Debug.Log("CHARACTER_BUILD_PASS catalogue, dedup, binding, weights/bounds, unchanged slots");
        EditorApplication.Exit(0);
    }
}
