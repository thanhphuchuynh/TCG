using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    public sealed class LoadedSurfaceBundle
    {
        readonly AssetBundle bundle;
        public Texture2D Texture { get; private set; }
        public LoadedSurfaceBundle(AssetBundle bundle, Texture2D texture) { this.bundle = bundle; Texture = texture; }
        public void Unload() { Texture = null; bundle.Unload(true); }
    }

    public sealed class SurfaceTextureService : MonoBehaviour
    {
        static SurfaceTextureService instance;
        Dictionary<string, SurfaceEntry> entries, bundles;
        string root;
        readonly Dictionary<Texture, string> loadedKeys = new Dictionary<Texture, string>();
        public ArtCache<LoadedSurfaceBundle> Cache { get; private set; }
        public string LastError { get; private set; }
        internal static SurfaceTextureService Host
        {
            get { if (!instance) new GameObject("Local surface texture cache").AddComponent<SurfaceTextureService>(); return instance; }
        }
        public static SurfaceTextureService Instance
        {
            get
            {
                var service = Host;
                if (service.entries == null)
                {
                    try
                    {
                        string folder = Path.Combine(Application.streamingAssetsPath, "TCGStreamedSurfaces");
                        string json = File.ReadAllText(Path.Combine(folder, "catalog.json"));
                        var canonical = Resources.Load<TextAsset>("TCGStreaming/SurfaceCatalog");
                        if (!canonical || canonical.text != json) throw new InvalidDataException("Local surface manifest does not match required resident catalogue");
                        service.Initialize(folder, JsonUtility.FromJson<SurfaceManifest>(json), 64L * 1024 * 1024);
                    }
                    catch (Exception error) { service.ReportError(error.Message); throw; }
                }
                return service;
            }
        }
        void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject); Application.lowMemory += LowMemory;
        }
        public void Initialize(string folder, SurfaceManifest manifest, long budget)
        {
            if (entries != null) throw new InvalidOperationException("Surface texture service already initialized");
            if (manifest == null) throw new ArgumentNullException("manifest");
            var validated = manifest.Validate();
            var byBundle = new Dictionary<string, SurfaceEntry>(StringComparer.Ordinal);
            foreach (var pair in validated) byBundle[pair.Value.bundle] = pair.Value;
            string fullRoot = Path.GetFullPath(folder);
            var cache = new ArtCache<LoadedSurfaceBundle>(budget, Load, value =>
            {
                if (value.Texture) loadedKeys.Remove(value.Texture);
                value.Unload();
            });
            entries = validated; bundles = byBundle; root = fullRoot; Cache = cache;
            LastError = null;
            Debug.Log("[SURFACE-STREAMING] soft cache budget=" + budget + "; active pins win; accounted image bytes are not application headroom. Cold loads are synchronous.");
        }
        public bool Contains(string key) { return key != null && entries != null && entries.ContainsKey(key); }
        public ArtCache<LoadedSurfaceBundle>.Lease Acquire(string key)
        {
            SurfaceEntry entry;
            if (key == null || entries == null || !entries.TryGetValue(key, out entry))
            { string message = "Missing required surface key: " + key; ReportError(message); throw new InvalidDataException(message); }
            var lease = Cache.Acquire(entry.bundle, entry.bytes);
            if (!lease.Ready || lease.Error != null || lease.Value == null)
            {
                string message = lease.Error ?? "Required surface texture did not load synchronously: " + key;
                lease.Dispose(); ReportError(message); throw new InvalidDataException(message);
            }
            return lease;
        }
        internal ArtCache<LoadedSurfaceBundle>.Lease Borrow(Texture texture)
        {
            string key;
            return loadedKeys.TryGetValue(texture, out key) ? Acquire(key) : null;
        }
        void Load(string key, Action<LoadedSurfaceBundle, string> done)
        {
            AssetBundle bundle = null;
            try
            {
                var entry = bundles[key]; string path = Path.Combine(root, entry.bundle);
                if (!File.Exists(path)) throw new FileNotFoundException("Missing local surface bundle: " + entry.bundle);
                bundle = AssetBundle.LoadFromFile(path, entry.crc);
                if (!bundle) throw new InvalidDataException("Could not load local surface bundle/CRC: " + entry.bundle);
                var names = bundle.GetAllAssetNames();
                if (bundle.isStreamedSceneAssetBundle || names.Length != 1 || names[0] != entry.asset)
                    throw new InvalidDataException("Unexpected payload/address in surface-only bundle: " + entry.bundle);
                var payload = bundle.LoadAllAssets<UnityEngine.Object>(); Texture2D texture = null;
                foreach (var asset in payload)
                {
                    var candidate = asset as Texture2D;
                    if (!candidate || texture) throw new InvalidDataException("Unexpected non-Texture2D or multiple-Texture2D payload: " + entry.bundle);
                    texture = candidate;
                }
                if (!texture || bundle.LoadAsset<Texture2D>(entry.asset) != texture) throw new InvalidDataException("Missing original surface texture: " + entry.asset);
                if (ArtImageSize.Of(texture) != entry.bytes) throw new InvalidDataException("Surface image byte count does not match catalogue: " + entry.bundle);
                loadedKeys.Add(texture, entry.key);
                done(new LoadedSurfaceBundle(bundle, texture), null);
            }
            catch (Exception error) { if (bundle) bundle.Unload(true); done(null, error.Message); }
        }
        public void ReportError(string error)
        {
            if (LastError == error) return;
            LastError = error; Debug.LogError("[SURFACE-STREAMING] " + error);
        }
        void Update() { SurfaceTextures.ReleaseDestroyedOwners(); }
        void LowMemory() { if (Cache != null) Cache.TrimAllUnpinned(); }
        void OnDestroy()
        {
            Application.lowMemory -= LowMemory;
            if (instance != this) return;
            SurfaceTextures.ReleaseAll();
            if (Cache != null) Cache.TrimAllUnpinned();
            instance = null;
        }
        void OnGUI()
        {
            if (LastError != null) GUI.Box(new Rect(30, 210, Math.Min(Screen.width - 60, 600), 80), "Shop surface texture could not be loaded.\nCheck the required local game files.");
        }
    }
}
