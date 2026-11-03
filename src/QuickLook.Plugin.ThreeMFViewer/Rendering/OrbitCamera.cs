using System;
using System.Windows.Media.Media3D;

namespace QuickLook.Plugin.ThreeMFViewer.Rendering;

/// <summary>
/// Turntable camera around a target point, Z up like the print bed of a slicer.
/// </summary>
internal sealed class OrbitCamera
{
    private const double DefaultAzimuth = -60;
    private const double DefaultElevation = 28;
    private static readonly Vector3D WorldUp = new(0, 0, 1);

    private readonly PerspectiveCamera _camera;
    private readonly DirectionalLight _headlight;

    private Point3D _target;
    private Point3D _center;
    private double _radius = 50;
    private double _distance = 150;
    private double _azimuth = DefaultAzimuth;
    private double _elevation = DefaultElevation;

    public OrbitCamera(PerspectiveCamera camera, DirectionalLight headlight)
    {
        _camera = camera;
        _headlight = headlight;
        Update();
    }

    /// <summary>Frames the whole model from the default angle.</summary>
    public void Fit(Rect3D bounds, double aspectRatio)
    {
        if (bounds.IsEmpty)
            return;

        _center = _target = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        _radius = Math.Max(new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ).Length / 2, 0.001);
        _azimuth = DefaultAzimuth;
        _elevation = DefaultElevation;

        // Closest distance at which every corner of the box is inside the view. The field
        // of view of WPF is the horizontal one.
        GetAxes(out var forward, out var right, out var up);
        var tanX = Math.Tan(_camera.FieldOfView * Math.PI / 360);
        var tanY = tanX / (aspectRatio > 0 ? aspectRatio : 4d / 3);

        _distance = 0;
        for (var corner = 0; corner < 8; corner++)
        {
            var offset = new Vector3D(
                (corner & 1) == 0 ? -bounds.SizeX / 2 : bounds.SizeX / 2,
                (corner & 2) == 0 ? -bounds.SizeY / 2 : bounds.SizeY / 2,
                (corner & 4) == 0 ? -bounds.SizeZ / 2 : bounds.SizeZ / 2);

            var towardsCamera = -Vector3D.DotProduct(offset, forward);
            _distance = Math.Max(_distance, towardsCamera + Math.Abs(Vector3D.DotProduct(offset, right)) / tanX);
            _distance = Math.Max(_distance, towardsCamera + Math.Abs(Vector3D.DotProduct(offset, up)) / tanY);
        }

        _distance = Math.Max(_distance * 1.1, _radius * 0.1);
        Update();
    }

    public void Rotate(double dx, double dy)
    {
        _azimuth -= dx * 0.4;
        _elevation = Math.Max(-89, Math.Min(89, _elevation + dy * 0.4));
        Update();
    }

    public void Pan(double dx, double dy, double viewportWidth)
    {
        if (viewportWidth <= 0)
            return;

        GetAxes(out var forward, out var right, out var up);
        var unitsPerPixel = 2 * _distance * Math.Tan(_camera.FieldOfView * Math.PI / 360) / viewportWidth;
        _target += (-dx * right + dy * up) * unitsPerPixel;
        Update();
    }

    public void Zoom(double wheelDelta)
    {
        _distance *= Math.Pow(0.85, wheelDelta / 120);
        _distance = Math.Max(_radius * 0.05, Math.Min(_radius * 50, _distance));
        Update();
    }

    private void GetAxes(out Vector3D forward, out Vector3D right, out Vector3D up)
    {
        var az = _azimuth * Math.PI / 180;
        var el = _elevation * Math.PI / 180;

        forward = -new Vector3D(Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el));
        right = Vector3D.CrossProduct(forward, WorldUp);
        right.Normalize();
        up = Vector3D.CrossProduct(right, forward);
        up.Normalize();
    }

    private void Update()
    {
        GetAxes(out var forward, out var right, out var up);

        _camera.Position = _target - forward * _distance;
        _camera.LookDirection = forward * _distance;
        _camera.UpDirection = WorldUp;
        // Keep the depth range tight around the model (panning moves the target away from it).
        var reach = _radius * 1.5 + (_target - _center).Length;
        _camera.NearPlaneDistance = Math.Max(_distance - reach, _distance * 0.01);
        _camera.FarPlaneDistance = _distance + reach;

        // A light attached to the camera, slightly above and to the left of it.
        _headlight.Direction = forward - up * 0.45 + right * 0.25;
    }
}
