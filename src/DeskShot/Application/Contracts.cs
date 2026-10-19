using DeskShot.Domain;

namespace DeskShot.Application;

public interface ICaptureService
{
    DesktopFrame CaptureDesktop();
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public interface IImageExporter
{
    BitmapSource Render(CaptureDocument document);
    string Save(BitmapSource image, string folder, ImageFileFormat format, int jpegQuality);
    void SaveTo(BitmapSource image, string path, ImageFileFormat format, int jpegQuality);
}

public sealed record OcrTextRegion(string Text, Int32Rect Bounds, double? Confidence = null);
public sealed record OcrResult(string Text, IReadOnlyList<OcrTextRegion> Regions);

// Punto de extensión: un motor local puede implementarlo sin depender de las ventanas.
// El llamador decide imagen original vs. editada y la cancelación de trabajos.
public interface IOcrEngine
{
    string Name { get; }
    Task<OcrResult> RecognizeAsync(BitmapSource image, string language, CancellationToken cancellationToken);
}
