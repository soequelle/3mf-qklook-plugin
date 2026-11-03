using QuickLook.Common.Plugin;
using System;
using System.IO;
using System.Windows;

namespace QuickLook.Plugin.ThreeMFViewer;

public sealed class Plugin : IViewer
{
    private ViewerPanel _panel;

    /// <summary>
    /// Above the built-in HelixViewer (-5), which also claims .3mf but cannot read
    /// the files written by current slicers.
    /// </summary>
    public int Priority => 10;

    public void Init()
    {
    }

    public bool CanHandle(string path)
    {
        return !Directory.Exists(path)
            && path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)
            && IsZip(path);
    }

    public void Prepare(string path, ContextObject context)
    {
        context.PreferredSize = new Size { Width = 1000, Height = 720 };
    }

    public void View(string path, ContextObject context)
    {
        _panel = new ViewerPanel(context);
        context.ViewerContent = _panel;
        context.Title = Path.GetFileName(path);
        _panel.Load(path);
    }

    public void Cleanup()
    {
        _panel?.Dispose();
        _panel = null;
    }

    private static bool IsZip(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[4];
            return stream.Read(header, 0, 4) == 4
                && header[0] == 'P' && header[1] == 'K' && header[2] == 3 && header[3] == 4;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
