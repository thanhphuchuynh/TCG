using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TCG.SwitchStreaming;
using TCG.SwitchStreaming.Editor;
using System.Collections.Generic;

[Serializable]
public sealed class StreamingFixtureMonster
{
    public int MonsterType;
    public Sprite Icon, GhostIcon;
    public List<Sprite> IconList = new List<Sprite>();
    public List<Sprite> BGList = new List<Sprite>();
}
public sealed class StreamingFixtureCatalogue : ScriptableObject
{
    public List<StreamingFixtureMonster> m_DataList = new List<StreamingFixtureMonster>();
    public List<Sprite> m_TetramonImageList = new List<Sprite>();
    public List<Sprite> m_CardBackImageList = new List<Sprite>();
}

public static class StreamingTests
{
    public static void Run()
    {
        PlayerSettings.runInBackground = true;
        Directory.CreateDirectory("Assets/Resources/TCGStreaming");
        File.WriteAllText("Assets/Resources/TCGStreaming/Catalog.txt", "{}");
        AssetDatabase.Refresh();
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
        var catalogue = ScriptableObject.CreateInstance<StreamingFixtureCatalogue>();
        catalogue.m_DataList.Add(new StreamingFixtureMonster { MonsterType = 7, Icon = a, IconList = new List<Sprite> { null, b }, BGList = new List<Sprite> { null, a } });
        catalogue.m_TetramonImageList.Add(b); catalogue.m_CardBackImageList.Add(a);
        var records = StreamingBuild.CollectRecords(catalogue);
        if (records.Count != 4 || !records.Exists(r => r.key == "monster:7:icon:0") || !records.Exists(r => r.key == "monster:7:variant-icon:1") || !records.Exists(r => r.key == "monster:7:variant-bg:1") || !records.Exists(r => r.key == "tetramon:0")) throw new Exception("Catalogue semantic extraction failed");
        var planned = StreamingBuild.CreateBundlePlan(records);
        if (planned.Length != 2) throw new Exception("Artwork plan must group by underlying texture");
        foreach (var bundle in planned)
            if (bundle.assetNames.Length != 1 || !bundle.assetBundleName.StartsWith("art_")) throw new Exception("Duplicate source sprite in bundle plan");
        foreach (var record in records) record.sprite = null; // BuildAssetBundles may unload editor object instances.
        StreamingBuild.StripCatalogue(catalogue, records);
        if (catalogue.m_DataList[0].Icon || catalogue.m_DataList[0].IconList[1] || catalogue.m_DataList[0].BGList[1] || catalogue.m_TetramonImageList[0] || catalogue.m_DataList[0].IconList.Count != 2 || catalogue.m_DataList[0].BGList.Count != 2 || catalogue.m_CardBackImageList[0] != a) throw new Exception("Catalogue stripping changed ordering/null semantics or resident frames");
        UnityEngine.Object.DestroyImmediate(catalogue);
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../fixture-bundles"));
        Directory.CreateDirectory(output);
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] {
            new AssetBundleBuild { assetBundleName = "art_red", assetNames = new[] { folder + "/red-sprite.asset" } },
            new AssetBundleBuild { assetBundleName = "art_blue", assetNames = new[] { folder + "/blue-sprite.asset" } }
        }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneOSX);
        if (!manifest) throw new Exception("Fixture bundle build failed");
        uint redCrc, blueCrc;
        if (!BuildPipeline.GetCRCForAssetBundle(output + "/art_red", out redCrc) || !BuildPipeline.GetCRCForAssetBundle(output + "/art_blue", out blueCrc)) throw new Exception("Missing fixture CRC");
        File.Copy(output + "/art_blue", output + "/art_wrongsize", true);
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
