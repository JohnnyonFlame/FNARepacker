using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;

[assembly: IgnoresAccessChecksTo("FNA")]
namespace FNARepacker
{
    public struct TextureData
    {
        public SurfaceFormat format;
        public int width, height;
        public int levelCount;
        public List<byte[]> levelData;
        
        public static TextureData FromDDSStream(Stream stream)
        {
            throw new Exception("DDS format not implemented!");
        }

        public static TextureData FromStream(Stream stream)
        {
            TextureData textureData = new TextureData();
            if (stream.CanSeek && stream.Position == stream.Length)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            int len;
            IntPtr bytes = FNA3D.ReadImageStream(
                stream,
                out textureData.width,
                out textureData.height,
                out len
            );

            if (bytes == null || len <= 0)
                throw new Exception("Bad image data.");

            byte[] data = new byte[len];
            Marshal.Copy(bytes, data, 0, len);
            FNA3D.FNA3D_Image_Free(bytes);
            
            textureData.levelData.Add(data);
            return textureData;
        }
    }
}
