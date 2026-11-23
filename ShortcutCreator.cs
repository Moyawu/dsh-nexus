using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using Media = System.Windows.Media;

namespace DshTrayLauncher;

public static class ShortcutCreator
{
    public const string ShortcutFileName = "DeepSeek Harness.lnk";

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static string GetDesktopShortcutPath()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desktop, ShortcutFileName);
    }

    public static string CreateDesktopShortcut()
    {
        string exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "dsh-tray-launcher.exe");

        string workingDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        string iconPath = EnsureIconFile(workingDir);
        string shortcutPath = GetDesktopShortcutPath();

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            throw new InvalidOperationException("COM-объект WScript.Shell недоступен в системе.");

        object? shellObj = Activator.CreateInstance(shellType);
        if (shellObj is null)
            throw new InvalidOperationException("Не удалось инициализировать WScript.Shell.");

        try
        {
            dynamic shell = shellObj;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = "DSH Nexus — запуск в системном трее";
            shortcut.IconLocation = $"{iconPath},0";
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellObj);
        }

        return shortcutPath;
    }

    public static string EnsureIconFile(string targetDirectory)
    {
        string iconPath = Path.Combine(targetDirectory, "dsh-launcher.ico");
        using var bmp = CreateLauncherBitmap(64);
        SaveBitmapAsIcon(bmp, iconPath);
        return iconPath;
    }

    public static Media.ImageSource CreateWindowIcon()
    {
        using var bmp = CreateLauncherBitmap(64);
        IntPtr hBitmap = bmp.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    public static void ExportAssetIcons(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        using (var logo = CreateLauncherBitmap(256))
        {
            logo.Save(Path.Combine(outputDirectory, "dsh-nexus-logo-256.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        var states = new (string Name, Color DotColor)[]
        {
            ("running", Color.FromArgb(255, 34, 197, 94)),
            ("starting", Color.FromArgb(255, 245, 158, 11)),
            ("error", Color.FromArgb(255, 239, 68, 68)),
            ("stopped", Color.FromArgb(255, 148, 163, 184))
        };

        int[] sizes = [32, 64, 128];

        foreach (var (stateName, dotColor) in states)
        {
            foreach (int size in sizes)
            {
                using var bmp = CreateLauncherBitmap(size, dotColor);
                string filePath = Path.Combine(outputDirectory, $"icon-{stateName}-{size}.png");
                bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }

    public static Bitmap CreateLauncherBitmap(int size, Color? statusDotColor = null)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        int pad = Math.Max(2, size / 16);
        int rectSize = size - pad * 2;
        int radius = Math.Max(4, size / 4);

        using var bgBrush = new LinearGradientBrush(
            new Rectangle(pad, pad, rectSize, rectSize),
            Color.FromArgb(255, 24, 32, 52),
            Color.FromArgb(255, 13, 18, 30),
            45f);

        using var borderPen = new Pen(Color.FromArgb(255, 77, 136, 255), Math.Max(1.5f, size / 16f));
        using var path = CreateRoundedRect(new Rectangle(pad, pad, rectSize, rectSize), radius);

        g.FillPath(bgBrush, path);
        g.DrawPath(borderPen, path);

        using var promptPen = new Pen(Color.FromArgb(255, 96, 165, 250), Math.Max(2f, size / 11f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        float x1 = size * 0.27f;
        float yTop = size * 0.34f;
        float yMid = size * 0.50f;
        float yBot = size * 0.66f;
        float xTip = size * 0.44f;

        g.DrawLines(promptPen, new[]
        {
            new PointF(x1, yTop),
            new PointF(xTip, yMid),
            new PointF(x1, yBot)
        });

        g.DrawLine(promptPen, size * 0.50f, yBot, size * 0.72f, yBot);

        if (statusDotColor.HasValue)
        {
            int dotSize = Math.Max(6, size / 3);
            int dotX = size - dotSize - 1;
            int dotY = 1;
            using var dotBrush = new SolidBrush(statusDotColor.Value);
            using var dotOutline = new Pen(Color.FromArgb(255, 13, 18, 30), Math.Max(1.5f, size / 18f));
            g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
            g.DrawEllipse(dotOutline, dotX, dotY, dotSize, dotSize);
        }

        return bmp;
    }

    public static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void SaveBitmapAsIcon(Bitmap bitmap, string outputPath)
    {
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        byte[] pngBytes = pngStream.ToArray();

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(fs);

        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)1);

        writer.Write((byte)(bitmap.Width >= 256 ? 0 : bitmap.Width));
        writer.Write((byte)(bitmap.Height >= 256 ? 0 : bitmap.Height));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(pngBytes.Length);
        writer.Write(6 + 16);

        writer.Write(pngBytes);
    }
}