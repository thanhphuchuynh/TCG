using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TCG.SwitchStreaming.Editor
{
    public static partial class StreamingBuild
    {
        const string Catalogue = "Assets/MonoBehaviour/SO_MonsterData.asset";
        static readonly string[] Scenes = { "Assets/1_Game/Scenes/Title.unity", "Assets/1_Game/Scenes/Start.unity" };
        const long Fat32Maximum = 4294967295L;
        [Serializable] sealed class Retained { public string texture, reason; }
        [Serializable] sealed class Report
        {
            public string status = "started", error, unity, player, backend, graphics;
            public int quality, records, eligibleRecords, textureBundles, errors, warnings;
            public long migratedImageBytes, largestFile;
            public List<Retained> retained = new List<Retained>();
            public List<ArtRecord> artwork;
        }

        // Each authored Sprite is explicitly assigned to its texture's bundle.
        // Loading by asset path is intentionally limited to a main Sprite asset;
        // ambiguous subassets must be given a stable selector before migration.
        public static AssetBundleBuild[] CreateBundlePlan(IList<ArtRecord> records)
        {
            var groups = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (!keys.Add(record.key) || String.IsNullOrEmpty(record.textureGuid) || record.bytes <= 0 ||
                    !record.sprite || AssetDatabase.LoadAssetAtPath<Sprite>(record.spritePath) != record.sprite)
                    throw new InvalidOperationException("Unaddressable or duplicate authored artwork: " + record.key);
                SortedSet<string> paths;
                if (!groups.TryGetValue(record.textureGuid, out paths)) groups.Add(record.textureGuid, paths = new SortedSet<string>(StringComparer.Ordinal));
                paths.Add(record.spritePath);
            }
            return groups.Select(group => new AssetBundleBuild { assetBundleName = "art_" + group.Key, assetNames = group.Value.ToArray() }).ToArray();
        }

        public static void Run()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string root = Path.GetDirectoryName(project);
            string snapshots = Path.Combine(root, "streaming-asset-backup");
            var report = new Report { unity = Application.unityVersion, player = Path.Combine(root, "streamed-player/TCG Autorun Test.exe") };
            int exit = 2;
            try
            {
                if (Path.GetFileName(root) != "switch-on-demand" || Path.GetFileName(project) != "macos-port-work" || !File.Exists(Path.Combine(project, "streaming-preparation.json")))
                    throw new InvalidOperationException("Run only in the prepared private switch-on-demand clone");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows || !BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows))
                    throw new InvalidOperationException("Start Unity with -buildTarget Win and the x86 Windows module installed");
                // Recover only our previous serialized-asset edits on a retry.
                if (Directory.Exists(snapshots))
                {
                    foreach (string saved in Directory.GetFiles(snapshots, "*", SearchOption.AllDirectories))
                    {
                        string relative = saved.Substring(snapshots.Length + 1);
                        File.Copy(saved, Path.Combine(project, relative), true);
                    }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                var catalogue = AssetDatabase.LoadMainAssetAtPath(Catalogue);
                if (!catalogue) throw new InvalidOperationException("Original catalogue missing");
                var records = CollectRecords(catalogue); report.records = records.Count;
                if (records.Count == 0) throw new InvalidOperationException("Empty artwork catalogue");
                Debug.Log("[STREAMING-BUILD] Auditing " + records.Count + " catalogue records");
                var audit = ReferenceAudit.Scan(Catalogue, records);
                report.artwork = audit.Eligible; report.eligibleRecords = audit.Eligible.Count;
                foreach (var retained in audit.RetainedTextures.OrderBy(pair => pair.Key, StringComparer.Ordinal)) report.retained.Add(new Retained { texture = retained.Key, reason = retained.Value });
                if (audit.Eligible.Count == 0) throw new InvalidOperationException("No safely relocatable artwork");
                Debug.Log("[STREAMING-BUILD] Eligible records=" + audit.Eligible.Count + " retained textures=" + audit.RetainedTextures.Count);
                var builds = CreateBundlePlan(audit.Eligible);
                report.textureBundles = builds.Length;
                report.migratedImageBytes = audit.Eligible.GroupBy(record => record.textureGuid).Sum(group => group.First().bytes);
                File.WriteAllText(Path.Combine(root, "streaming-reference-audit.json"), JsonUtility.ToJson(report, true));
                var eligibleGuids = new HashSet<string>(audit.Eligible.Select(record => record.textureGuid), StringComparer.Ordinal);
                Sprite loading = FindLoadingSprite(eligibleGuids);
                Directory.CreateDirectory("Assets/Resources/TCGStreaming");
                const string loadingPath = "Assets/Resources/TCGStreaming/Loading.asset";
                if (!AssetDatabase.LoadAssetAtPath<Sprite>(loadingPath)) AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(loading), loadingPath);
                // Back up precisely the serialized assets about to be edited.
                foreach (string path in audit.ImageFiles.Concat(new[] { Catalogue }).Distinct(StringComparer.Ordinal))
                {
                    string saved = Path.Combine(snapshots, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved));
                    if (!File.Exists(saved)) File.Copy(Path.Combine(project, path), saved);
                }
                string output = Path.Combine(Application.streamingAssetsPath, "TCGStreamedArt");
                Directory.CreateDirectory(output);
                AssetDatabase.SaveAssets();
                Debug.Log("[STREAMING-BUILD] Building " + builds.Length + " per-texture bundles");
                var bundles = BuildPipeline.BuildAssetBundles(output, builds,
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                    BuildTarget.StandaloneWindows);
                if (!bundles) throw new InvalidOperationException("Artwork bundle build failed");
                var manifest = new ArtManifest();
                foreach (var group in audit.Eligible.GroupBy(record => record.textureGuid))
                {
                    string name = "art_" + group.Key; uint crc;
                    if (bundles.GetAllDependencies(name).Length != 0) throw new InvalidOperationException("Artwork has unexpected external bundle dependencies: " + name);
                    if (!BuildPipeline.GetCRCForAssetBundle(Path.Combine(output, name), out crc)) throw new InvalidOperationException("Missing bundle CRC: " + name);
                    foreach (var record in group) manifest.entries.Add(new ArtEntry { key = record.key, bundle = name, asset = record.spritePath.ToLowerInvariant(), bytes = record.bytes, crc = crc });
                }
                manifest.Validate();
                string catalogJson = JsonUtility.ToJson(manifest, true);
                File.WriteAllText(Path.Combine(output, "catalog.json"), catalogJson);
                // A tiny identifier-only resident copy makes a missing/altered SD
                // manifest fail explicitly instead of falling back to null art.
                File.WriteAllText("Assets/Resources/TCGStreaming/Catalog.txt", catalogJson);
                AssetDatabase.ImportAsset("Assets/Resources/TCGStreaming/Catalog.txt", ImportAssetOptions.ForceSynchronousImport);
                // Bundle builds may unload editor objects. Reopen assets by stable
                // identity instead of keeping invalidated Unity object handles.
                catalogue = AssetDatabase.LoadMainAssetAtPath(Catalogue);
                StripCatalogue(catalogue, audit.Eligible);
                ReferenceAudit.StripImages(audit.ImageFiles, eligibleGuids, AssetDatabase.LoadAssetAtPath<Sprite>(loadingPath));
                AssetDatabase.SaveAssets();
                var remaining = ReferenceAudit.FindEagerReferences(audit.Eligible);
                if (remaining.Count != 0) throw new InvalidOperationException("Unexplained eager artwork references remain: " + String.Join("; ", remaining.ToArray()));
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Disabled);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows, new[] { GraphicsDeviceType.OpenGLCore });
                report.backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString();
                report.graphics = String.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows).Select(api => api.ToString()).ToArray());
                report.quality = QualitySettings.GetQualityLevel();
                if (report.quality != 5) throw new InvalidOperationException("Current Ultra quality level changed");
                if (Directory.Exists(Path.Combine(Application.streamingAssetsPath, "TCGAutorunBundles"))) throw new InvalidOperationException("Obsolete all-content bootstrap bundles remain");
                Directory.CreateDirectory(Path.GetDirectoryName(report.player));
                Debug.Log("[STREAMING-BUILD] Building direct-scene player");
                var built = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes, locationPathName = report.player,
                    target = BuildTarget.StandaloneWindows, targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.None });
                report.errors = (int)built.summary.totalErrors; report.warnings = (int)built.summary.totalWarnings;
                if (built.summary.result != BuildResult.Succeeded || report.errors != 0) throw new InvalidOperationException("Direct-scene player build failed");
                foreach (string path in Directory.GetFiles(Path.GetDirectoryName(report.player), "*", SearchOption.AllDirectories))
                {
                    long length = new FileInfo(path).Length; report.largestFile = Math.Max(report.largestFile, length);
                    if (length > Fat32Maximum) throw new InvalidOperationException("File exceeds FAT32 maximum: " + path);
                }
                VerifyPlayerHeader(report.player);
                report.status = "succeeded"; exit = 0;
            }
            catch (Exception error) { report.status = "failed"; report.error = error.ToString(); Debug.LogException(error); }
            File.WriteAllText(Path.Combine(root, "streaming-build.json"), JsonUtility.ToJson(report, true));
            EditorApplication.Exit(exit);
        }

        static Sprite FindLoadingSprite(ISet<string> migrating)
        {
            // Recovered scenes do not serialize LoadStreamTexture's optional loading
            // Sprite. Use the game's existing tiny UI Sprite; pending cards remain
            // hidden until their complete authored artwork has been rebound.
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/White_Pixel_1x1.asset");
            if (sprite && sprite.texture && sprite.texture.width <= 64 && sprite.texture.height <= 64 &&
                !migrating.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture)))) return sprite;
            throw new InvalidOperationException("Original resident loading Sprite not found");
        }
        static void VerifyPlayerHeader(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                reader.BaseStream.Position = 0x3c; int pe = reader.ReadInt32(); reader.BaseStream.Position = pe;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x14c) throw new InvalidOperationException("Player is not Windows x86");
                reader.BaseStream.Position = pe + 22;
                if ((reader.ReadUInt16() & 0x20) == 0) throw new InvalidOperationException("Player is not LargeAddressAware");
            }
        }
    }
}
