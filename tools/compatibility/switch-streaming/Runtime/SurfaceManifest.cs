using System;
using System.Collections.Generic;
using System.Globalization;

namespace TCG.SwitchStreaming
{
    [Serializable]
    public sealed class SurfaceEntry
    {
        public string key, bundle, asset;
        public long bytes;
        public uint crc;
    }

    [Serializable]
    public sealed class SurfaceManifest
    {
        public int version = 1;
        public List<SurfaceEntry> entries = new List<SurfaceEntry>();

        public Dictionary<string, SurfaceEntry> Validate()
        {
            if (version != 1 || entries == null) throw new ArgumentException("Unsupported surface manifest");
            var result = new Dictionary<string, SurfaceEntry>(StringComparer.Ordinal);
            var bundles = new Dictionary<string, SurfaceEntry>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || !ValidKey(entry.key) || !ValidBundle(entry.bundle) || entry.asset != "assets/tcgstreamedsurface/" + entry.bundle.Substring(8) + ".asset" || entry.bytes <= 0 || entry.crc == 0)
                    throw new ArgumentException("Invalid local surface entry");
                if (result.ContainsKey(entry.key)) throw new ArgumentException("Duplicate surface key: " + entry.key);
                SurfaceEntry previous;
                if (bundles.TryGetValue(entry.bundle, out previous) && (previous.asset != entry.asset || previous.bytes != entry.bytes || previous.crc != entry.crc))
                    throw new ArgumentException("Conflicting surface bundle metadata: " + entry.bundle);
                result.Add(entry.key, entry); bundles[entry.bundle] = entry;
            }
            return result;
        }
        static bool ValidBundle(string value)
        {
            if (value == null || value.Length != 40 || !value.StartsWith("surface_", StringComparison.Ordinal)) return false;
            for (int i = 8; i < value.Length; i++) if (!(value[i] >= '0' && value[i] <= '9') && !(value[i] >= 'a' && value[i] <= 'f')) return false;
            return true;
        }
        static bool ValidKey(string value)
        {
            if (value == null) return false;
            var parts = value.Split(':'); int index;
            return parts.Length == 4 && parts[0] == "surface" && (parts[1] == "floor" || parts[1] == "wall" || parts[1] == "wallbar" || parts[1] == "ceiling")
                && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0 && index.ToString(CultureInfo.InvariantCulture) == parts[2]
                && (parts[3] == "mainTexture" || parts[3] == "roughnessMap" || parts[3] == "normalMap");
        }
    }
}
