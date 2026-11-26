using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ArcBright
{
    // Draws the app icon: a warm sun on a dark rounded square.
    internal static class IconArt
    {
        public static Bitmap Draw(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float r = size * 0.22f;
                using (GraphicsPath path = RoundedRect(new RectangleF(0, 0, size, size), r))
                using (var bg = new SolidBrush(Color.FromArgb(255, 22, 27, 34)))
                {
                    g.FillPath(bg, path);
                }

                float c = size / 2f;
                Color sun = Color.FromArgb(255, 255, 183, 3);
                float rayWidth = Math.Max(1.5f, size * 0.075f);
                using (var pen = new Pen(sun, rayWidth))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    float inner = size * 0.30f;
                    float outer = size * 0.40f;
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(pen,
                            c + (float)Math.Cos(a) * inner, c + (float)Math.Sin(a) * inner,
                            c + (float)Math.Cos(a) * outer, c + (float)Math.Sin(a) * outer);
                    }
                }
                float core = size * 0.19f;
                using (var brush = new SolidBrush(sun))
                {
                    g.FillEllipse(brush, c - core, c - core, core * 2, core * 2);
                }
            }
            return bmp;
        }

        public static Icon MakeIcon(int size)
        {
            using (Bitmap bmp = Draw(size))
            {
                IntPtr handle = bmp.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    Native.DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
