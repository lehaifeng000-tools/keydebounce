using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class BuildIcon
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private static void Main(string[] args)
    {
        string directory = args[0];
        using (Bitmap preview = Draw(128))
            preview.Save(Path.Combine(directory, "KeyDebounce-icon.png"), ImageFormat.Png);

        var images = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++)
        {
            using (Bitmap bitmap = Draw(Sizes[i]))
                images[i] = Sizes[i] >= 128 ? PngBytes(bitmap) : DibBytes(bitmap);
        }

        using (var icon = new BinaryWriter(File.Create(Path.Combine(directory, "KeyDebounce.ico"))))
        {
            icon.Write((ushort)0);
            icon.Write((ushort)1);
            icon.Write((ushort)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                byte size = Sizes[i] == 256 ? (byte)0 : (byte)Sizes[i];
                icon.Write(size);
                icon.Write(size);
                icon.Write((ushort)0);
                icon.Write((ushort)1);
                icon.Write((ushort)32);
                icon.Write(images[i].Length);
                icon.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] frame in images)
                icon.Write(frame);
        }
    }

    private static Bitmap Draw(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics canvas = Graphics.FromImage(bitmap))
        using (var tile = new GraphicsPath())
        using (var face = new GraphicsPath())
        using (var wall = new GraphicsPath())
        using (var tileFill = new SolidBrush(Color.FromArgb(24, 39, 69)))
        using (var faceFill = new SolidBrush(Color.FromArgb(46, 70, 114)))
        using (var wallFill = new SolidBrush(Color.FromArgb(29, 48, 83)))
        using (var outline = new Pen(Color.FromArgb(241, 247, 255), 3.5f))
        using (var legend = new Pen(Color.FromArgb(132, 235, 223), 3.8f))
        {
            canvas.Clear(Color.Transparent);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.ScaleTransform(size / 64f, size / 64f);
            tile.AddArc(2, 2, 28, 28, 180, 90);
            tile.AddArc(34, 2, 28, 28, 270, 90);
            tile.AddArc(34, 34, 28, 28, 0, 90);
            tile.AddArc(2, 34, 28, 28, 90, 90);
            tile.CloseFigure();
            canvas.FillPath(tileFill, tile);

            wall.AddLine(12, 35, 12, 43);
            wall.AddBezier(12, 43, 12, 47, 15, 50, 20, 50);
            wall.AddLine(20, 50, 44, 50);
            wall.AddBezier(44, 50, 49, 50, 52, 47, 52, 43);
            wall.AddLine(52, 43, 52, 35);
            wall.CloseFigure();
            canvas.FillPath(wallFill, wall);
            outline.LineJoin = LineJoin.Round;
            canvas.DrawPath(outline, wall);

            face.AddLine(23, 14, 41, 14);
            face.AddBezier(41, 14, 44, 14, 46, 16, 47, 19);
            face.AddLine(47, 19, 51.5f, 33);
            face.AddBezier(51.5f, 33, 53, 37, 50, 41, 45, 41);
            face.AddLine(45, 41, 19, 41);
            face.AddBezier(19, 41, 14, 41, 11, 37, 12.5f, 33);
            face.AddLine(12.5f, 33, 17, 19);
            face.AddBezier(17, 19, 18, 16, 20, 14, 23, 14);
            face.CloseFigure();
            canvas.FillPath(faceFill, face);
            canvas.DrawPath(outline, face);

            legend.StartCap = LineCap.Round;
            legend.EndCap = LineCap.Round;
            canvas.DrawLine(legend, 27, 27, 37, 27);
        }
        return bitmap;
    }

    private static byte[] PngBytes(Bitmap bitmap)
    {
        using (var bytes = new MemoryStream())
        {
            bitmap.Save(bytes, ImageFormat.Png);
            return bytes.ToArray();
        }
    }

    private static byte[] DibBytes(Bitmap bitmap)
    {
        int size = bitmap.Width;
        int maskStride = (size + 31) / 32 * 4;
        using (var bytes = new MemoryStream())
        using (var dib = new BinaryWriter(bytes))
        {
            dib.Write(40);
            dib.Write(size);
            dib.Write(size * 2); // The color bitmap and transparency mask each occupy one height.
            dib.Write((ushort)1);
            dib.Write((ushort)32);
            for (int i = 0; i < 6; i++)
                dib.Write(0);

            var mask = new byte[maskStride * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color pixel = bitmap.GetPixel(x, size - 1 - y);
                    dib.Write(pixel.ToArgb()); // Little endian BGRA.
                    if (pixel.A == 0)
                        mask[y * maskStride + x / 8] |= (byte)(128 >> (x % 8));
                }
            dib.Write(mask);
            return bytes.ToArray();
        }
    }
}
