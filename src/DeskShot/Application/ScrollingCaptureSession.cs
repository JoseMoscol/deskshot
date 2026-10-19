namespace DeskShot.Application;

public interface IScrollingCaptureSource
{
    Task<BitmapSource> CaptureAsync(CancellationToken token);
    Task ScrollDownAsync(CancellationToken token);
}
public sealed record ScrollProgress(int Width, int Height, int Frames);
public sealed record ScrollingCaptureResult(BitmapSource Image, string Message, int Frames);

public sealed class ScrollingCaptureSession
{
    public async Task<ScrollingCaptureResult> CaptureAsync(IScrollingCaptureSource source, IProgress<ScrollProgress>? progress,
        CancellationToken token, int maxSteps = 300)
    {
        // La cancelación posterior al primer fotograma conserva todo lo que ya pudo unirse.
        var first = await source.CaptureAsync(token);
        var stitcher = new ScrollStitcher(first);
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
                var result = await Task.Run(() => stitcher.Append(next));
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
        return new(await Task.Run(stitcher.BuildImage), message, stitcher.Frames);
    }
}
