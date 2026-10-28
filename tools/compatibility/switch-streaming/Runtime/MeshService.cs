using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    public sealed class LoadedMeshBundle
    {
        readonly AssetBundle bundle;
        public Mesh Mesh { get; private set; }
        public LoadedMeshBundle(AssetBundle bundle, Mesh mesh) { this.bundle = bundle; Mesh = mesh; }
        public void Unload() { Mesh = null; bundle.Unload(true); }
    }

    // Synchronous by design: original character setup must inspect the authored mesh.
    public sealed class MeshService : MonoBehaviour
    {
        static MeshService instance;
        Dictionary<string, MeshEntry> entries;
        readonly Dictionary<MeshBundleOwner, int> inactive = new Dictionary<MeshBundleOwner, int>();
        readonly HashSet<MeshBundleOwner> prepared = new HashSet<MeshBundleOwner>();
        string root;
        float nextReport;
        public ArtCache<LoadedMeshBundle> Cache { get; private set; }
        public string LastError { get; private set; }
        public int InactivePendingCount { get { return inactive.Count; } }
        public static MeshService Instance
        {
            get
            {
                if (!instance) new GameObject("Local character mesh cache").AddComponent<MeshService>();
                if (instance.entries == null)
                {
                    try
                    {
                        string folder = Path.Combine(Application.streamingAssetsPath, "TCGStreamedMeshes");
                        string json = File.ReadAllText(Path.Combine(folder, "catalog.json"));
                        var canonical = Resources.Load<TextAsset>("TCGStreaming/MeshCatalog");
                        if (!canonical || json != canonical.text) throw new InvalidDataException("Local mesh manifest does not match required resident catalogue");
                        instance.Initialize(folder, JsonUtility.FromJson<MeshManifest>(json), 128L * 1024 * 1024);
                    }
                    catch (Exception error) { instance.ReportError(error.Message); throw; }
                }
                return instance;
            }
        }
        void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject); Application.lowMemory += LowMemory;
        }
        public void Initialize(string folder, MeshManifest manifest, long budget)
        {
            if (entries != null) throw new InvalidOperationException("Mesh service already initialized");
            if (manifest == null) throw new ArgumentNullException("manifest");
            var validated = manifest.Validate();
            string fullRoot = Path.GetFullPath(folder);
            var cache = new ArtCache<LoadedMeshBundle>(budget, Load, value => value.Unload());
            entries = validated; root = fullRoot; Cache = cache;
            Debug.Log("[MESH-STREAMING] soft cache budget=" + budget + "; accounted mesh build estimates are not actual application memory/headroom; active pins win. Cold loads are synchronous.");
        }
        public ArtCache<LoadedMeshBundle>.Lease Acquire(string key)
        {
            MeshEntry entry;
            if (entries == null || key == null || !entries.TryGetValue(key, out entry))
            {
                string error = "Missing required mesh key: " + key; ReportError(error); throw new InvalidDataException(error);
            }
            var lease = Cache.Acquire(key, entry.bytes);
            if (lease.Error != null) ReportError(lease.Error);
            return lease;
        }
        void Load(string key, Action<LoadedMeshBundle, string> done)
        {
            AssetBundle bundle = null;
            try
            {
                var entry = entries[key]; string path = Path.Combine(root, entry.bundle);
                if (!File.Exists(path)) throw new FileNotFoundException("Missing local mesh bundle: " + entry.bundle);
                bundle = AssetBundle.LoadFromFile(path, entry.crc);
                if (!bundle) throw new InvalidDataException("Could not load local mesh bundle/CRC: " + entry.bundle);
                if (bundle.GetAllAssetNames().Length != 1) throw new InvalidDataException("Unexpected payload in mesh-only bundle: " + entry.bundle);
                // Inspect the complete payload, including subassets. No textures/materials/dependencies are accepted.
                var payload = bundle.LoadAllAssets<UnityEngine.Object>(); Mesh mesh = null;
                foreach (var asset in payload)
                {
                    var candidate = asset as Mesh;
                    if (!candidate || mesh) throw new InvalidDataException("Unexpected non-Mesh or multiple-Mesh payload: " + entry.bundle);
                    mesh = candidate;
                }
                if (!mesh || bundle.LoadAsset<Mesh>(entry.asset) != mesh) throw new InvalidDataException("Missing authored mesh address: " + entry.asset);
                done(new LoadedMeshBundle(bundle, mesh), null);
            }
            catch (Exception error)
            {
                if (bundle) bundle.Unload(true);
                done(null, error.Message);
            }
        }
        internal void Track(MeshBundleOwner owner)
        {
            prepared.Add(owner);
            if (!owner.isActiveAndEnabled) inactive[owner] = Time.frameCount + 1;
        }
        internal void Forget(MeshBundleOwner owner) { inactive.Remove(owner); prepared.Remove(owner); }
        void Update()
        {
            if (inactive.Count > 0)
                foreach (var pair in new List<KeyValuePair<MeshBundleOwner, int>>(inactive))
                {
                    if (Time.frameCount < pair.Value) continue;
                    var owner = pair.Key;
                    inactive.Remove(owner);
                    // A never-activated object's OnDestroy is not guaranteed; its managed lease still needs release.
                    if (!owner || !owner.isActiveAndEnabled) owner.ReleaseMeshes();
                }
            if (Cache != null && Time.unscaledTime >= nextReport)
            {
                nextReport = Time.unscaledTime + 5;
                Debug.Log("[MESH-STREAMING] estimated loaded=" + Cache.LoadedBytes + " pinned=" + Cache.PinnedBytes + " peak=" + Cache.PeakBytes + " pending=" + Cache.PendingCount);
            }
        }
        public void ReportError(string error)
        {
            if (LastError == error) return;
            LastError = error; Debug.LogError("[MESH-STREAMING] " + error);
        }
        void LowMemory() { if (Cache != null) Cache.TrimAllUnpinned(); }
        void OnDestroy()
        {
            Application.lowMemory -= LowMemory;
            foreach (var owner in new List<MeshBundleOwner>(prepared)) owner.ReleaseMeshes();
            if (Cache != null) Cache.TrimAllUnpinned();
            if (instance == this) instance = null;
        }
        void OnGUI()
        {
            if (LastError != null) GUI.Box(new Rect(30, 120, Math.Min(Screen.width - 60, 600), 80), "Character mesh could not be loaded.\nCheck the required local game files.");
        }
    }
}
