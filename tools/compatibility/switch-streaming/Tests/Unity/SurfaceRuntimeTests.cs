using System;
using System.IO;
using TCG.SwitchStreaming;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Own synthetic assets only; never opens or copies the commercial project.
public static class SurfaceRuntimeTests
{
    public static void Run()
    {
        PlayerSettings.runInBackground = true;
        const string folder = "Assets/SurfaceFixtures";
        Directory.CreateDirectory(folder);
        var numbers = new[] { 1, 2, 3, 4, 6 };
        foreach (int i in numbers)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, true) { name = "authored-surface-" + i, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 3 };
            var pixels = new Color32[64]; for (int p = 0; p < pixels.Length; p++) pixels[p] = new Color32((byte)(i * 20), (byte)p, 17, 255);
            texture.SetPixels32(pixels); texture.Apply(true, false);
            AssetDatabase.CreateAsset(texture, folder + "/" + i + ".asset");
            if (i == 3) AssetDatabase.AddObjectToAsset(new Texture2D(2, 2), texture);
            if (i == 6) AssetDatabase.AddObjectToAsset(new Material(Shader.Find("Standard")) { name = "forbidden-material-subasset" }, texture);
        }
        AssetDatabase.SaveAssets();
        string root = Path.GetFullPath("fixture-bundles"); Directory.CreateDirectory(root);
        var builds = new AssetBundleBuild[numbers.Length];
        for (int n = 0; n < numbers.Length; n++) { int i = numbers[n]; builds[n] = new AssetBundleBuild { assetBundleName = "surface_" + GuidFor(i), assetNames = new[] { folder + "/" + i + ".asset" }, addressableNames = new[] { "assets/tcgstreamedsurface/" + GuidFor(i) + ".asset" } }; }
        var built = BuildPipeline.BuildAssetBundles(root, builds, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneOSX);
        if (!built) throw new Exception("Synthetic surface bundle build failed");
        var crc = new uint[6]; foreach (int i in numbers) if (!BuildPipeline.GetCRCForAssetBundle(root + "/surface_" + GuidFor(i), out crc[i - 1])) throw new Exception("Missing CRC");
        var canonical = new SurfaceManifest(); canonical.entries.Add(Entry(1, crc[0], "surface:floor:0:mainTexture"));
        Directory.CreateDirectory("Assets/Resources/TCGStreaming");
        File.WriteAllText("Assets/Resources/TCGStreaming/SurfaceCatalog.txt", JsonUtility.ToJson(canonical));
        string local = Path.Combine(Application.streamingAssetsPath, "TCGStreamedSurfaces"); if (Directory.Exists(local)) Directory.Delete(local, true);
        AssetDatabase.Refresh();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Synthetic surface runtime tests").AddComponent<SurfaceRuntimeProbe>();
        probe.BundleRoot = root; probe.Crc = crc;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), folder + "/Tests.unity");
        EditorApplication.isPlaying = true;
    }
    public static string GuidFor(int number) { return number.ToString().PadLeft(32, '0'); }
    public static SurfaceEntry Entry(int number, uint crc, string key)
    { string guid = GuidFor(number); return new SurfaceEntry { key = key, bundle = "surface_" + guid, asset = "assets/tcgstreamedsurface/" + guid + ".asset", bytes = 340, crc = crc }; }
}
