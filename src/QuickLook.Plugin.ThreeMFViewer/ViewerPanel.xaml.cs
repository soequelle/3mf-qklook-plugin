using QuickLook.Common.Plugin;
using QuickLook.Plugin.ThreeMFViewer.Rendering;
using QuickLook.Plugin.ThreeMFViewer.ThreeMF;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace QuickLook.Plugin.ThreeMFViewer;

public partial class ViewerPanel : UserControl, IDisposable
{
    private static readonly Brush ActiveButton = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));

    private readonly ContextObject _context;
    private readonly OrbitCamera _orbit;
    private readonly CancellationTokenSource _cancellation = new();

    private List<PlateImage> _images = [];
    private int _imageIndex;
    private bool _hasModel;
    private bool _userChoseImage;
    private Rect3D _bounds = Rect3D.Empty;
    private string _modelInfo;

    private Point _lastMouse;
    private MouseButton? _dragButton;

    public ViewerPanel() : this(null)
    {
    }

    public ViewerPanel(ContextObject context)
    {
        _context = context;
        InitializeComponent();

        _orbit = new OrbitCamera(camera, headlight);
        modelButton.Content = Strings.Model3D;
        imageButton.Content = Strings.Image;

        viewportHost.MouseDown += Viewport_MouseDown;
        viewportHost.MouseMove += Viewport_MouseMove;
        viewportHost.MouseUp += Viewport_MouseUp;
        viewportHost.LostMouseCapture += (_, _) => _dragButton = null;
        viewportHost.MouseWheel += Viewport_MouseWheel;
    }

    public void Load(string path)
    {
        var token = _cancellation.Token;

        Task.Run(() =>
        {
            // Embedded renders are small and quick to show while the geometry loads.
            Dispatch(() => ShowImages(LoadImages(path)));

            var scene = ThreeMFReader.Read(path, token);
            var model = scene.TriangleCount > 0 ? SceneBuilder.Build(scene, token) : null;
            Dispatch(() => ShowModel(scene, model));
        }, token).ContinueWith(task =>
        {
            if (task.IsFaulted)
                Dispatch(() => ShowError(task.Exception.GetBaseException()));
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        modelVisual.Content = null;
        imageView.Source = null;
        _images = [];
    }

    internal void ShowModel(ThreeMFScene scene, Model3DGroup model)
    {
        if (model == null || model.Bounds.IsEmpty)
        {
            ShowError(new InvalidDataException(Strings.NoGeometry));
            return;
        }

        modelVisual.Content = model;
        _bounds = model.Bounds;
        _hasModel = true;

        var parts = new List<string>
        {
            Strings.Objects(scene.ObjectCount),
            Strings.Triangles(scene.TriangleCount),
            string.Format(CultureInfo.CurrentCulture, "{0:0.#} × {1:0.#} × {2:0.#} mm", _bounds.SizeX, _bounds.SizeY, _bounds.SizeZ),
        };
        if (!string.IsNullOrEmpty(scene.Application))
            parts.Add(scene.Application);
        _modelInfo = string.Join("  ·  ", parts);

        if (!_userChoseImage)
            SetMode(showImage: false);
        else
            UpdateChrome();

        Dispatcher.BeginInvoke(new Action(FitView), System.Windows.Threading.DispatcherPriority.Loaded);
        SetBusy(false);
    }

    private void ShowImages(List<PlateImage> images)
    {
        _images = images;
        if (_images.Count > 0 && !_hasModel)
            SetMode(showImage: true);

        if (_images.Count > 0)
            SetBusy(false);
    }

    private void ShowError(Exception error)
    {
        if (error is OperationCanceledException)
            return;

        var message = error is InvalidDataException ? error.Message : Strings.LoadFailed(error.Message);

        if (_images.Count > 0)
        {
            // The slicer render is still worth showing.
            _modelInfo = message;
            SetMode(showImage: true);
        }
        else
        {
            statusText.Text = message;
            statusText.Visibility = Visibility.Visible;
            viewportHost.Visibility = Visibility.Collapsed;
        }

        SetBusy(false);
    }

    private void SetMode(bool showImage)
    {
        showImage &= _images.Count > 0;
        viewportHost.Visibility = showImage || !_hasModel ? Visibility.Collapsed : Visibility.Visible;
        imageView.Visibility = showImage ? Visibility.Visible : Visibility.Collapsed;

        if (showImage)
            ShowImage(_imageIndex);

        UpdateChrome();
    }

    private void UpdateChrome()
    {
        var showingImage = imageView.Visibility == Visibility.Visible;

        modeSwitch.Visibility = _hasModel && _images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        modelButton.Background = showingImage ? Brushes.Transparent : ActiveButton;
        imageButton.Background = showingImage ? ActiveButton : Brushes.Transparent;

        plateNav.Visibility = showingImage && _images.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        var info = showingImage ? (_hasModel ? null : _modelInfo) : _modelInfo + Environment.NewLine + Strings.Hint;
        infoText.Text = info;
        infoBar.Visibility = string.IsNullOrEmpty(info) ? Visibility.Collapsed : Visibility.Visible;
        infoBar.Margin = plateNav.Visibility == Visibility.Visible ? new Thickness(10, 0, 0, 52) : new Thickness(10, 0, 0, 10);
    }

    private void ShowImage(int index)
    {
        if (_images.Count == 0)
            return;

        _imageIndex = (index % _images.Count + _images.Count) % _images.Count;
        var image = _images[_imageIndex];
        imageView.Source = image.Bitmap;

        // Small slicer thumbnails look better at their own size than blown up.
        imageView.StretchDirection = image.Bitmap.PixelWidth < 400 ? StretchDirection.DownOnly : StretchDirection.Both;
        plateText.Text = Strings.Plate(image.Plate, _imageIndex + 1, _images.Count);
    }

    internal void FitView()
    {
        var width = viewportHost.ActualWidth;
        var height = viewportHost.ActualHeight;
        _orbit.Fit(_bounds, width > 0 && height > 0 ? width / height : 4d / 3);
    }

    private void SetBusy(bool busy)
    {
        if (_context != null)
            _context.IsBusy = busy;
    }

    private void Dispatch(Action action)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_cancellation.IsCancellationRequested)
                action();
        }));
    }

    private static List<PlateImage> LoadImages(string path)
    {
        try
        {
            return ThreeMFReader.ReadImages(path)
                .Select(image => new PlateImage { Plate = image.Plate, Bitmap = Decode(image.Data) })
                .Where(image => image.Bitmap != null)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static BitmapSource Decode(byte[] data)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(data);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            FitView();
            e.Handled = true;
            return;
        }

        _dragButton = e.ChangedButton;
        _lastMouse = e.GetPosition(viewportHost);
        viewportHost.CaptureMouse();
        e.Handled = true;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragButton == null)
            return;

        var position = e.GetPosition(viewportHost);
        var delta = position - _lastMouse;
        _lastMouse = position;

        var pan = _dragButton != MouseButton.Left || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (pan)
            _orbit.Pan(delta.X, delta.Y, viewportHost.ActualWidth);
        else
            _orbit.Rotate(delta.X, delta.Y);
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != _dragButton)
            return;

        _dragButton = null;
        viewportHost.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _orbit.Zoom(e.Delta);
        e.Handled = true;
    }

    private void ModelButton_Click(object sender, RoutedEventArgs e)
    {
        _userChoseImage = false;
        SetMode(showImage: false);
    }

    private void ImageButton_Click(object sender, RoutedEventArgs e)
    {
        _userChoseImage = true;
        SetMode(showImage: true);
    }

    private void PreviousPlate_Click(object sender, RoutedEventArgs e) => ShowImage(_imageIndex - 1);

    private void NextPlate_Click(object sender, RoutedEventArgs e) => ShowImage(_imageIndex + 1);

    private sealed class PlateImage
    {
        public int Plate { get; set; }

        public BitmapSource Bitmap { get; set; }
    }
}
