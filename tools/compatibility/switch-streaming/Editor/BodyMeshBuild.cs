using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace TCG.SwitchStreaming.Editor
{
    public static class BodyMeshBuild
    {
        const string ScenePath = "Assets/1_Game/Scenes/Start.unity";
        const string Resident = "Assets/Resources/TCGStreaming/MeshCatalog.txt";
        const string Local = "Assets/StreamingAssets/TCGStreamedMeshes/catalog.json";
        static readonly string[] Prefabs = {
            "Assets/GameObject/CustomerFemalePrefab.prefab", "Assets/GameObject/CustomerPrefab.prefab",
            "Assets/GameObject/WorkerFemalePrefab.prefab", "Assets/GameObject/WorkerPrefab.prefab",
            "Assets/Resources/Female_Combined_HP Variant.prefab", "Assets/Resources/Male_Combined_HP Variant.prefab"
        };
        [Serializable] public sealed class FileSnapshot { public string path, guid, originalSha256, preparedSha256; }
        [Serializable] sealed class MeshStamp { public string path, guid, sha256; }
        [Serializable] sealed class BundleStamp { public string name, sha256; }
        [Serializable] sealed class Journal
        {
            public int version = 1;
            public string phase = "preparing";
            public BodyAudit audit;
            public List<MeshRecord> records;
            public List<MeshStamp> meshes = new List<MeshStamp>();
            public List<FileSnapshot> files;
            public List<BundleStamp> previousBundles = new List<BundleStamp>();
        }
        static string Project { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        static string Root { get { return Path.GetDirectoryName(Project); } }
        static string JournalPath { get { return Path.Combine(Root, "body-mesh-journal.json"); } }
        static string Backups { get { return Path.Combine(Root, "body-mesh-backup"); } }
        static void Guard()
        {
            if (Path.GetFileName(Project) != "macos-port-work" || Path.GetFileName(Root) != "switch-on-demand" || !File.Exists(Path.Combine(Project, "character-mesh-preparation.json")))
                throw new InvalidOperationException("Body preparation requires the private prepared on-demand clone");
        }
        public static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static void Relative(string path)
        {
            if (String.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("\\") || path.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                throw new InvalidOperationException("Invalid body journal source path");
        }
        static void Digest(string digest)
        {
            if (digest == null || digest.Length != 64 || digest.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')))
                throw new InvalidOperationException("Invalid body journal digest");
        }
        public static string BackupPath(string backups, FileSnapshot row)
        {
            Relative(row.path); Digest(row.originalSha256);
            return Path.Combine(Path.GetFullPath(backups), row.originalSha256, row.path);
        }
        public static List<FileSnapshot> CaptureFiles(string backups, IEnumerable<string> paths)
        {
            var rows = new List<FileSnapshot>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            // All identities and existing destinations are preflighted before creating any backup.
            foreach (string path in paths)
            {
                Relative(path); if (!seen.Add(path) || !File.Exists(path)) throw new InvalidOperationException("Duplicate/missing body source");
                var row = new FileSnapshot { path = path, originalSha256 = Hash(path), guid = path.EndsWith(".prefab", StringComparison.Ordinal) ? AssetDatabase.AssetPathToGUID(path) : null };
                if (path.EndsWith(".prefab", StringComparison.Ordinal) && String.IsNullOrEmpty(row.guid)) throw new InvalidOperationException("Missing prefab GUID");
                string saved = BackupPath(backups, row);
                if (File.Exists(saved) && Hash(saved) != row.originalSha256) throw new InvalidOperationException("Original body backup changed");
                rows.Add(row);
            }
            foreach (var row in rows)
            {
                string saved = BackupPath(backups, row); Directory.CreateDirectory(Path.GetDirectoryName(saved));
                if (!File.Exists(saved)) File.Copy(row.path, saved);
            }
            return rows;
        }
        public static void ValidateFiles(string backups, IList<FileSnapshot> rows)
        {
            if (rows == null || rows.Count == 0) throw new InvalidOperationException("Empty body recovery journal");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row == null) throw new InvalidOperationException("Missing body recovery entry");
                Relative(row.path); if (!seen.Add(row.path)) throw new InvalidOperationException("Duplicate body recovery entry");
                string saved = BackupPath(backups, row);
                if (!File.Exists(row.path) || !File.Exists(saved) || Hash(saved) != row.originalSha256) throw new InvalidOperationException("Body source/backup missing or changed: " + row.path);
                if (row.path.EndsWith(".prefab", StringComparison.Ordinal) && (String.IsNullOrEmpty(row.guid) || AssetDatabase.AssetPathToGUID(row.path) != row.guid))
                    throw new InvalidOperationException("Body prefab GUID changed: " + row.path);
                string current = Hash(row.path);
                if (current != row.originalSha256 && (String.IsNullOrEmpty(row.preparedSha256) || current != row.preparedSha256))
                    throw new InvalidOperationException("Body source changed or interrupted before receipt: " + row.path);
            }
        }
        public static void RestoreFiles(string backups, IList<FileSnapshot> rows)
        {
            ValidateFiles(backups, rows);
            foreach (var row in rows) if (Hash(row.path) != row.originalSha256) File.Copy(BackupPath(backups, row), row.path, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        static void Save(Journal journal)
        {
            string temporary = JournalPath + ".new";
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))) writer.Write(JsonUtility.ToJson(journal, true));
            if (File.Exists(JournalPath)) File.Replace(temporary, JournalPath, null); else File.Move(temporary, JournalPath);
        }
        static Journal Read()
        {
            if (File.Exists(JournalPath + ".new")) throw new InvalidOperationException("Interrupted body journal write requires inspection");
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(JournalPath));
            if (journal == null || journal.version != 1 || journal.audit == null || journal.records == null || journal.records.Count != 8 || journal.meshes == null || journal.meshes.Count != 8 ||
                journal.files == null || journal.files.Count != 8 || !new HashSet<string>(Prefabs.Concat(new[] { Resident, Local }), StringComparer.Ordinal).SetEquals(journal.files.Select(f => f.path)) ||
                journal.previousBundles == null || journal.previousBundles.Count != 252 || !new[] { "preparing", "prepared", "restored" }.Contains(journal.phase))
                throw new InvalidOperationException("Invalid body preparation journal");
            var scene = journal.audit.sources.SingleOrDefault(s => s.path == ScenePath);
            if (journal.audit.sources.Count != 7 || scene == null || scene.guid != AssetDatabase.AssetPathToGUID(ScenePath) || scene.sha256 != Hash(ScenePath)) throw new InvalidOperationException("Body source scene changed");
            foreach (var mesh in journal.meshes)
                if (AssetDatabase.GUIDToAssetPath(mesh.guid) != mesh.path || Hash(mesh.path) != mesh.sha256) throw new InvalidOperationException("Original body mesh changed");
            return journal;
        }
        public static void Restore()
        {
            Guard(); if (!File.Exists(JournalPath)) return;
            var journal = Read(); ValidateFiles(Backups, journal.files);
            RestoreFiles(Backups, journal.files); journal.phase = "restored"; Save(journal);
        }
        static List<MeshRecord> Reload(IList<MeshRecord> records)
        {
            foreach (var record in records)
            {
                record.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(record.path);
                if (!record.mesh || AssetDatabase.AssetPathToGUID(record.path) != record.guid) throw new InvalidOperationException("Body source identity changed during build");
            }
            return records.ToList();
        }
        static void VerifyPrevious(Journal journal, string output)
        {
            foreach (var row in journal.previousBundles)
            {
                if (row.name == null || row.name.Contains("/") || row.name.Contains("\\") || !File.Exists(Path.Combine(output, row.name)) || Hash(Path.Combine(output, row.name)) != row.sha256)
                    throw new InvalidOperationException("Existing choice bundle changed during body preparation");
            }
        }
        public static List<MeshRecord> CollectBodies(IEnumerable<string> paths)
        {
            var records = new List<MeshRecord>();
            foreach (string path in paths)
            {
                Relative(path); var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); string guid; long id;
                if (!mesh || !AssetDatabase.IsMainAsset(mesh) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out guid, out id))
                    throw new InvalidOperationException("Body requires a standalone authored Mesh: " + path);
                var flag = new SerializedObject(mesh).FindProperty("m_MeshUsageFlags"); long bytes = Profiler.GetRuntimeMemorySizeLong(mesh);
                if (flag == null || bytes <= 0 || records.Any(r => r.guid == guid)) throw new InvalidOperationException("Missing/duplicate body accounting identity");
                records.Add(new MeshRecord { path = path, guid = guid, key = "mesh:" + guid, name = mesh.name, bytes = bytes, usageFlags = flag.longValue, mesh = mesh });
            }
            return records.OrderBy(r => r.guid, StringComparer.Ordinal).ToList();
        }
        public static void Prepare()
        {
            Guard();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows) throw new InvalidOperationException("Body bundles require original Win32 target");
            if (File.Exists(JournalPath) && Read().phase != "restored") throw new InvalidOperationException("Restore body state before preparation");
            string originalManifest = File.ReadAllText(Local);
            if (originalManifest != File.ReadAllText(Resident)) throw new InvalidOperationException("Choice manifest copies differ");
            var manifest = JsonUtility.FromJson<MeshManifest>(originalManifest); manifest.Validate();
            if (manifest.entries.Count != 252) throw new InvalidOperationException("Expected original252 choice entries");
            var expectedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string gender in new[] { "Male", "Female" }) for (int lod = 0; lod < 4; lod++) expectedPaths.Add("Assets/Mesh/" + gender + "_Combined_LOD" + lod + ".asset");
            var records = CollectBodies(expectedPaths);
            if (records.Count != 8 || !expectedPaths.SetEquals(records.Select(r => r.path)) || records.Any(r => r.usageFlags != 1)) throw new InvalidOperationException("Original eight body identities/flags changed");
            var audit = BodyMeshReferenceAudit.Scan(records, Prefabs, ScenePath, 4);
            var sceneSource = audit.sources.Single(s => s.path == ScenePath);
            if (sceneSource.slots.Count != 224 || sceneSource.slots.Select(s => s.ownerPath).Distinct().Count() != 56 || audit.sources.Where(s => s.path != ScenePath).Any(s => s.slots.Count != 4) || audit.sources.Sum(s => s.slots.Count) != 248)
                throw new InvalidOperationException("Original body dependency accounting changed");
            string output = Path.GetDirectoryName(Local); var journal = new Journal { audit = audit, records = records };
            foreach (var record in records) journal.meshes.Add(new MeshStamp { path = record.path, guid = record.guid, sha256 = Hash(record.path) });
            foreach (var entry in manifest.entries) journal.previousBundles.Add(new BundleStamp { name = entry.bundle, sha256 = Hash(Path.Combine(output, entry.bundle)) });
            // Build outside the shared252-bundle output; Unity's aggregate manifest cannot affect it.
            string stage = Path.Combine(Root, "body-mesh-bundles"); Directory.CreateDirectory(stage);
            var builds = records.Select(r => new AssetBundleBuild { assetBundleName = "mesh_" + r.guid, assetNames = new[] { r.path }, addressableNames = new[] { "assets/tcgstreamedmesh/" + r.guid + ".asset" } }).ToArray();
            var built = BuildPipeline.BuildAssetBundles(stage, builds, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows);
            if (!built) throw new InvalidOperationException("Body mesh bundle build failed");
            var crcs = CharacterMeshBuild.PreserveUsageFlags(stage, records, Path.Combine(Root, "body-mesh-flag-request.json"));
            foreach (var record in records)
            {
                string name = "mesh_" + record.guid; uint crc;
                if (built.GetAllDependencies(name).Length != 0 || !crcs.TryGetValue(Path.GetFullPath(Path.Combine(stage, name)), out crc)) throw new InvalidOperationException("Body bundle dependency/CRC proof failed");
                manifest.entries.Add(new MeshEntry { key = record.key, bundle = name, asset = "assets/tcgstreamedmesh/" + record.guid + ".asset", bytes = record.bytes, crc = crc });
            }
            manifest.Validate(); if (manifest.entries.Count != 260) throw new InvalidOperationException("Combined body manifest count differs");
            VerifyPrevious(journal, output);
            journal.files = CaptureFiles(Backups, Prefabs.Concat(new[] { Resident, Local })); Save(journal);
            foreach (var record in records) File.Copy(Path.Combine(stage, "mesh_" + record.guid), Path.Combine(output, "mesh_" + record.guid), true);
            records = Reload(records);
            foreach (var row in journal.files.Where(f => f.path.EndsWith(".prefab", StringComparison.Ordinal)))
            {
                CharacterMeshBuild.RewritePrefab(row.path, records); row.preparedSha256 = Hash(row.path); Save(journal);
            }
            string combined = JsonUtility.ToJson(manifest, true);
            foreach (var row in journal.files.Where(f => !f.path.EndsWith(".prefab", StringComparison.Ordinal)))
            {
                File.WriteAllText(row.path, combined); row.preparedSha256 = Hash(row.path); Save(journal);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); VerifyPrevious(journal, output);
            if (Hash(ScenePath) != sceneSource.sha256) throw new InvalidOperationException("Source scene changed during preparation");
            journal.phase = "prepared"; Save(journal);
            Debug.Log("[BODY-BUILD] Prepared eight exact meshes, six prefabs and260-entry catalogue; scene remains unchanged");
        }
        public static void RewriteScene(Scene scene, BodySource source, IList<MeshRecord> records)
        {
            if (source == null || source.path != scene.path || source.guid != AssetDatabase.AssetPathToGUID(source.path) || source.sha256 != Hash(source.path)) throw new InvalidOperationException("Body scene identity/hash changed");
            var roots = scene.GetRootGameObjects(); var actual = BodyMeshReferenceAudit.Inspect(roots, records, source.slots.GroupBy(s => s.ownerPath).First().Count());
            Func<BodySlot, string> key = s => s.ownerPath + ":" + s.rendererPath + ":" + s.meshGuid;
            if (!actual.Select(key).OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(source.slots.Select(key).OrderBy(k => k, StringComparer.Ordinal))) throw new InvalidOperationException("Build scene body ownership changed");
            var transforms = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToDictionary(BodyMeshReferenceAudit.Route, StringComparer.Ordinal);
            foreach (var group in source.slots.GroupBy(s => s.ownerPath))
                if (transforms[group.Key].GetComponent<MeshBundleOwner>()) throw new InvalidOperationException("Body scene was already converted");
            foreach (var group in source.slots.GroupBy(s => s.ownerPath))
                if (CharacterMeshBuild.RewriteHierarchy(transforms[group.Key].gameObject, records) != group.Count()) throw new InvalidOperationException("Scene body conversion accounting changed");
            if (Hash(source.path) != source.sha256) throw new InvalidOperationException("Body conversion wrote source scene");
        }
        public static void ProcessScene(Scene scene, BuildReport report)
        {
            if (scene.path != ScenePath || Path.GetFileName(Project) != "macos-port-work" || Path.GetFileName(Root) != "switch-on-demand") return;
            Guard();
            if (report == null || report.summary.platform != BuildTarget.StandaloneWindows || !File.Exists(JournalPath)) throw new InvalidOperationException("Body scene requires verified Win32 preparation");
            var journal = Read(); ValidateFiles(Backups, journal.files);
            if (journal.phase != "prepared" || journal.files.Any(f => String.IsNullOrEmpty(f.preparedSha256) || Hash(f.path) != f.preparedSha256)) throw new InvalidOperationException("Body preparation incomplete");
            VerifyPrevious(journal, Path.GetDirectoryName(Local));
            var source = journal.audit.sources.Single(s => s.path == ScenePath);
            RewriteScene(scene, source, Reload(journal.records));
            File.WriteAllText(Path.Combine(Root, "body-scene-build-proof.json"), JsonUtility.ToJson(source, true));
            Debug.Log("[BODY-BUILD] Converted224 scene slots on56 owners without saving source scene");
        }
    }
}
