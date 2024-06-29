using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.IO;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS0122

[assembly: IgnoresAccessChecksTo("FNA")]
namespace FNARepacker
{
    public class SkippedAssetFile: Exception {
        public SkippedAssetFile(string message): base(message) { }
    }
    
    public class AssetLoader
    {
        public static TextureData ReadTextureAsset(Stream stream)
        {
            byte version;
            Stream dataStream;
            BinaryReader dataReader;
            TextureData result;
            byte[] xnbHeader = new byte[4];
            stream.Read(xnbHeader, 0, xnbHeader.Length);
            
            if (xnbHeader[0] == 'X' &&
                xnbHeader[1] == 'N' &&
                xnbHeader[2] == 'B' &&
                ContentManager.targetPlatformIdentifiers.Contains((char) xnbHeader[3]) )
            {
                // This is really a XNB file, decode it 
                dataStream = GetStreamFromXnb(ref stream, new BinaryReader(stream), (char) xnbHeader[3], out version);
                dataReader = new BinaryReader(dataStream);
                result = GetXNBTexture(version, xnbHeader[3], dataReader, dataStream);

                // GetStreamFromXNB might return the same stream if it's not compressed, so beware when closing it lest
                // we'll end up closing the stream twice and throwing an Exception.
                if (dataStream != stream)
                    dataStream.Close();
            }
            else
            {
                // Oops, not XNB, reset and retry
                stream.Seek(0, SeekOrigin.Begin);
                dataReader = new BinaryReader(stream);

                stream.Read(xnbHeader, 0, xnbHeader.Length);
                stream.Seek(0, SeekOrigin.Begin);
                if (xnbHeader[0] == 'D' &&
                    xnbHeader[1] == 'D' &&
                    xnbHeader[2] == 'S' &&
                    xnbHeader[3] == ' '	)
                {
                    // This is DDS, use it
                    result = TextureData.FromDDSStream(stream);
                }
                else
                {
                    // This is unknown, maybe FNA3D can decode it?
                    result = TextureData.FromStream(stream);
                }
            }

            stream.Close();
            return result;
        }
    
        public class ColorSwizzle
        {
            private int bytes;
            private UInt32 rmask, gmask, bmask, amask;
            private int rshift, gshift, bshift, ashift;

            static void getMaskShift(out UInt32 mask, out int shift, UInt32 absMask)
            {
                int i = 0;
                for (; (absMask & 1) == 0; absMask >>= 1, i++);

                shift = i;
                mask = absMask;
            }

            public ColorSwizzle(UInt32 rmask, UInt32 gmask, UInt32 bmask, UInt32 amask = 0x00)
            {
                getMaskShift(out this.rmask, out this.rshift, rmask);
                getMaskShift(out this.gmask, out this.gshift, gmask);
                getMaskShift(out this.bmask, out this.bshift, bmask);
                getMaskShift(out this.amask, out this.ashift, amask);
                if (((rmask | gmask | bmask | amask) & 0xFFFF0000) > 0)
                    bytes = 4;
                else
                    bytes = 2;
            }

            public void Swizzle(BinaryReader br, out byte r, out byte g, out byte b, out byte a)
            {
                UInt32 col = (bytes == 4) ? br.ReadUInt32() : br.ReadUInt16();
                
                r = (byte)((col >> rshift) & rmask);
                g = (byte)((col >> gshift) & gmask);
                b = (byte)((col >> bshift) & bmask);
                a = (byte)((col >> ashift) & amask);
            }
        }

        static public byte[] convertSurfaceFormat(byte[] data, int width, int height, SurfaceFormat fmt)
        {
            Console.Out.WriteLine($"Running convertion from {fmt.ToString()} to RGBA8");
            MemoryStream orig = new MemoryStream(data, writable: false);
            MemoryStream result = new MemoryStream(width * height * sizeof(UInt32));
            BinaryReader br = new BinaryReader(orig);
            BinaryWriter bw = new BinaryWriter(result);
            ColorSwizzle swizzle;
            switch(fmt)
            {
                case SurfaceFormat.Bgr565:   swizzle = new ColorSwizzle(0x1F,     0x3F << 5, 0x1F << 11/* */); break;
                case SurfaceFormat.Bgra4444: swizzle = new ColorSwizzle(0xF << 4, 0xF  << 8, 0xF  << 12, 0xF); break;
                case SurfaceFormat.Bgra5551: swizzle = new ColorSwizzle(0xF << 1, 0xF  << 5, 0xF  << 9,  0x1); break;
                default: throw new Exception($"Unsupported format {fmt.ToString()}");
            }


            for (int i = 0; i < width * height; i++)
            {
                byte r, g, b, a;
                
                swizzle.Swizzle(br, out r, out g, out b, out a);
                bw.Write((UInt32)((r << 24) | (g << 16) | (b << 8) | a));
            }

            return result.ToArray();
        }

