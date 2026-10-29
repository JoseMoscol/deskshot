namespace DeskShot.Application;

public interface IScrollingCaptureSource
{
    Task<BitmapSource> CaptureAsync(CancellationToken token);
    Task ScrollDownAsync(CancellationToken token);
}
public sealed record ScrollProgress(int Width, int Height, int Frames);
public sealed record ScrollingCaptureResult(BitmapSource Image, string Message, int Frames, Int32Rect? DetectedRegion = null);

public sealed class ScrollingCaptureSession
{
    public async Task<ScrollingCaptureResult> CaptureAsync(IScrollingCaptureSource source, IProgress<ScrollProgress>? progress,
        CancellationToken token, int maxSteps = 300, bool detectFixedEdges = false)
    {
        // La cancelación posterior al primer fotograma conserva todo lo que ya pudo unirse.
        var first = await source.CaptureAsync(token);
        var stitcher = new ScrollStitcher(first);
        Int32Rect? detected = null;
        bool detectionPending = detectFixedEdges;
        string message = $"Se alcanzó el límite de {maxSteps} desplazamientos; se conserva la parte capturada.";
        int unchanged = 0;
        for (int step = 0; step < maxSteps; step++)
        {
            progress?.Report(new(stitcher.Width, stitcher.Height, stitcher.Frames));
            if (token.IsCancellationRequested) { message = "Terminaste la captura; se conserva la parte capturada."; break; }
            try
            {
                await source.ScrollDownAsync(token);
                var next = await source.CaptureAsync(token);
                StitchResult result;
                if (detectionPending)
                {
                    var proposal = await Task.Run(() => ScrollAreaDetector.Detect(first, next));
                    if (proposal is { } region)
                    {
                        var candidate = new ScrollStitcher(ScrollAreaDetector.Crop(first, region));
                        result = await Task.Run(() => candidate.Append(ScrollAreaDetector.Crop(next, region)));
                        // Mantener ambos fotogramas originales hasta tener una unión comprobada.
                        stitcher = candidate; detected = region; detectionPending = false;
                    }
                    else
                    {
                        result = await Task.Run(() => stitcher.Append(next));
                        if (result.Status != StitchStatus.Unchanged) detectionPending = false;
                    }
                }
                else result = await Task.Run(() => stitcher.Append(detected is { } crop ? ScrollAreaDetector.Crop(next, crop) : next));
                if (result.Status == StitchStatus.Unchanged)
                {
                    // Reintentos separados por el tiempo de estabilización del origen.
                    if (++unchanged < 3) continue;
                    message = "No se detectó más movimiento (fin del documento o zona sin cambios). Revisa el resultado."; break;
                }
                unchanged = 0;
                if (result.Status == StitchStatus.Uncertain)
                { message = "No se pudo unir la siguiente parte con seguridad. Se conserva la parte capturada; excluye encabezados fijos, animaciones y barras e inténtalo de nuevo."; break; }
                if (result.Status == StitchStatus.LimitReached)
                { message = "Se alcanzó el límite de 32 megapíxeles o 60 000 px de alto. Se conserva la parte capturada sin reducirla."; break; }
            }
            catch (OperationCanceledException) { message = "Terminaste la captura; se conserva la parte capturada."; break; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { message = "Captura detenida: " + ex.Message + " Se conserva la parte capturada."; break; }
        }
        if (detected is { } area)
            message = $"Área validada: {area.Width} × {area.Height} px por parte; bordes fijos y márgenes exteriores sin cambios excluidos. " + message;
        else if (detectFixedEdges) message = "No se pudo validar un recorte automático; se conserva la selección original. " + message;
        return new(await Task.Run(stitcher.BuildImage), message, stitcher.Frames, detected);
    }
}
