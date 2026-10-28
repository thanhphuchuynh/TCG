using System;
using UnityEngine;

namespace TCG.SwitchStreaming.Editor
{
    [Serializable]
    public sealed class SurfaceRecord
    {
        public string key, property, textureGuid, texturePath, textureName;
        public long bytes;
        [NonSerialized] public Texture2D texture;
    }
}