        public static TextureData GetXNBTexture(int version, byte platform, BinaryReader reader, Stream stream)
        {
            TextureData result = new TextureData();
            SurfaceFormat surfaceFormat;

            int readerCount = reader.Read7BitEncodedIntExt();
            string readerName = reader.ReadString();
            int readerVersionNo = reader.ReadInt32();
            if (readerCount > 1 || !readerName.Contains("Microsoft.Xna.Framework.Content.Texture2DReader"))
            {
                throw new SkippedAssetFile($"Not a Texture2D, got {readerName.Split(new char[]{ ',' })[0]} instead!");
            }

            int sharedResourceCount = reader.Read7BitEncodedIntExt();
            if (sharedResourceCount > 1)
            {
                throw new Exception("Too many shared resources.");
            }

            int typeId = reader.Read7BitEncodedIntExt();

            if (version < 5)
            {
                int legacyFormat = reader.ReadInt32();
                switch(legacyFormat)
                {
                    case  1: surfaceFormat = SurfaceFormat.ColorBgraEXT; break;
                    case 28: surfaceFormat = SurfaceFormat.Dxt1; break;
                    case 30: surfaceFormat = SurfaceFormat.Dxt3; break;
                    case 32: surfaceFormat = SurfaceFormat.Dxt5; break;
                    default: throw new Exception("Unsupported legacyFormat!");
                }
            }
            else
            {
                surfaceFormat = (SurfaceFormat)reader.ReadInt32();
            }

            if (!Enum.IsDefined(typeof(SurfaceFormat), surfaceFormat))
            {
                throw new Exception($"Unknown surfaceFormat {surfaceFormat}!");
            }

            switch (surfaceFormat)
            {
                case SurfaceFormat.Astc4x4EXT:
                case SurfaceFormat.Astc5x5EXT:
                case SurfaceFormat.Astc6x6EXT:
                case SurfaceFormat.Astc8x8EXT:
                    throw new SkippedAssetFile("Texture already encoded.");
                default:
                    break;
            }

            result.format = SurfaceFormat.Color;
            result.width = reader.ReadInt32();
            result.height = reader.ReadInt32();
            result.levelCount = reader.ReadInt32();
            result.levelData = new List<byte[]>();

            // Refer to FNA's Texture2DReader.cs if you want to implement htis.
            if (platform == 'x')
                throw new Exception("XBox360 texture reader not implemented.");

            MemoryStream textureData = new MemoryStream();
            for (int i = 0; i < result.levelCount; i++)
            {
                int levelDataSizeInBytes = reader.ReadInt32();
                int levelWidth = result.width >> i;
                int levelHeight = result.height >> i;

                byte[] levelData = reader.ReadBytes(levelDataSizeInBytes);
                switch(surfaceFormat)
                {
                case SurfaceFormat.Color: break; // Already Converted
                case SurfaceFormat.Dxt1: levelData = DxtUtil.DecompressDxt1(levelData, levelWidth, levelHeight); break;
                case SurfaceFormat.Dxt3: levelData = DxtUtil.DecompressDxt3(levelData, levelWidth, levelHeight); break;
                case SurfaceFormat.Dxt5: levelData = DxtUtil.DecompressDxt5(levelData, levelWidth, levelHeight); break;
                default: levelData = convertSurfaceFormat(levelData, levelWidth, levelHeight, surfaceFormat); break;
                }
                                
                result.levelData.Add(levelData);
            }

            return result;
        }

        public static Stream GetStreamFromXnb(ref Stream stream, BinaryReader xnbReader, char platform, out byte version)
        {
            version = xnbReader.ReadByte();
            byte flags = xnbReader.ReadByte();
            bool compressed = (flags & 0x80) != 0;

            if (version != 5 && version != 4)
            {
                throw new Exception("Invalid XNB version");
            }

            int xnbLength = xnbReader.ReadInt32();
            if (compressed)
            {
                int compressedSize = xnbLength - 14;
                int decompressedSize = xnbReader.ReadInt32();

                MemoryStream decompressedStream = new MemoryStream(
                    new byte[decompressedSize],
                    0,
                    decompressedSize,
                    true,
                    true
                );

                MemoryStream compressedStream = new MemoryStream(
                    new byte[compressedSize],
                    0,
                    compressedSize,
                    true,
                    true
                );
                stream.Read(compressedStream.GetBuffer(), 0, compressedSize);

                LzxDecoder dec = new LzxDecoder(16);
                int decodedBytes = 0;
                long pos = 0;

                while (pos < compressedSize)
                {
                    int hi = compressedStream.ReadByte();
                    int lo = compressedStream.ReadByte();
                    int block_size = (hi << 8) | lo;
                    int frame_size = 0x8000;

                    if (hi == 0xFF)
                    {
                        hi = lo;
                        lo = (byte) compressedStream.ReadByte();
                        frame_size = (hi << 8) | lo;
                        hi = (byte) compressedStream.ReadByte();
                        lo = (byte) compressedStream.ReadByte();
                        block_size = (hi << 8) | lo;
                        pos += 5;
                    }
                    else
                    {
                        pos += 2;
                    }

                    if (block_size == 0 || frame_size == 0)
                    {
                        break;
                    }
                    dec.Decompress(compressedStream, block_size, decompressedStream, frame_size);
                    pos += block_size;
                    decodedBytes += frame_size;
                    compressedStream.Seek(pos, SeekOrigin.Begin);
                }
                if (decompressedStream.Position != decompressedSize)
                {
                    throw new Exception("Asset Decompression failed.");
                }

                decompressedStream.Seek(0, SeekOrigin.Begin);
                return decompressedStream;
            }
            else
            {
                return stream;
            }
        }
    }
}
