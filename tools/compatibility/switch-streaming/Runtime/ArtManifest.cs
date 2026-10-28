using System;
using System.Collections.Generic;

namespace TCG.SwitchStreaming
{
    [Serializable]
    public sealed class ArtEntry
    {
        public string key, bundle, asset;
        public long bytes;
        public uint crc;
    }

    [Serializable]
    public sealed class ArtManifest
    {
        public int version = 1;
        public string loadingResource = "TCGStreaming/Loading";
        public List<ArtEntry> entries = new List<ArtEntry>();

        public Dictionary<string, ArtEntry> Validate()
        {
            if (version != 1 || entries == null) throw new ArgumentException("Unsupported artwork manifest");
            var result = new Dictionary<string, ArtEntry>(StringComparer.Ordinal);
            var bundles = new Dictionary<string, ArtEntry>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.key) || e.bytes <= 0 || !SafeBundle(e.bundle) || !SafeAsset(e.asset))
                    throw new ArgumentException("Invalid local artwork entry");
                if (result.ContainsKey(e.key)) throw new ArgumentException("Duplicate artwork key: " + e.key);
                ArtEntry previous;
                if (bundles.TryGetValue(e.bundle, out previous) && (previous.bytes != e.bytes || previous.crc != e.crc))
                    throw new ArgumentException("Conflicting artwork bundle metadata");
                result.Add(e.key, e);
                bundles[e.bundle] = e;
            }
            return result;
        }
        static bool SafeBundle(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] == '.' || value[0] == '-') return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.') return false;
            return true;
        }
        static bool SafeAsset(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("assets/", StringComparison.Ordinal) || value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0) return false;
            foreach (string part in value.Split('/')) if (part == "." || part == ".." || part.Length == 0) return false;
            return true;
        }
    }
}
