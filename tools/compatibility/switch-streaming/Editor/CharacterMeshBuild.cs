using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using Debug = UnityEngine.Debug;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace TCG.SwitchStreaming.Editor
{
    public static class CharacterMeshBuild
    {
        [Serializable] sealed class Move { public string guid, original, current; }
        [Serializable] public sealed class PrefabSnapshot { public string path, guid, originalSha256, preparedSha256; }
        [Serializable] sealed class Journal { public int version; public List<Move> moves = new List<Move>(); public List<PrefabSnapshot> snapshots = new List<PrefabSnapshot>(); }
        [Serializable] sealed class Retained { public string guid, reason; }
        [Serializable] sealed class Report
        {
            public string status = "started", error;
            public int candidateCount, bundleCount;
            public long estimatedMeshBytes;
            public List<MeshRecord> eligible = new List<MeshRecord>();
            public List<Retained> retained = new List<Retained>();
        }
        static string Project { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        static string Root { get { return Path.GetDirectoryName(Project); } }
        static string JournalPath { get { return Path.Combine(Root, "character-mesh-journal.json"); } }
        static string Backups { get { return Path.Combine(Root, "character-mesh-asset-backup"); } }
        static void Guard()
        {
            if (Path.GetFileName(Root) != "switch-on-demand" || Path.GetFileName(Project) != "macos-port-work" || !File.Exists(Path.Combine(Project, "character-mesh-preparation.json")))
                throw new InvalidOperationException("Character meshes require the prepared private on-demand clone");
        }

        public static List<string> CollectPartPrefabs(IEnumerable<UnityEngine.Object> tables)
        {
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                var serialized = new SerializedObject(table);
                foreach (string field in new[] { "Hairstyles", "Items" })
                {
                    var list = serialized.FindProperty(field); if (list == null) continue;
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        var property = list.GetArrayElementAtIndex(i).FindPropertyRelative("Mesh");
                        if (property == null || !property.objectReferenceValue) continue;
                        var prefab = property.objectReferenceValue as GameObject;
                        string path = AssetDatabase.GetAssetPath(prefab);
                        if (!prefab || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choice is not an authored prefab");
                        paths.Add(path);
                    }
                }
            }
            return paths.ToList();
        }
        public static List<MeshRecord> CollectMeshes(IEnumerable<string> prefabs)
        {
            var records = new Dictionary<string, MeshRecord>(StringComparer.Ordinal);
            foreach (string path in prefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(records, renderer.sharedMesh);
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) Add(records, filter.sharedMesh);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return records.Values.OrderBy(record => record.guid, StringComparer.Ordinal).ToList();
        }
        static void Add(Dictionary<string, MeshRecord> records, Mesh mesh)
        {
            if (!mesh) return;
            string guid; long id; string path = AssetDatabase.GetAssetPath(mesh);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsMainAsset(mesh) ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out guid, out id))
                throw new InvalidOperationException("Choice mesh has no standalone authored identity: " + mesh.name);
            if (records.ContainsKey(guid)) return;
            long estimate = Profiler.GetRuntimeMemorySizeLong(mesh);
            if (estimate <= 0) throw new InvalidOperationException("Mesh accounting estimate unavailable: " + path);
            var usage = new SerializedObject(mesh).FindProperty("m_MeshUsageFlags");
            if (usage == null || usage.longValue < Int32.MinValue || usage.longValue > Int32.MaxValue) throw new InvalidOperationException("Authored mesh usage flags unavailable: " + path);
            records.Add(guid, new MeshRecord { key = "mesh:" + guid, guid = guid, path = path, name = mesh.name, bytes = estimate, usageFlags = usage.longValue, mesh = mesh });
        }
        public static void RewritePrefab(string path, IList<MeshRecord> eligible)
        {
            var byGuid = eligible.ToDictionary(record => record.guid, StringComparer.Ordinal);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<MeshBundleOwner>()) throw new InvalidOperationException("Restore original prefab before retry: " + path);
                var bindings = new List<MeshBinding>();
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    MeshRecord record;
                    if (!Find(renderer.sharedMesh, byGuid, out record)) continue;
                    var weights = new float[renderer.sharedMesh.blendShapeCount];
                    for (int i = 0; i < weights.Length; i++) weights[i] = renderer.GetBlendShapeWeight(i);
                    bindings.Add(new MeshBinding { skinned = renderer, key = record.key, defaultWeights = weights, defaultBounds = renderer.localBounds });
                    renderer.sharedMesh = null;
                }
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    MeshRecord record; if (!Find(filter.sharedMesh, byGuid, out record)) continue;
                    bindings.Add(new MeshBinding { filter = filter, key = record.key, defaultWeights = new float[0] }); filter.sharedMesh = null;
                }
                if (bindings.Count == 0) return;
                root.AddComponent<MeshBundleOwner>().Bindings = bindings.ToArray();
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new IOException("Cannot save mesh bindings: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static bool Find(Mesh mesh, Dictionary<string, MeshRecord> records, out MeshRecord record)
        {
            record = null; if (!mesh) return false;
            string guid; long id;
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out guid, out id) && records.TryGetValue(guid, out record);
        }

        [Serializable] sealed class FlagRequest { public List<FlagEntry> entries = new List<FlagEntry>(); }
        [Serializable] sealed class FlagEntry { public string bundle; public long sourceFlag; }
        [Serializable] sealed class FlagProof { public List<FlagProofEntry> entries; }
        [Serializable] sealed class FlagProofEntry { public string bundle; public long sourceFlag; public uint crc; }
        static string QuoteArgument(string value)
        {
            if (value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new InvalidOperationException("Invalid local build-tool path");
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
        public static Dictionary<string, uint> PreserveUsageFlags(string output, IList<MeshRecord> records, string requestPath)
        {
            string python = Environment.GetEnvironmentVariable("TCG_STREAMING_PYTHON");
            string tool = Environment.GetEnvironmentVariable("TCG_MESH_FLAG_TOOL");
            if (String.IsNullOrEmpty(python) || !Path.IsPathRooted(python) || !File.Exists(python) ||
                String.IsNullOrEmpty(tool) || !Path.IsPathRooted(tool) || !File.Exists(tool))
                throw new InvalidOperationException("Configure absolute TCG_STREAMING_PYTHON and TCG_MESH_FLAG_TOOL paths for faithful mesh flag preservation");
            var request = new FlagRequest();
            foreach (var record in records)
            {
                if (record.usageFlags < Int32.MinValue || record.usageFlags > Int32.MaxValue) throw new InvalidOperationException("Invalid authored mesh usage flags");
                request.entries.Add(new FlagEntry { bundle = Path.GetFullPath(Path.Combine(output, "mesh_" + record.guid)), sourceFlag = record.usageFlags });
            }
            File.WriteAllText(requestPath, JsonUtility.ToJson(request, true));
            var start = new ProcessStartInfo { FileName = python, Arguments = QuoteArgument(tool) + " --request " + QuoteArgument(Path.GetFullPath(requestPath)),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Could not start mesh flag preservation");
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(600000)) { process.Kill(); throw new TimeoutException("Mesh flag preservation exceeded ten minutes"); }
                string result = stdout.Result; string errors = stderr.Result;
                if (process.ExitCode != 0) throw new InvalidOperationException("Mesh flag preservation failed: " + errors + result);
                var proof = JsonUtility.FromJson<FlagProof>(result);
                var expected = request.entries.ToDictionary(entry => entry.bundle, entry => entry.sourceFlag, StringComparer.Ordinal);
                if (proof == null || proof.entries == null || proof.entries.Count != expected.Count) throw new InvalidDataException("Mesh preservation proof count differs from request");
                var crcs = new Dictionary<string, uint>(StringComparer.Ordinal);
                foreach (var entry in proof.entries)
                {
                    long original;
                    if (entry == null || entry.bundle == null || !expected.TryGetValue(entry.bundle, out original) || entry.sourceFlag != original || entry.crc == 0 || crcs.ContainsKey(entry.bundle))
                        throw new InvalidDataException("Mesh preservation proof does not match exact requested identity/flag/CRC");
                    crcs.Add(entry.bundle, entry.crc);
                }
                Debug.Log("[MESH-BUILD] Authored usage flags preserved: " + result);
                return crcs;
            }
            // Unity's GetCRCForAssetBundle reads its build sidecar (stale after
            // preservation). Use the verified decompressed-payload CRC, checked
            // by native LoadFromFile fixtures and the independent artifact audit.
        }

        static string HashFile(string path)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        static string PrefabFile(string root, string path)
        {
            if (String.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || path.Contains("\\") || path.Split('/').Any(part => part == "." || part == ".." || part.Length == 0))
                throw new InvalidOperationException("Invalid journaled prefab path");
            return Path.GetFullPath(Path.Combine(root, path));
        }
        static void ValidatePrefabSnapshots(string project, string backups, IList<PrefabSnapshot> snapshots)
        {
            if (Path.GetFullPath(project) != Project || snapshots == null || snapshots.Count == 0) throw new InvalidOperationException("Expected current project and nonempty prefab identity journal");
            var paths = new HashSet<string>(StringComparer.Ordinal); var guids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in snapshots)
            {
                if (item == null || !paths.Add(item.path) || String.IsNullOrEmpty(item.guid) || !guids.Add(item.guid)) throw new InvalidOperationException("Duplicate or missing prefab journal identity");
                string current = PrefabFile(project, item.path), saved = PrefabFile(backups, item.path);
                if (!File.Exists(current) || !File.Exists(saved) || AssetDatabase.AssetPathToGUID(item.path) != item.guid) throw new InvalidOperationException("Prefab identity/backup changed: " + item.path);
                if (String.IsNullOrEmpty(item.originalSha256) || HashFile(saved) != item.originalSha256) throw new InvalidOperationException("Original prefab backup changed: " + item.path);
                string actual = HashFile(current);
                if (actual != item.originalSha256 && (String.IsNullOrEmpty(item.preparedSha256) || actual != item.preparedSha256))
                    throw new InvalidOperationException("Prefab content changed or preparation was interrupted before its receipt: " + item.path + ". Preserve edits and inspect before recovery.");
            }
        }
        public static void RestorePrefabSnapshots(string project, string backups, IList<PrefabSnapshot> snapshots)
        {
            // Validate the entire batch first; later drift must not clobber earlier files.
            ValidatePrefabSnapshots(project, backups, snapshots);
            foreach (var item in snapshots)
                if (HashFile(PrefabFile(project, item.path)) != item.originalSha256)
                    File.Copy(PrefabFile(backups, item.path), PrefabFile(project, item.path), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        static void SaveJournal(Journal journal)
        {
            string temporary = JournalPath + ".new";
            File.WriteAllText(temporary, JsonUtility.ToJson(journal, true));
            if (File.Exists(JournalPath)) File.Replace(temporary, JournalPath, null); else File.Move(temporary, JournalPath);
        }
        public static void Restore()
        {
            Guard(); if (!File.Exists(JournalPath)) return;
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(JournalPath));
            if (journal == null || journal.version != 2 || journal.moves == null) throw new InvalidOperationException("Legacy/incomplete mesh journal requires verified recovery before retry");
            ValidatePrefabSnapshots(Project, Backups, journal.snapshots);
            // All identities and destinations are checked before any asset move.
            var moveGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var move in journal.moves)
            {
                if (move == null || String.IsNullOrEmpty(move.guid) || !moveGuids.Add(move.guid)) throw new InvalidOperationException("Invalid mesh move journal identity");
                string path = AssetDatabase.GUIDToAssetPath(move.guid);
                if (path != move.original && path != move.current) throw new InvalidOperationException("Mesh moved outside preparation journal: " + move.guid);
                if (path == move.current && File.Exists(Path.Combine(Project, move.original))) throw new InvalidOperationException("Original mesh destination is occupied: " + move.original);
            }
            foreach (var move in journal.moves)
            {
                string path = AssetDatabase.GUIDToAssetPath(move.guid); if (path == move.original) continue;
                string error = AssetDatabase.MoveAsset(path, move.original); if (error.Length != 0) throw new IOException(error);
            }
            RestorePrefabSnapshots(Project, Backups, journal.snapshots);
        }
        public static void Prepare()
        {
            Guard(); var report = new Report();
            try
            {
                string[] tableNames = { "Beards", "Hair_M", "Hair_F", "Eyebrows", "UpperBody_M", "UpperBody_F", "LowerBody_M", "LowerBody_F", "Footwear_M", "Footwear_F", "Misc_M", "Misc_F", "Dummy_Hats" };
                var tables = tableNames.Select(name => AssetDatabase.LoadMainAssetAtPath("Assets/MonoBehaviour/" + name + ".asset")).ToArray();
                if (tables.Any(table => !table)) throw new InvalidOperationException("Expected character choice tables missing");
                var prefabs = CollectPartPrefabs(tables); var records = CollectMeshes(prefabs); report.candidateCount = records.Count;
                Debug.Log("[MESH-BUILD] Auditing " + records.Count + " meshes across " + prefabs.Count + " choice prefabs");
                var audit = MeshReferenceAudit.Scan(records, new HashSet<string>(prefabs, StringComparer.Ordinal));
                foreach (var pair in audit.Retained.OrderBy(pair => pair.Key, StringComparer.Ordinal)) report.retained.Add(new Retained { guid = pair.Key, reason = pair.Value });
                report.eligible = audit.Eligible; report.bundleCount = audit.Eligible.Count; report.estimatedMeshBytes = audit.Eligible.Sum(record => record.bytes);
                File.WriteAllText(Path.Combine(Root, "character-mesh-audit.json"), JsonUtility.ToJson(report, true));
                if (audit.Eligible.Count == 0) throw new InvalidOperationException("No safely relocatable choice meshes");
                string output = Path.Combine(Application.streamingAssetsPath, "TCGStreamedMeshes"); Directory.CreateDirectory(output);
                var builds = audit.Eligible.Select(record => new AssetBundleBuild { assetBundleName = "mesh_" + record.guid,
                    assetNames = new[] { record.path }, addressableNames = new[] { "assets/tcgstreamedmesh/" + record.guid + ".asset" } }).ToArray();
                var bundles = BuildPipeline.BuildAssetBundles(output, builds, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows);
                if (!bundles) throw new InvalidOperationException("Character mesh bundles failed");
                var restoredCrcs = PreserveUsageFlags(output, audit.Eligible, Path.Combine(Root, "character-mesh-flag-request.json"));
                var manifest = new MeshManifest();
                foreach (var record in audit.Eligible)
                {
                    string name = "mesh_" + record.guid; uint crc;
                    if (bundles.GetAllDependencies(name).Length != 0 || !restoredCrcs.TryGetValue(Path.GetFullPath(Path.Combine(output, name)), out crc) || crc == 0)
                        throw new InvalidOperationException("Mesh bundle has external dependencies or unsupported CRC: " + name);
                    manifest.entries.Add(new MeshEntry { key = record.key, bundle = name, asset = "assets/tcgstreamedmesh/" + record.guid + ".asset", bytes = record.bytes, crc = crc });
                }
                // Only the identifier catalogue is used at runtime. Discard Unity's
                // now-stale build sidecars and aggregate hash manifest.
                foreach (string sidecar in Directory.GetFiles(output, "*.manifest")) File.Delete(sidecar);
                File.Delete(Path.Combine(output, Path.GetFileName(output)));
                manifest.Validate(); string json = JsonUtility.ToJson(manifest, true);
                File.WriteAllText(Path.Combine(output, "catalog.json"), json);
                Directory.CreateDirectory("Assets/Resources/TCGStreaming"); File.WriteAllText("Assets/Resources/TCGStreaming/MeshCatalog.txt", json);
                AssetDatabase.ImportAsset("Assets/Resources/TCGStreaming/MeshCatalog.txt", ImportAssetOptions.ForceSynchronousImport);
                var journal = new Journal { version = 2 };
                foreach (string prefab in audit.PrefabFiles)
                {
                    string saved = Path.Combine(Backups, prefab); Directory.CreateDirectory(Path.GetDirectoryName(saved));
                    if (!File.Exists(saved)) File.Copy(Path.Combine(Project, prefab), saved);
                    string originalHash = HashFile(Path.Combine(Project, prefab));
                    if (HashFile(saved) != originalHash) throw new InvalidOperationException("Original prefab backup does not match restored source: " + prefab);
                    journal.snapshots.Add(new PrefabSnapshot { path = prefab, guid = AssetDatabase.AssetPathToGUID(prefab), originalSha256 = originalHash });
                }
                const string destination = "Assets/TCGSwitchStreaming/StreamedMeshSources";
                Directory.CreateDirectory(destination); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (var record in audit.Eligible)
                    if (record.path.Split('/').Contains("Resources")) journal.moves.Add(new Move { guid = record.guid, original = record.path, current = destination + "/" + record.guid + ".asset" });
                SaveJournal(journal);
                foreach (var snapshot in journal.snapshots)
                {
                    RewritePrefab(snapshot.path, audit.Eligible);
                    snapshot.preparedSha256 = HashFile(Path.Combine(Project, snapshot.path)); SaveJournal(journal);
                }
                foreach (var move in journal.moves)
                {
                    string error = AssetDatabase.MoveAsset(move.original, move.current); if (error.Length != 0) throw new IOException(error);
                }
                AssetDatabase.SaveAssets();
                var currentRecords = audit.Eligible.Select(record => new MeshRecord { key = record.key, guid = record.guid,
                    path = AssetDatabase.GUIDToAssetPath(record.guid), name = record.name, bytes = record.bytes, usageFlags = record.usageFlags,
                    mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(record.guid)) }).ToList();
                var remaining = MeshReferenceAudit.Scan(currentRecords, new HashSet<string>(StringComparer.Ordinal));
                if (remaining.Retained.Count != 0) throw new InvalidOperationException("Unexplained eager mesh references remain: " + String.Join("; ", remaining.Retained.Values.ToArray()));
                report.status = "prepared";
                Debug.Log("[MESH-BUILD] Prepared " + report.bundleCount + " exact authored meshes; accounting estimate=" + report.estimatedMeshBytes);
            }
            catch (Exception error) { report.status = "failed"; report.error = error.ToString(); throw; }
            finally { File.WriteAllText(Path.Combine(Root, "character-mesh-build.json"), JsonUtility.ToJson(report, true)); }
        }
    }
}
