using System;
using UnityEngine;

namespace TCG.SwitchStreaming.Editor
{
    [Serializable]
    public sealed class MeshRecord
    {
        public string key, guid, path, name;
        public long bytes, usageFlags;
        [NonSerialized] public Mesh mesh;
    }
}
