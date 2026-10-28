using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TCG.SwitchStreaming.Editor;

namespace TCG.SwitchStreaming.Tests
{
    // A real serialized catalogue and unknown consumer; no mocked AssetDatabase.
    public sealed class SurfaceAuditTests : ScriptableObject
    {
        public Texture2D mainTexture, normalMap, unknown;
        public UnityEngine.Object other;
        const string Root = "Assets/SurfaceAuditFixtures";
        static int passed;
        public static void Run()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            AssetDatabase.DeleteAsset(Root); Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            passed = 0; int failures = 0;
            foreach (Action scenario in new Action[] { ExactCataloguePropertiesAllowAliases, UnknownCataloguePropertyRetainsAliasGroup,
                MaterialDefaultRetains, DirectSceneReferenceRetains, SpriteAliasRetainsWithoutListedConsumer,
                BinaryConsumerRetains, ResourcesTextureRetains, ResourcesConsumerRetains,
                CandidateSubassetRetains, ImportedSpriteSubassetRetains, ImporterSecondaryTextureRetains,
                WrongCandidateIdentityRetains, ConflictingAliasRecordsRetain, UnresolvedRawGuidRetains,
                CatalogueSubassetCannotBorrowPropertyPermission, NullFallbackIsUnaffected,
                BinaryWrapperDoesNotInventTransitiveEdges, PoststripRejectsEveryOldSlot, DefaultScanFindsUnknownConsumers,
                DefaultScanExcludesStreamingAssetsPayloads, MissingSourceFailsClosed, CandidateMetadataDriftRetains,
                RecordedSlotIdentityDriftRetains, PlainImportedTextureIsEligible })
            {
                try { scenario(); passed++; Debug.Log("SURFACE_AUDIT_SCENARIO_PASS " + scenario.Method.Name); }
                catch (Exception error) { failures++; Debug.LogError(scenario.Method.Name + ": " + error); }
                finally { EditorSettings.serializationMode = SerializationMode.ForceText; }
            }
            if (failures != 0) throw new Exception("SURFACE_AUDIT_FAILED_SCENARIOS " + failures);
            Debug.Log("SURFACE_AUDIT_PASS " + passed + " scenarios");
        }
        static void Check(bool value, string message) { if (!value) throw new Exception("SURFACE_AUDIT_FAIL: " + message); }
        static SurfaceRecord TextureAsset(string name, string folder = null)
        {
            folder = folder ?? Root; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = name };
            string path = folder + "/" + name + ".asset"; AssetDatabase.CreateAsset(texture, path); AssetDatabase.SaveAssets();
            return Record(texture, "mainTexture");
        }
        static SurfaceRecord Record(Texture2D texture, string property)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            return new SurfaceRecord { key = "surface:floor:0:" + property, property = property, texture = texture,
                textureGuid = AssetDatabase.AssetPathToGUID(path), texturePath = path, textureName = texture.name, bytes = new FileInfo(path).Length };
        }
        static SurfaceAuditTests Catalogue(string name, SurfaceRecord record)
        {
            var catalogue = CreateInstance<SurfaceAuditTests>(); catalogue.mainTexture = record.texture;
            AssetDatabase.CreateAsset(catalogue, Root + "/" + name + "-catalogue.asset"); AssetDatabase.SaveAssets(); return catalogue;
        }
        static string Path(UnityEngine.Object value) { return AssetDatabase.GetAssetPath(value); }
        static SurfaceAuditResult Scan(SurfaceRecord record, SurfaceAuditTests catalogue, params string[] paths)
        { return SurfaceReferenceAudit.Scan(Path(catalogue), new[] { record }, paths); }
        static void Retained(SurfaceAuditResult result, SurfaceRecord record, string message)
        { Check(result.EligibleRecords.Count == 0 && result.RetainedTextures.ContainsKey(record.textureGuid), message); }
        static SurfaceAuditTests Consumer(string name, UnityEngine.Object value, string folder = null)
        {
            folder = folder ?? Root; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var consumer = CreateInstance<SurfaceAuditTests>(); consumer.other = value;
            AssetDatabase.CreateAsset(consumer, folder + "/" + name + ".asset"); AssetDatabase.SaveAssets(); return consumer;
        }
        static void ExactCataloguePropertiesAllowAliases()
        {
            var record = TextureAsset("allowed"); var catalogue = Catalogue("allowed", record);
            catalogue.normalMap = record.texture; EditorUtility.SetDirty(catalogue); AssetDatabase.SaveAssets();
            var alias = Record(record.texture, "normalMap"); string before = File.ReadAllText(Path(catalogue));
            var result = SurfaceReferenceAudit.Scan(Path(catalogue), new[] { record, alias }, new[] { Path(catalogue), record.texturePath });
            Check(result.EligibleRecords.Count == 2 && result.RetainedTextures.Count == 0, "both exact fields in main catalogue permit same original Texture2D");
            Check(File.ReadAllText(Path(catalogue)) == before, "scan is read only");
        }
        static void UnknownCataloguePropertyRetainsAliasGroup()
        {
            var record = TextureAsset("unknown"); var catalogue = Catalogue("unknown", record);
            catalogue.normalMap = catalogue.unknown = record.texture; EditorUtility.SetDirty(catalogue); AssetDatabase.SaveAssets();
            var result = SurfaceReferenceAudit.Scan(Path(catalogue), new[] { record, Record(record.texture, "normalMap") }, new[] { Path(catalogue) });
            Retained(result, record, "unknown field retains every record of shared GUID");
        }
        static void MaterialDefaultRetains()
        {
            var record = TextureAsset("material"); var catalogue = Catalogue("material", record);
            var material = new Material(Shader.Find("Unlit/Texture")); material.mainTexture = record.texture;
            string path = Root + "/default.mat"; AssetDatabase.CreateAsset(material, path); AssetDatabase.SaveAssets();
            Retained(Scan(record, catalogue, Path(catalogue), path), record, "material default is resident");
        }
        static void DirectSceneReferenceRetains()
        {
            var record = TextureAsset("scene"); var catalogue = Catalogue("scene", record);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("camera").AddComponent<Camera>().targetTexture = null;
            var material = new Material(Shader.Find("Unlit/Texture")); material.mainTexture = record.texture;
            new GameObject("render").AddComponent<MeshRenderer>().sharedMaterial = material;
            string path = Root + "/direct.unity"; EditorSceneManager.SaveScene(scene, path);
            Retained(Scan(record, catalogue, path, Path(catalogue)), record, "scene embedded material keeps direct texture edge");
        }
        static void SpriteAliasRetainsWithoutListedConsumer()
        {
            var record = TextureAsset("sprite"); var catalogue = Catalogue("sprite", record);
            var sprite = Sprite.Create(record.texture, new Rect(0, 0, 8, 8), Vector2.one * .5f);
            AssetDatabase.CreateAsset(sprite, Root + "/alias.asset"); AssetDatabase.SaveAssets();
            Retained(Scan(record, catalogue, Path(catalogue)), record, "standalone Sprite root discovered even with explicit consumer paths");
        }
        static void BinaryConsumerRetains()
        {
            var record = TextureAsset("binary"); var catalogue = Catalogue("binary", record);
            string text = File.ReadAllText(Path(catalogue));
            EditorSettings.serializationMode = SerializationMode.ForceBinary;
            var consumer = Consumer("binary-consumer", record.texture);
            File.WriteAllText(Path(catalogue), text); AssetDatabase.ImportAsset(Path(catalogue), ImportAssetOptions.ForceUpdate);
            Check(!File.ReadAllText(Path(consumer)).StartsWith("%YAML", StringComparison.Ordinal), "actual binary consumer");
            Retained(Scan(record, catalogue, Path(catalogue), Path(consumer)), record, "binary edge fails closed");
        }
        static void ResourcesTextureRetains()
        {
            var record = TextureAsset("resource-root", Root + "/Resources"); var catalogue = Catalogue("resource-root", record);
            Retained(Scan(record, catalogue, Path(catalogue)), record, "implicit Resources residency retained outside supplied paths");
        }
        static void ResourcesConsumerRetains()
        {
            var record = TextureAsset("resource-consumer"); var catalogue = Catalogue("resource-consumer", record);
            var consumer = Consumer("unknown", record.texture, Root + "/Resources");
            Retained(Scan(record, catalogue, Path(catalogue), Path(consumer)), record, "Resources consumers are never blanket exempted");
        }
        static void CandidateSubassetRetains()
        {
            var record = TextureAsset("subasset"); var catalogue = Catalogue("subasset", record);
            var extra = CreateInstance<SurfaceAuditTests>(); AssetDatabase.AddObjectToAsset(extra, record.texture); AssetDatabase.SaveAssets();
            Retained(Scan(record, catalogue, Path(catalogue)), record, "bundle payload cannot include extra subassets");
        }
        static Texture2D Imported(string name, bool sprite)
        {
            var image = new Texture2D(8, 8); string path = Root + "/" + name + ".png";
            File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default; importer.spriteImportMode = SpriteImportMode.Single; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void ImportedSpriteSubassetRetains()
        {
            var record = Record(Imported("sprite-import", true), "mainTexture"); var catalogue = Catalogue("sprite-import", record);
            Retained(Scan(record, catalogue, Path(catalogue)), record, "automatic Sprite subasset is not a one-texture payload");
        }
        static void ImporterSecondaryTextureRetains()
        {
            var record = TextureAsset("secondary"); var catalogue = Catalogue("secondary", record);
            var sprite = Imported("secondary-owner", true); var importer = (TextureImporter)AssetImporter.GetAtPath(Path(sprite));
            importer.secondarySpriteTextures = new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = record.texture } }; importer.SaveAndReimport();
            Retained(Scan(record, catalogue, Path(catalogue), Path(sprite)), record, "importer metadata secondary texture is resident");
        }
        static void WrongCandidateIdentityRetains()
        {
            var record = TextureAsset("identity"); var catalogue = Catalogue("identity", record);
            record.texture = TextureAsset("identity-other").texture;
            Retained(Scan(record, catalogue, Path(catalogue)), record, "mismatched live candidate identity fails closed");
        }
        static void ConflictingAliasRecordsRetain()
        {
            var record = TextureAsset("conflict"); var catalogue = Catalogue("conflict", record);
            var alias = Record(record.texture, "normalMap"); alias.texturePath = Root + "/not-the-source.asset";
            Retained(SurfaceReferenceAudit.Scan(Path(catalogue), new[] { record, alias }, new[] { Path(catalogue) }), record, "one invalid alias retains whole group");
        }
        static void UnresolvedRawGuidRetains()
        {
            var record = TextureAsset("unresolved"); var catalogue = Catalogue("unresolved", record);
            string path = Path(catalogue), text = File.ReadAllText(path);
            text = text.Replace("{fileID: 2800000, guid: " + record.textureGuid, "{fileID: 999999999, guid: " + record.textureGuid);
            File.WriteAllText(path, text); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Retained(Scan(record, catalogue, path), record, "raw unresolved GUID is not an allowed exact slot");
        }
        static void CatalogueSubassetCannotBorrowPropertyPermission()
        {
            var record = TextureAsset("catalogue-subasset"); var catalogue = Catalogue("catalogue-subasset", record);
            var extra = CreateInstance<SurfaceAuditTests>(); extra.mainTexture = record.texture; AssetDatabase.AddObjectToAsset(extra, catalogue); AssetDatabase.SaveAssets();
            Retained(Scan(record, catalogue, Path(catalogue)), record, "catalogue subasset cannot borrow main asset property permission");
        }
        static void NullFallbackIsUnaffected()
        {
            var record = TextureAsset("null"); var catalogue = Catalogue("null", record);
            var empty = Consumer("null-consumer", null); string before = File.ReadAllText(Path(empty));
            Check(Scan(record, catalogue, Path(catalogue), Path(empty)).EligibleRecords.Count == 1, "unrelated null does not retain");
            Check(File.ReadAllText(Path(empty)) == before, "null remains authored null");
        }
        static void BinaryWrapperDoesNotInventTransitiveEdges()
        {
            var record = TextureAsset("wrapper"); var catalogue = Catalogue("wrapper", record); string text = File.ReadAllText(Path(catalogue));
            EditorSettings.serializationMode = SerializationMode.ForceBinary;
            var wrapper = Consumer("binary-wrapper-consumer", catalogue);
            File.WriteAllText(Path(catalogue), text); AssetDatabase.ImportAsset(Path(catalogue), ImportAssetOptions.ForceUpdate);
            var result = Scan(record, catalogue, Path(catalogue), Path(wrapper));
            Check(result.EligibleRecords.Count == 1, "wrapper to exact audited catalogue is not a direct texture edge: " + String.Join(";", result.RetainedTextures.Values));
        }
        static void PoststripRejectsEveryOldSlot()
        {
            var record = TextureAsset("poststrip"); var catalogue = Catalogue("poststrip", record); string path = Path(catalogue);
            Check(SurfaceReferenceAudit.FindEagerReferences(new[] { record }, new[] { path }).Contains(path), "poststrip scan grants no original property exemption");
            catalogue.mainTexture = null; EditorUtility.SetDirty(catalogue); AssetDatabase.SaveAssets();
            record.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(record.texturePath);
            Check(SurfaceReferenceAudit.FindEagerReferences(new[] { record }, new[] { path, record.texturePath }).Count == 0, "identifier records with freshly reloaded pointers pass after exact removal");
        }
        static void DefaultScanFindsUnknownConsumers()
        {
            var record = TextureAsset("default"); var catalogue = Catalogue("default", record); Consumer("default-unknown", record.texture);
            Retained(SurfaceReferenceAudit.Scan(Path(catalogue), new[] { record }), record, "default scan finds all Assets consumers");
        }
        static void DefaultScanExcludesStreamingAssetsPayloads()
        {
            var record = TextureAsset("streaming"); var catalogue = Catalogue("streaming", record);
            var payload = Consumer("surface-payload", record.texture, "Assets/StreamingAssets/TCGStreamedSurfaces");
            try { Check(SurfaceReferenceAudit.Scan(Path(catalogue), new[] { record }).EligibleRecords.Count == 1, "StreamingAssets game bundle bytes are not resident roots"); }
            finally { AssetDatabase.DeleteAsset(Path(payload)); }
        }
        static void CandidateMetadataDriftRetains()
        {
            var record = TextureAsset("metadata-drift"); var catalogue = Catalogue("metadata-drift", record);
            string path = record.texturePath + ".meta", original = File.ReadAllText(path);
            try
            {
                File.WriteAllText(path, original.Replace("guid: " + record.textureGuid, "guid: 11111111111111111111111111111111"));
                Retained(Scan(record, catalogue, Path(catalogue)), record, "on-disk metadata GUID drift fails closed even before AssetDatabase refresh");
            }
            finally { File.WriteAllText(path, original); }
        }
        static void RecordedSlotIdentityDriftRetains()
        {
            var record = TextureAsset("slot-drift"); var catalogue = Catalogue("slot-drift", record);
            catalogue.mainTexture = null; EditorUtility.SetDirty(catalogue); AssetDatabase.SaveAssets();
            Retained(Scan(record, catalogue, Path(catalogue)), record, "a preparation record cannot authorize a slot that drifted to null");
        }
        static void PlainImportedTextureIsEligible()
        {
            var record = Record(Imported("plain-texture", false), "mainTexture"); var catalogue = Catalogue("plain-texture", record);
            Check(Scan(record, catalogue, Path(catalogue), record.texturePath).EligibleRecords.Count == 1, "plain imported main Texture2D outside Resources is not an implicit root");
        }
        static void MissingSourceFailsClosed()
        {
            var record = TextureAsset("missing-source"); var catalogue = Catalogue("missing-source", record);
            Retained(Scan(record, catalogue, Path(catalogue), Root + "/missing-consumer.asset"), record, "explicit unreadable consumer fails closed");
        }
    }
}
