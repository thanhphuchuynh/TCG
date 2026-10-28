using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TCG.SwitchStreaming;

public static class StreamingTests
{
    public static void Run()
    {
        PlayerSettings.runInBackground = true;
        const string folder = "Assets/StreamingFixtures";
        Directory.CreateDirectory(folder);
        var red = Texture(folder + "/red.asset", Color.red);
        var blue = Texture(folder + "/blue.asset", Color.blue);
        // Synthetic fixtures only. Production artwork must use authored Sprite assets.
        var a = Sprite.Create(red, new Rect(2, 2, 4, 4), new Vector2(.25f, .75f), 32, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
        AssetDatabase.CreateAsset(a, folder + "/red-sprite.asset"); a.name = "red-art"; EditorUtility.SetDirty(a);
        var b = Sprite.Create(blue, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f), 32);
        AssetDatabase.CreateAsset(b, folder + "/blue-sprite.asset"); b.name = "blue-art"; EditorUtility.SetDirty(b);
        AssetDatabase.SaveAssets();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../fixture-bundles"));
        Directory.CreateDirectory(output);
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] {
            new AssetBundleBuild { assetBundleName = "art_red", assetNames = new[] { folder + "/red-sprite.asset" } },
            new AssetBundleBuild { assetBundleName = "art_blue", assetNames = new[] { folder + "/blue-sprite.asset" } }
        }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneOSX);
        if (!manifest) throw new Exception("Fixture bundle build failed");
        uint redCrc, blueCrc;
        if (!BuildPipeline.GetCRCForAssetBundle(output + "/art_red", out redCrc) || !BuildPipeline.GetCRCForAssetBundle(output + "/art_blue", out blueCrc)) throw new Exception("Missing fixture CRC");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Actual bundle ownership tests").AddComponent<StreamingProbe>();
        probe.BundleRoot = output; probe.RedCrc = redCrc; probe.BlueCrc = blueCrc; probe.LoadingSprite = a;
        EditorSceneManager.SaveScene(scene, folder + "/Tests.unity");
        EditorApplication.isPlaying = true;
    }
    static Texture2D Texture(string path, Color color)
    {
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var pixels = new Color[64]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels); texture.Apply(); AssetDatabase.CreateAsset(texture, path); return texture;
    }
}
