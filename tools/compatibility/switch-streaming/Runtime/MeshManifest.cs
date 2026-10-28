using System;
using System.Collections.Generic;

namespace TCG.SwitchStreaming
{
    [Serializable]
    public sealed class MeshEntry
    {
        public string key, bundle, asset;
        public long bytes;
        public uint crc;
    }

    [Serializable]
    public sealed class MeshManifest
    {
        public int version = 1;
        public List<MeshEntry> entries = new List<MeshEntry>();

        public Dictionary<string, MeshEntry> Validate()
        {
            if (version != 1 || entries == null) throw new ArgumentException("Unsupported mesh manifest");
            var result = new Dictionary<string, MeshEntry>(StringComparer.Ordinal);
            var bundles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || !MeshKey(entry.key) || !SafeBundle(entry.bundle) || !SafeAsset(entry.asset) || entry.bytes <= 0 || entry.crc == 0)
                    throw new ArgumentException("Invalid local mesh entry");
                if (result.ContainsKey(entry.key)) throw new ArgumentException("Duplicate mesh key: " + entry.key);
                if (!bundles.Add(entry.bundle)) throw new ArgumentException("Mesh bundle must have exactly one entry: " + entry.bundle);
                result.Add(entry.key, entry);
            }
            return result;
        }
        static bool MeshKey(string value)
        {
            if (value == null || value.Length != 37 || !value.StartsWith("mesh:", StringComparison.Ordinal)) return false;
            for (int i = 5; i < value.Length; i++) if (!(value[i] >= '0' && value[i] <= '9') && !(value[i] >= 'a' && value[i] <= 'f')) return false;
            return true;
        }
        static bool SafeBundle(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] == '.' || value[0] == '-') return false;
            foreach (char c in value) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.') return false;
            return true;
        }
        static bool SafeAsset(string value)
        {
            if (MeshKey(value)) return true;
            if (string.IsNullOrEmpty(value) || !value.StartsWith("assets/", StringComparison.Ordinal) || value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0) return false;
            foreach (string part in value.Split('/')) if (part.Length == 0 || part == "." || part == "..") return false;
            return true;
        }
    }
}
