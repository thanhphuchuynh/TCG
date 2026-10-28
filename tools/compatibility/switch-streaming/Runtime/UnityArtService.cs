using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    public sealed class LoadedArtBundle
    {
        readonly AssetBundle bundle;
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        public LoadedArtBundle(AssetBundle bundle) { this.bundle = bundle; }
        public Sprite Sprite(string asset)
        {
            Sprite sprite;
            if (!sprites.TryGetValue(asset, out sprite))
            {
                sprite = bundle.LoadAsset<Sprite>(asset);
                if (!sprite) throw new InvalidDataException("Missing authored sprite: " + asset);
                sprites.Add(asset, sprite);
            }
            return sprite;
        }
        public bool Owns(Sprite sprite) { return sprite && sprites.ContainsValue(sprite); }
        public void Unload() { sprites.Clear(); bundle.Unload(true); }
    }

    public sealed class UnityArtService : MonoBehaviour
    {
        static UnityArtService instance;
        Dictionary<string, ArtEntry> entries;
        Dictionary<string, ArtEntry> bundles;
        string root;
        float nextReport;
        public ArtCache<LoadedArtBundle> Cache { get; private set; }
        public Sprite Placeholder { get; private set; }
        public string LastError { get; private set; }
        public bool TraceOwnership;
        public void Trace(string message) { if (TraceOwnership) Debug.Log("[STREAMING-OWNER] frame=" + Time.frameCount + " " + message); }
        public static bool Available
        {
            get { return instance && instance.entries != null || File.Exists(Path.Combine(Application.streamingAssetsPath, "TCGStreamedArt/catalog.json")); }
        }
        public static UnityArtService Instance
        {
            get
            {
                if (!instance) new GameObject("Local artwork cache").AddComponent<UnityArtService>();
                if (instance.entries == null)
                {
                    string path = Path.Combine(Application.streamingAssetsPath, "TCGStreamedArt");
                    var manifest = JsonUtility.FromJson<ArtManifest>(File.ReadAllText(Path.Combine(path, "catalog.json")));
                    var placeholder = Resources.Load<Sprite>(manifest.loadingResource);
                    instance.Initialize(path, manifest, placeholder, 128L * 1024 * 1024);
                }
                return instance;
            }
        }
        void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.lowMemory += OnLowMemory;
        }
        public void Initialize(string folder, ArtManifest manifest, Sprite placeholder, long budget)
        {
            if (entries != null) throw new InvalidOperationException("Artwork service already initialized");
            if (!placeholder) throw new InvalidDataException("Missing resident artwork loading sprite");
            entries = manifest.Validate();
            bundles = new Dictionary<string, ArtEntry>(StringComparer.Ordinal);
            foreach (var entry in entries.Values) bundles[entry.bundle] = entry;
            root = Path.GetFullPath(folder);
            Placeholder = placeholder;
            Cache = new ArtCache<LoadedArtBundle>(budget, (key, done) => StartCoroutine(Load(key, done)), value => value.Unload());
        }
        public bool Find(string key, out ArtEntry entry) { return entries.TryGetValue(key, out entry); }
        public ArtCache<LoadedArtBundle>.Lease Acquire(ArtEntry entry, Action<ArtCache<LoadedArtBundle>.Lease> changed = null)
        { return Cache.Acquire(entry.bundle, entry.bytes, changed); }
        IEnumerator Load(string key, Action<LoadedArtBundle, string> done)
        {
            var entry = bundles[key];
            string path = Path.Combine(root, key);
            if (!File.Exists(path)) { done(null, "Missing local artwork bundle: " + key); yield break; }
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(path, entry.crc);
            if (request == null) { done(null, "Could not start artwork bundle load: " + key); yield break; }
            yield return request;
            var bundle = request.assetBundle;
            if (!bundle) { done(null, "Could not load local artwork bundle: " + key); yield break; }
            var sprites = bundle.LoadAllAssetsAsync<Sprite>();
            yield return sprites;
            done(new LoadedArtBundle(bundle), null);
        }
        public void ReportError(string error)
        {
            if (LastError == error) return;
            LastError = error;
            Debug.LogError("[STREAMING] " + error);
        }
        void Update()
        {
            CardArtSession.Tick();
            if (Cache != null && Time.unscaledTime >= nextReport)
            {
                nextReport = Time.unscaledTime + 5;
                Debug.Log("[STREAMING] loaded=" + Cache.LoadedBytes + " pinned=" + Cache.PinnedBytes + " pending=" + Cache.PendingCount + " peak=" + Cache.PeakBytes);
            }
        }
        void OnLowMemory() { if (Cache != null) Cache.TrimAllUnpinned(); }
        void OnDestroy()
        {
            Application.lowMemory -= OnLowMemory;
            if (instance == this) instance = null;
        }
        void OnGUI()
        {
            if (LastError != null) GUI.Box(new Rect(30, 30, Math.Min(Screen.width - 60, 600), 80), "Card artwork could not be loaded.\nCheck the local game files.");
        }
    }
}
