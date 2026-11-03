using QuickLook.Plugin.ThreeMFViewer;
using QuickLook.Plugin.ThreeMFViewer.Rendering;
using QuickLook.Plugin.ThreeMFViewer.ThreeMF;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RenderTest;

/// <summary>
/// Loads 3MF files with the plugin code and saves what the viewer shows as PNG,
/// to check the parser and the rendering without going through QuickLook.
///
/// Usage: RenderTest &lt;output folder&gt; &lt;file.3mf&gt;...
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: RenderTest <output folder> <file.3mf>...");
            return 2;
        }

        var output = Directory.CreateDirectory(args[0]).FullName;
        var failures = 0;

        foreach (var file in args.Skip(1))
        {
            Console.WriteLine($"== {Path.GetFileName(file)}");
            try
            {
                var watch = Stopwatch.StartNew();
                var scene = ThreeMFReader.Read(file);
                var readTime = watch.ElapsedMilliseconds;

                watch.Restart();
                var model = SceneBuilder.Build(scene);
                var buildTime = watch.ElapsedMilliseconds;

                var images = ThreeMFReader.ReadImages(file);

                Console.WriteLine($"   app: {scene.Application}");
                Console.WriteLine($"   objects: {scene.ObjectCount}, mesh instances: {scene.Instances.Count}, triangles: {scene.TriangleCount:N0}");
                Console.WriteLine($"   filaments: {string.Join(" ", scene.FilamentColors.Select(c => "#" + (c & 0xFFFFFF).ToString("X6")))}");
                Console.WriteLine($"   colour groups: {string.Join(", ", scene.Instances.Select(i => $"[f{i.DefaultFilament}: {string.Join(" ", i.Mesh.Keys.Select(Describe))}]"))}");
                Console.WriteLine($"   bounds: {model.Bounds}");
                Console.WriteLine($"   embedded images: {string.Join(", ", images.Select(i => $"plate {i.Plate} ({i.Data.Length / 1024} KB)"))}");
                Console.WriteLine($"   read {readTime} ms, build {buildTime} ms");

                var png = Path.Combine(output, Path.GetFileNameWithoutExtension(file) + ".png");
                Render(scene, model, png);
                Console.WriteLine($"   -> {png}");
            }
            catch (Exception e)
            {
                failures++;
                Console.WriteLine($"   FAILED: {e}");
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static string Describe(ColorKey key) =>
        key.IsExplicit ? "#" + (key.Argb & 0xFFFFFF).ToString("X6") : key.Filament == 0 ? "inherit" : $"f{key.Filament}";

    private static void Render(ThreeMFScene scene, System.Windows.Media.Media3D.Model3DGroup model, string path)
    {
        const int width = 1000, height = 720;

        var panel = new ViewerPanel();
        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Child = panel };

        panel.ShowModel(scene, model);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        panel.FitView();
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
