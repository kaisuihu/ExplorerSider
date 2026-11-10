using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerSider.Services;

/// <summary>
/// 代码原创绘制的四叶草图标（无图片素材依赖）。
/// 矢量绘制：四片心形叶对角布局 + 绿色渐变 + 弯茎。
/// 同一逻辑用于：exe 图标（多尺寸 .ico，--genicon 生成）与托盘 HICON。
/// </summary>
public static class CloverIcon
{
    // 设计坐标系 100x100
    private const double Cx = 50, Cy = 44;   // 四叶草中心（下方留茎空间）
    private const double Leaf = 36;          // 心形单边长度

    /// <summary>标准心形（单位坐标，尖在 (0.5,1)，两瓣朝上）</summary>
    private const string HeartPath =
        "M0.5,1 C0.5,1 0,0.62 0,0.30 C0,0.02 0.26,-0.12 0.5,0.08 C0.74,-0.12 1,0.02 1,0.30 C1,0.62 0.5,1 0.5,1 Z";

    /// <summary>渲染指定像素尺寸的四叶草图</summary>
    public static BitmapSource Render(int size)
    {
        var geo = new GeometryGroup { FillRule = FillRule.Nonzero };

        // 四片叶：右上45° / 右下135° / 左下225° / 左上315°，心形尖端置于中心
        foreach (var deg in new[] { 45.0, 135.0, 225.0, 315.0 })
        {
            var heart = Geometry.Parse(HeartPath).Clone(); // Parse 结果可能已冻结，Clone 后才可设 Transform
            // 注意：TransformGroup 中后声明的变换先应用于点
            heart.Transform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(Leaf, Leaf),                         // 1. 缩放到叶片大小
                    new TranslateTransform(-0.5 * Leaf, -1.0 * Leaf),       // 2. 心形尖端移到原点
                    new RotateTransform(deg),                               // 3. 瓣朝对角方向
                    new TranslateTransform(Cx, Cy)                          // 4. 尖端落在四叶草中心
                }
            };
            geo.Children.Add(heart);
        }

        var stem = Geometry.Parse($"M {Cx},{Cy + 4} Q {Cx + 13},{Cy + 18} {Cx + 7},{Cy + 44}");

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 100.0, size / 100.0));

            // 茎
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x0E)), 5.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            }, stem);

            // 叶：绿色渐变 + 细描边（Absolute 模式：相对下方设计坐标系 100x100）
            var grad = new LinearGradientBrush(
                Color.FromRgb(0x9B, 0xE0, 0x5A), Color.FromRgb(0x37, 0x8E, 0x1B),
                new Point(8, 4), new Point(92, 96))
            {
                MappingMode = BrushMappingMode.Absolute
            };
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x76, 0x0D)), 1.6);
            dc.DrawGeometry(grad, pen, geo);

            // 中心高光小点
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(200, 0xFF, 0xFF, 0xFF)), null,
                new Point(Cx, Cy), 3.2, 3.2);

            dc.Pop();
        }
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>渲染为 PNG 字节</summary>
    public static byte[] RenderPng(int size)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Render(size)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 生成多尺寸 .ico（PNG 容器格式，Vista+ 支持）。
    /// 结构：ICONDIR(6B) + N×ICONDIRENTRY(16B) + PNG 数据。
    /// </summary>
    public static void WriteIco(string file, int[] sizes)
    {
        using var fs = new FileStream(file, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write((ushort)0);      // reserved
        bw.Write((ushort)1);      // type: icon
        bw.Write((ushort)sizes.Length);

        var pngs = sizes.Select(RenderPng).ToArray();
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte w = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
            bw.Write(w);                        // width
            bw.Write(w);                        // height
            bw.Write((byte)0);                  // palette
            bw.Write((byte)0);                  // reserved
            bw.Write((ushort)1);                // color planes
            bw.Write((ushort)32);               // bits per pixel
            bw.Write((uint)pngs[i].Length);     // data size
            bw.Write((uint)offset);             // data offset
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) bw.Write(png);
    }

    /// <summary>渲染为 System.Drawing.Icon（托盘 HICON 用）。调用方负责 Dispose。</summary>
    public static System.Drawing.Icon ToIcon(int size = 32)
    {
        var bmp = Render(size);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(ms);
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
