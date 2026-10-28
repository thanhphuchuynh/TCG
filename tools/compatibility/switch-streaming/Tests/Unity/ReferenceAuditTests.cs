using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TCG.SwitchStreaming.Editor;

public sealed class ReferenceAuditTests : ScriptableObject
{
    public Sprite ArtReference, ExtraReference;
    public ReferenceAuditTests CatalogueReference;
    const string Root = "Assets/ReferenceAuditFixtures";
    static int passed;
    public static void Run()
    {
        EditorSettings.serializationMode = SerializationMode.ForceText;
        AssetDatabase.DeleteAsset(Root); Directory.CreateDirectory(Root); AssetDatabase.Refresh();
        int failures = 0;
        foreach (Action scenario in new Action[] { KnownImagesAreReplacedInSavedPrefabAndScene, UnlistedCatalogueReferenceRetainsWholeTexture,
            OrdinaryImageRetainsWholeTexture, DirectMaterialTextureRetainsWholeTexture, AlternateSpriteRetainsSharedTexture, OtherCardUIFieldIsNeverCleared, BinarySerializedReferenceRetainsTexture, AliasIsFoundWithConsumerOnlyPaths, CatalogueSubassetCannotUseMainPropertyExemption, ResourcesTextureIsRetained, ResourcesOriginalSpriteIsRetained, ResourcesAliasSpriteIsRetained, BinaryWrapperDoesNotRetainAuditedCatalogueDependencies })
        {
            try { scenario(); } catch (Exception error) { failures++; Debug.LogError(error); }
        }
        if (failures != 0) throw new Exception("REFERENCE_AUDIT_FAILED_SCENARIOS " + failures);
        Debug.Log("REFERENCE_AUDIT_PASS " + passed + " scenarios");
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception("REFERENCE_AUDIT_FAIL: " + message); }
    static Sprite Art(string name, Texture2D shared = null)
    {
        var texture = shared;
        if (!texture) { texture = new Texture2D(8, 8, TextureFormat.RGBA32, false); AssetDatabase.CreateAsset(texture, Root + "/" + name + "-texture.asset"); }
        var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * .5f, 32);
        AssetDatabase.CreateAsset(sprite, Root + "/" + name + ".asset"); AssetDatabase.SaveAssets(); return sprite;
    }
    static List<ArtRecord> Catalogue(string name, Sprite art, bool extra, out ReferenceAuditTests catalogue)
    {
        catalogue = ScriptableObject.CreateInstance<ReferenceAuditTests>();
        catalogue.ArtReference = art; if (extra) catalogue.ExtraReference = art;
        AssetDatabase.CreateAsset(catalogue, Root + "/" + name + "-catalogue.asset"); AssetDatabase.SaveAssets();
        string guid; long id; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(art, out guid, out id);
        string texturePath = AssetDatabase.GetAssetPath(art.texture);
        return new List<ArtRecord> { new ArtRecord { key = name, property = "ArtReference", sprite = art,
            spritePath = AssetDatabase.GetAssetPath(art), spriteGuid = guid, spriteId = id,
            texturePath = texturePath, textureGuid = AssetDatabase.AssetPathToGUID(texturePath) } };
    }
    static GameObject Card(Sprite art, bool marked)
    {
        var root = new GameObject("fixture");
        if (marked) root.AddComponent<TCG.SwitchStreaming.Tests.CardUI>();
        var image = new GameObject("art", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); image.transform.SetParent(root.transform); image.GetComponent<Image>().sprite = art;
        return root;
    }
    static string Prefab(string name, Sprite art, bool marked, bool overrideSprite = false)
    {
        string path = Root + "/" + name + ".prefab"; var root = Card(art, marked);
        if (overrideSprite) root.GetComponent<TCG.SwitchStreaming.Tests.CardUI>().ExtraReference = art;
        PrefabUtility.SaveAsPrefabAsset(root, path); UnityEngine.Object.DestroyImmediate(root); return path;
    }
    static IEnumerable<string> Paths() { return AssetDatabase.GetAllAssetPaths(); }
    static void KnownImagesAreReplacedInSavedPrefabAndScene()
    {
        var art = Art("known"); var placeholder = Art("placeholder"); ReferenceAuditTests catalogue;
        var records = Catalogue("known", art, false, out catalogue); string catPath = AssetDatabase.GetAssetPath(catalogue);
        string prefab = Prefab("known", art, true);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); Card(art, true);
        var resident = Card(placeholder, false); resident.name = "resident";
        string scenePath = Root + "/known.unity"; EditorSceneManager.SaveScene(scene, scenePath);
        var result = ReferenceAudit.Scan(catPath, records, Paths());
        Check(result.Eligible.Count == 1 && result.RetainedTextures.Count == 0, "known CardUI images must be eligible");
        Check(result.ImageFiles.Contains(prefab) && result.ImageFiles.Contains(scenePath), "both matching hierarchy files are reported");
        Check(ReferenceAudit.FindEagerReferences(records, Paths()).Contains(prefab), "post-strip check rejects images still pointing at art");
        catalogue = AssetDatabase.LoadAssetAtPath<ReferenceAuditTests>(catPath);
        StreamingBuild.StripCatalogue(catalogue, records); AssetDatabase.SaveAssets();
        ReferenceAudit.StripImages(result.ImageFiles, new HashSet<string> { records[0].textureGuid }, placeholder);
        var loaded = PrefabUtility.LoadPrefabContents(prefab);
        Check(loaded.GetComponentInChildren<Image>(true).sprite == placeholder, "saved prefab uses authored placeholder"); PrefabUtility.UnloadPrefabContents(loaded);
        scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects()) Check(root.GetComponentInChildren<Image>(true).sprite == placeholder, "scene replacement preserves resident placeholder");
        Check(ReferenceAudit.FindEagerReferences(records, Paths()).Count == 0, "no eager references remain after catalogue/image stripping"); passed++;
    }
    static void UnlistedCatalogueReferenceRetainsWholeTexture()
    {
        var art = Art("extra"); ReferenceAuditTests catalogue; var records = Catalogue("extra", art, true, out catalogue);
        var result = ReferenceAudit.Scan(AssetDatabase.GetAssetPath(catalogue), records, Paths());
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "unrecorded catalogue property retains texture"); passed++;
    }
    static void OrdinaryImageRetainsWholeTexture()
    {
        var art = Art("ordinary"); ReferenceAuditTests catalogue; var records = Catalogue("ordinary", art, false, out catalogue); string prefab = Prefab("ordinary", art, false);
        var result = ReferenceAudit.Scan(AssetDatabase.GetAssetPath(catalogue), records, Paths());
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "Image outside CardUI retains texture");
        ReferenceAudit.StripImages(new[] { prefab }, new HashSet<string> { records[0].textureGuid }, Art("ordinary-placeholder"));
        var loaded = PrefabUtility.LoadPrefabContents(prefab); Check(loaded.GetComponentInChildren<Image>().sprite == art, "ordinary UI cannot be silently cleared"); PrefabUtility.UnloadPrefabContents(loaded); passed++;
    }
    static void DirectMaterialTextureRetainsWholeTexture()
    {
        var art = Art("material"); ReferenceAuditTests catalogue; var records = Catalogue("material", art, false, out catalogue);
        var material = new Material(Shader.Find("UI/Default")); material.mainTexture = art.texture;
        string path = Root + "/direct.mat"; AssetDatabase.CreateAsset(material, path); AssetDatabase.SaveAssets();
        var result = ReferenceAudit.Scan(AssetDatabase.GetAssetPath(catalogue), records, Paths());
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "direct material Texture reference retains texture");
        Check(ReferenceAudit.FindEagerReferences(records, Paths()).Contains(path), "material appears in unexplained eager paths"); passed++;
    }
    static void AlternateSpriteRetainsSharedTexture()
    {
        var art = Art("shared"); var alias = Art("alias", art.texture); ReferenceAuditTests catalogue;
        var records = Catalogue("shared", art, false, out catalogue); string prefab = Prefab("alias", alias, false);
        var result = ReferenceAudit.Scan(AssetDatabase.GetAssetPath(catalogue), records, Paths());
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "different Sprite GUID sharing target texture retains whole texture");
        Check(ReferenceAudit.FindEagerReferences(records, Paths()).Contains(prefab), "alias user appears in eager paths"); passed++;
    }
    static void OtherCardUIFieldIsNeverCleared()
    {
        var art = Art("override"); ReferenceAuditTests catalogue; var records = Catalogue("override", art, false, out catalogue); string prefab = Prefab("override", art, true, true);
        var result = ReferenceAudit.Scan(AssetDatabase.GetAssetPath(catalogue), records, Paths());
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "additional serialized CardUI Sprite field is not the audited m_Sprite default"); passed++;
    }
    static void BinarySerializedReferenceRetainsTexture()
    {
        var art = Art("binary"); ReferenceAuditTests catalogue; var records = Catalogue("binary", art, false, out catalogue);
        string catPath = AssetDatabase.GetAssetPath(catalogue), artPath = AssetDatabase.GetAssetPath(art);
        string path = Root + "/binary-other.asset";
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceBinary;
            var other = ScriptableObject.CreateInstance<ReferenceAuditTests>(); other.ExtraReference = AssetDatabase.LoadAssetAtPath<Sprite>(artPath);
            AssetDatabase.CreateAsset(other, path); AssetDatabase.SaveAssets();
            Check(!File.ReadAllText(path).StartsWith("%YAML", StringComparison.Ordinal), "fixture really is binary during audit");
            var result = ReferenceAudit.Scan(catPath, records, new[] { path });
            Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "binary serialized references cannot silently pass raw-text prefilter"); passed++;
        }
        finally { EditorSettings.serializationMode = SerializationMode.ForceText; }
    }
    static void AliasIsFoundWithConsumerOnlyPaths()
    {
        var art = Art("subset"); var alias = Art("subset-alias", art.texture); ReferenceAuditTests catalogue;
        var records = Catalogue("subset", art, false, out catalogue); string prefab = Prefab("subset-alias", alias, false);
        var result = ReferenceAudit.Scan(AssetDatabase.GetAssetPath(catalogue), records, new[] { prefab });
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "consumer-only paths still discover alternate Sprite sources"); passed++;
    }

    static void CatalogueSubassetCannotUseMainPropertyExemption()
    {
        var art = Art("subasset"); ReferenceAuditTests catalogue; var records = Catalogue("subasset", art, false, out catalogue);
        string path = AssetDatabase.GetAssetPath(catalogue);
        var subasset = ScriptableObject.CreateInstance<ReferenceAuditTests>(); subasset.name = "unexplained subasset"; subasset.ArtReference = art;
        AssetDatabase.AddObjectToAsset(subasset, catalogue); AssetDatabase.SaveAssets();
        var result = ReferenceAudit.Scan(path, records, new[] { path });
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "another object cannot reuse main catalogue property exemption"); passed++;
    }

    static string MoveToResources(UnityEngine.Object asset)
    {
        string folder = Root + "/Resources"; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        string original = AssetDatabase.GetAssetPath(asset), destination = folder + "/" + Path.GetFileName(original);
        string error = AssetDatabase.MoveAsset(original, destination); Check(error == "", "Resources fixture move succeeds: " + error);
        AssetDatabase.SaveAssets(); return destination;
    }
    static void ResourcesTextureIsRetained()
    {
        var art = Art("resource-texture"); string resident = MoveToResources(art.texture); ReferenceAuditTests catalogue;
        var records = Catalogue("resource-texture", art, false, out catalogue); string path = AssetDatabase.GetAssetPath(catalogue);
        var result = ReferenceAudit.Scan(path, records, new[] { path });
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "texture in Resources stays eager without incoming consumers");
        Check(ReferenceAudit.FindEagerReferences(records, new[] { path }).Contains(resident), "implicit Resources texture appears in remaining eager paths"); passed++;
    }
    static void ResourcesOriginalSpriteIsRetained()
    {
        var art = Art("resource-original"); string resident = MoveToResources(art); ReferenceAuditTests catalogue;
        var records = Catalogue("resource-original", art, false, out catalogue); string path = AssetDatabase.GetAssetPath(catalogue);
        var result = ReferenceAudit.Scan(path, records, new[] { path });
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "original Sprite under Resources pins its texture");
        Check(ReferenceAudit.FindEagerReferences(records, new[] { path }).Contains(resident), "implicit Resources Sprite is reported"); passed++;
    }
    static void ResourcesAliasSpriteIsRetained()
    {
        var art = Art("resource-shared"); var alias = Art("resource-alias", art.texture); string resident = MoveToResources(alias); ReferenceAuditTests catalogue;
        var records = Catalogue("resource-shared", art, false, out catalogue); string path = AssetDatabase.GetAssetPath(catalogue);
        var result = ReferenceAudit.Scan(path, records, new[] { path });
        Check(result.Eligible.Count == 0 && result.RetainedTextures.ContainsKey(records[0].textureGuid), "alternate Sprite under Resources pins shared texture even with consumer-only paths");
        Check(ReferenceAudit.FindEagerReferences(records, new[] { path }).Contains(resident), "implicit Resources alias is reported"); passed++;
    }

    static void BinaryWrapperDoesNotRetainAuditedCatalogueDependencies()
    {
        var art = Art("binary-wrapper"); ReferenceAuditTests catalogue; var records = Catalogue("binary-wrapper", art, false, out catalogue);
        string catPath = AssetDatabase.GetAssetPath(catalogue), path = Root + "/binary-wrapper-other.asset";
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceBinary;
            var wrapper = ScriptableObject.CreateInstance<ReferenceAuditTests>(); wrapper.CatalogueReference = AssetDatabase.LoadAssetAtPath<ReferenceAuditTests>(catPath);
            AssetDatabase.CreateAsset(wrapper, path); AssetDatabase.SaveAssets();
            Check(!File.ReadAllText(path).StartsWith("%YAML", StringComparison.Ordinal), "wrapper fixture is binary during audit");
            var result = ReferenceAudit.Scan(catPath, records, new[] { path });
            Check(result.Eligible.Count == 1 && result.RetainedTextures.Count == 0, "binary wrapper cannot turn separately audited catalogue edges into unexplained direct art refs"); passed++;
        }
        finally { EditorSettings.serializationMode = SerializationMode.ForceText; }
    }

}
