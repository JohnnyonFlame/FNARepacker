using System;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Ionic.Zlib;

[assembly: IgnoresAccessChecksTo("FNA")]
namespace FNARepacker
{
    class Program
    {
        public static volatile int count_done = 0;
        public static int total;
        public static string assetPath;

        [DllImport("astcUtil.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int convert_texture(int w, int h, int len, uint blk_w, uint blk_h, IntPtr in_tex, IntPtr out_tex);

        public static long GetASTCPayloadSize(int w, int h, uint blk_w, uint blk_h)
        {
            // 16 bytes per <x,y> block
            return 16 *
                ((w + blk_w - 1) / blk_w) * 
                ((h + blk_h - 1) / blk_h);
        }

        static IEnumerable<string> FilesInDirRecursive(string dir)
        {
            bool shouldSkip = false;

            foreach (var f in Directory.GetFiles(dir)) {
                // HACK FOR TMNT:SR - Skip folders of characters/enemies with
                // palettes so they don't get crunched up by ASTC
                if (f.EndsWith("Palette.zxnb"))
                    shouldSkip = true;
            }

            if (shouldSkip == false) {
                foreach (var f in Directory.GetFiles(dir)) {
                    // HACK FOR Salt and Sanctuary - don't encode the parchment
                    // texture because it's crunches the noise texture too hard.
                    if (f.EndsWith("gfx/parchment.xnb"))
                        continue;
                    yield return f;
                }
            }
            else
                Console.Out.WriteLine($"Skipped textures in {dir}.");

            foreach (var d in Directory.GetDirectories(dir))
                foreach (var f in FilesInDirRecursive(d))
                    yield return f;
        }

        unsafe static int encodeAstcXNB(Stream output, TextureData textureData, SurfaceFormat type)
        {
            uint blk_w, blk_h;
            switch (type) {
                case SurfaceFormat.Astc4x4EXT:
                    blk_w = blk_h = 4;
                    break;
                case SurfaceFormat.Astc5x5EXT:
                    blk_w = blk_h = 5;
                    break;
                case SurfaceFormat.Astc6x6EXT:
                    blk_w = blk_h = 6;
                    break;
                case SurfaceFormat.Astc8x8EXT:
                    blk_w = blk_h = 8;
                    break;
                default:
                    throw new Exception("Unknown ASTC type!"); 
            }

            BinaryWriter writer = new BinaryWriter(output);
            int ret = 0;
            int astcPayloadLength = (int)GetASTCPayloadSize(textureData.width, textureData.height, blk_w, blk_h);
            byte[] astcPayload = new byte[astcPayloadLength];

            fixed (byte *textureDataLevelPtr = textureData.levelData[0])
            fixed (byte *astcPayloadPtr = astcPayload)
            {
                ret = convert_texture(
                    textureData.width,
                    textureData.height,
                    astcPayloadLength,
                    blk_w, blk_h,
                    (IntPtr)textureDataLevelPtr,
                    (IntPtr)astcPayloadPtr);
            }

            if (ret != 1)
                throw new Exception("Failed to encode ASTC!");

            // See: XNB Format.docx
            writer.Write(ASCIIEncoding.ASCII.GetBytes("XNBw"));              /* format specifier and target plaftorm */
            writer.Write((byte)5);                                           /* XNB Format version */
            writer.Write((byte)0x00);                                        /* Flag bits (decompressed) */
            writer.Write((UInt32)0);                                         /* Compressed file size */
            // writer.Write((UInt32)0);                                      /* Decompressed data size */
            writer.Write7BitEncodedIntExt(1);                                /* Type reader count */
            writer.Write("Microsoft.Xna.Framework.Content.Texture2DReader"); /* Fully qualified name */
            writer.Write((Int32)0);                                          /* Reader version number (0) */
            writer.Write7BitEncodedIntExt(0);                                /* Shared resource count (none) */

            /* Primary asset data */
            writer.Write7BitEncodedIntExt(1);                                /* Type Id (1) ?? */
            writer.Write((Int32)type);                                       /* Surface format */
            writer.Write((Int32)textureData.width);                          /* Texture width */
            writer.Write((Int32)textureData.height);                         /* Texture height */
            writer.Write((Int32)1);                                          /* Mipmap level count, TODO:: More mipmap levels? */

            /* Mip levels, TODO:: More mipmap levels? */
            writer.Write((UInt32)astcPayloadLength);                         /* Mipmap level data length */
            writer.Write(astcPayload);

            // Update total file size.
            UInt32 size = (UInt32)output.Position;
            output.Seek(0x6, SeekOrigin.Begin);
            writer.Write(size);

            writer.Close();

            return ret;
        }

        // Normal XNB files
        unsafe static void ProcessXNBStream(Stream stream, string filepath)
        {
            TextureData textureData = AssetLoader.ReadTextureAsset(stream);
            using (FileStream output = File.Open(filepath + "_tmp", FileMode.Create))
            {
                float percent = (float)count_done / (float)total * 100.0f;
                Console.Out.WriteLine($"'{Path.GetRelativePath(assetPath, filepath)}' [{(int)percent}%] -> w: {textureData.width}, h: {textureData.height}");
                encodeAstcXNB(output, textureData, SurfaceFormat.Astc4x4EXT);
            }

            // Convertion has got to be atomic...
            File.Delete(filepath);
            File.Move(filepath + "_tmp", filepath);
        }

        // Paris Engine compressed assets, for Panzer Paladin.
        static void ProcessZXNBStream(Stream stream, string filepath)
        {
            var deflate = new DeflateStream(stream, CompressionMode.Decompress);
            TextureData textureData = AssetLoader.ReadTextureAsset(deflate);
            string newFilepath = Path.ChangeExtension(filepath, ".xnb");

            if (textureData.width < 128 || textureData.height < 128)
                throw new SkippedAssetFile($"Asset too small!");

            SurfaceFormat sf = SurfaceFormat.Astc4x4EXT;
            if (filepath.Contains("Cutscenes") ||
                filepath.Contains("Menu") || 
                filepath.Contains("Level") ||
                filepath.Contains("BG") ||
                filepath.Contains("Tileset"))
                sf = SurfaceFormat.Astc5x5EXT;

            if (File.Exists(newFilepath)) {
                Console.Out.WriteLine($"'{Path.GetRelativePath(assetPath, newFilepath)}' exists, deleting...");
                File.Delete(newFilepath);
            }

            try {
                using (FileStream output = File.Open(newFilepath, FileMode.Create))
                {
                    float percent = (float)count_done / (float)total * 100.0f;
                    Console.Out.WriteLine($"'{Path.GetRelativePath(assetPath, newFilepath)}' [{(int)percent}%] -> w: {textureData.width}, h: {textureData.height}");
                    encodeAstcXNB(output, textureData, sf);
                }
            }
            catch (Exception e)
            {
                // We failed to encode somehow.
                File.Delete(newFilepath);
                throw;
            }

            // Only delete the ZXNB file once done, so conv is atomic
            File.Delete(filepath);
        }

        static void Main(string[] args)
        {
            assetPath = args[0];
            var files = FilesInDirRecursive(assetPath).ToList();
            Program.total = files.Count;
            foreach (var file in files)
            {
                try {
                    switch (Path.GetExtension(file).ToLower())
                    {
                        case ".xnb": ProcessXNBStream(new FileStream(file, FileMode.Open), file); break;
                        case ".zxnb": ProcessZXNBStream(new FileStream(file, FileMode.Open), file); break;
                    }
                }
                catch (Exception e)
                {
                    if (e is SkippedAssetFile)
                        continue;

                    Console.Out.WriteLine($"File {file} failed to be re-encoded:");
                    Console.Out.WriteLine(e.ToString());
                }
                finally
                {
                    count_done++;
                }
            }
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }

        public string AssemblyName { get; }
    }
}