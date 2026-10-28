using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Only synthetic authored assets are generated; never opens the commercial project.
public static class MeshRuntimeTests
{
    public static void Run()
    {
        PlayerSettings.runInBackground = true;
        const string folder = "Assets/MeshFixtures";
        Directory.CreateDirectory(folder);
        var mesh = new Mesh { name = "authored-rigged-blendshape" };
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } };
        mesh.AddBlendShapeFrame("smile", 100, new[] { Vector3.forward, Vector3.forward, Vector3.forward }, new Vector3[3], new Vector3[3]);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, folder + "/rigged.asset");
        var second = UnityEngine.Object.Instantiate(mesh); second.name = "authored-second-choice";
        AssetDatabase.CreateAsset(second, folder + "/second.asset");
        var badMesh = UnityEngine.Object.Instantiate(mesh); AssetDatabase.CreateAsset(badMesh, folder + "/bad-rigged.asset");
        var texture = new Texture2D(2, 2); AssetDatabase.AddObjectToAsset(texture, badMesh);
        AssetDatabase.SaveAssets();
        string root = Path.GetFullPath("fixture-bundles"); Directory.CreateDirectory(root);
        var built = BuildPipeline.BuildAssetBundles(root, new[] {
            new AssetBundleBuild { assetBundleName = "mesh_rigged", assetNames = new[] { folder + "/rigged.asset" }, addressableNames = new[] { "mesh:11111111111111111111111111111111" } },
            new AssetBundleBuild { assetBundleName = "mesh_second", assetNames = new[] { folder + "/second.asset" }, addressableNames = new[] { "mesh:22222222222222222222222222222222" } },
            new AssetBundleBuild { assetBundleName = "mesh_badpayload", assetNames = new[] { folder + "/bad-rigged.asset" }, addressableNames = new[] { "mesh:11111111111111111111111111111111" } }
        }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneOSX);
        if (!built) throw new Exception("Synthetic mesh bundle build failed");
        uint crc, secondCrc, badCrc;
        BuildPipeline.GetCRCForAssetBundle(root + "/mesh_rigged", out crc);
        BuildPipeline.GetCRCForAssetBundle(root + "/mesh_second", out secondCrc);
        BuildPipeline.GetCRCForAssetBundle(root + "/mesh_badpayload", out badCrc);
        File.Copy(root + "/mesh_rigged", root + "/mesh_wrongcrc", true);
        Directory.CreateDirectory("Assets/Resources/TCGStreaming");
        var canonical = new TCG.SwitchStreaming.MeshManifest();
        canonical.entries.Add(new TCG.SwitchStreaming.MeshEntry { key = "mesh:11111111111111111111111111111111", bundle = "mesh_rigged", asset = "mesh:11111111111111111111111111111111", bytes = 1024, crc = crc });
        File.WriteAllText("Assets/Resources/TCGStreaming/MeshCatalog.txt", JsonUtility.ToJson(canonical));
        string localRoot = Path.Combine(Application.streamingAssetsPath, "TCGStreamedMeshes");
        if (Directory.Exists(localRoot)) Directory.Delete(localRoot, true);
        AssetDatabase.Refresh();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Synthetic mesh runtime tests").AddComponent<MeshRuntimeProbe>();
        probe.BundleRoot = root; probe.Crc = crc; probe.SecondCrc = secondCrc; probe.BadPayloadCrc = badCrc;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), folder + "/Tests.unity");
        EditorApplication.isPlaying = true;
    }
}
