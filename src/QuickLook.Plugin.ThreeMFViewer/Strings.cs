using System.Globalization;

namespace QuickLook.Plugin.ThreeMFViewer;

/// <summary>
/// The few texts shown by the viewer, in Spanish or English depending on the system language.
/// </summary>
internal static class Strings
{
    private static readonly bool Spanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";

    public static string Model3D => "3D";

    public static string Image => Spanish ? "Imagen" : "Image";

    public static string Plate(int number, int index, int count) =>
        number > 0
            ? (Spanish ? $"Placa {number}" : $"Plate {number}") + $"  ({index}/{count})"
            : $"{index}/{count}";

    public static string Objects(int count) =>
        Spanish ? (count == 1 ? "1 objeto" : $"{count} objetos") : (count == 1 ? "1 object" : $"{count} objects");

    public static string Triangles(long count) =>
        string.Format(CultureInfo.CurrentCulture, Spanish ? "{0:N0} triángulos" : "{0:N0} triangles", count);

    public static string NoGeometry =>
        Spanish ? "El archivo no contiene geometría que se pueda mostrar." : "The file has no geometry to show.";

    public static string LoadFailed(string message) =>
        (Spanish ? "No se pudo cargar el modelo 3D: " : "Could not load the 3D model: ") + message;

    public static string Hint =>
        Spanish
            ? "Arrastrar: girar · Clic derecho: mover · Rueda: zoom · Doble clic: centrar"
            : "Drag: rotate · Right-drag: pan · Wheel: zoom · Double-click: reset";
}
