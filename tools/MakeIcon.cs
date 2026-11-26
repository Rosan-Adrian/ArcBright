using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ArcBright.Tools
{
    // Writes assets\icon.ico (and a PNG for the README) from IconArt.
    internal static class MakeIcon
    {
        private static int Main(string[] args)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
            {
                using (Bitmap b = IconArt.Draw(s))
                {
                    images.Add(s >= 256 ? Png(b) : Dib(b));
                }
            }
            using (var w = new BinaryWriter(File.Create(args[0])))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim);
                    w.Write(dim);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images)
                {
                    w.Write(image);
                }
            }
            if (args.Length > 1)
            {
                using (Bitmap b = IconArt.Draw(256))
                {
                    b.Save(args[1], ImageFormat.Png);
                }
            }
            return 0;
        }

        private static byte[] Png(Bitmap b)
        {
            using (var ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static byte[] Dib(Bitmap b)
        {
            int s = b.Width;
            int maskStride = (s + 31) / 32 * 4;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(40);
                w.Write(s);
                w.Write(s * 2);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(0);
                w.Write(s * s * 4 + maskStride * s);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                for (int y = s - 1; y >= 0; y--)
                {
                    for (int x = 0; x < s; x++)
                    {
                        Color c = b.GetPixel(x, y);
                        w.Write(c.B);
                        w.Write(c.G);
                        w.Write(c.R);
                        w.Write(c.A);
                    }
                }
                for (int y = s - 1; y >= 0; y--)
                {
                    var row = new byte[maskStride];
                    for (int x = 0; x < s; x++)
                    {
                        if (b.GetPixel(x, y).A == 0)
                        {
                            row[x / 8] |= (byte)(0x80 >> (x % 8));
                        }
                    }
                    w.Write(row);
                }
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
