using System;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    public static class ArtImageSize
    {
        public static long Of(Texture2D texture)
        {
            int block = 0, pixel = 0;
            switch (texture.format)
            {
                case TextureFormat.DXT1: block = 8; break;
                case TextureFormat.DXT5: case TextureFormat.BC7: case TextureFormat.BC6H: block = 16; break;
                case TextureFormat.RGBA32: case TextureFormat.ARGB32: case TextureFormat.BGRA32: pixel = 4; break;
                case TextureFormat.RGB24: pixel = 3; break;
                case TextureFormat.Alpha8: case TextureFormat.R8: pixel = 1; break;
                case TextureFormat.RGB565: case TextureFormat.ARGB4444: case TextureFormat.RGBA4444: pixel = 2; break;
                default: throw new InvalidOperationException("Unaccounted artwork format: " + texture.format);
            }
            int width = texture.width, height = texture.height; long bytes = 0;
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                bytes += block > 0 ? (long)((width + 3) / 4) * ((height + 3) / 4) * block : (long)width * height * pixel;
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            }
            return bytes;
        }
    }
}
