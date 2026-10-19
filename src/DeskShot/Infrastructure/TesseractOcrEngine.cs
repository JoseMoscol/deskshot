using DeskShot.Application;
using Tesseract;

namespace DeskShot.Infrastructure;

/// <summary>OCR en memoria. Solo usa DLLs y modelos incluidos junto al ejecutable.</summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly string _dataDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string Name => "Tesseract · local";

    public TesseractOcrEngine(string? dataDirectory = null) =>
        _dataDirectory = dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "tessdata");

    public async Task<OcrResult> RecognizeAsync(BitmapSource image, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (language is not ("spa" or "eng" or "spa+eng"))
            throw new ArgumentException("Selecciona español, inglés o español e inglés.", nameof(language));
        foreach (string code in language.Split('+'))
            if (!File.Exists(Path.Combine(_dataDirectory, code + ".traineddata")))
                throw new FileNotFoundException($"Falta el modelo local {code}.traineddata. Conserva la carpeta tessdata junto a DeskShot.exe.");

        // BitmapFrame puede conservar metadatos de un decodificador ligado al hilo
        // de origen incluso después de Freeze. El contenedor de píxeles evita que
        // BitmapFrame.Create intente leer esos metadatos en el hilo de OCR.
        var source = image.IsFrozen ? image : image.Clone();
        source.Freeze();
        var snapshot = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        snapshot.Freeze();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Recognize(snapshot, language, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNativeDependencyError(ex))
        {
            throw new InvalidOperationException("No se pudo cargar el motor OCR local. Conserva las carpetas x64/x86 y tessdata junto al ejecutable y verifica que esté instalado Microsoft Visual C++ Redistributable 2015–2022 de la arquitectura correspondiente.", ex);
        }
        finally { _gate.Release(); }
    }

    private OcrResult Recognize(BitmapSource original, string language, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.PixelHeight > 8000) return RecognizeLong(original, language, token);
        // Aumentar capturas pequeñas ayuda con el texto fino. La imagen original
        // no se cambia y las regiones se convierten de vuelta a sus píxeles.
        double scale = Math.Max(original.PixelWidth, original.PixelHeight) < 1600 ? 2 : 1;
        BitmapSource input = scale == 1 ? original : new TransformedBitmap(original, new ScaleTransform(scale, scale));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(input));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        token.ThrowIfCancellationRequested();
        using var engine = new TesseractEngine(_dataDirectory, language, EngineMode.LstmOnly);
        engine.SetVariable("preserve_interword_spaces", "1");
        engine.SetVariable("user_defined_dpi", "300");
        using var pix = Pix.LoadFromMemory(buffer.ToArray());
        using var page = engine.Process(pix, PageSegMode.Auto);
        // El wrapper no expone interrupción de la llamada nativa. La cancelación
        // se comprueba entre fases y descarta resultados antes de publicarlos.
        string text = page.GetText();
        token.ThrowIfCancellationRequested();
        var regions = new List<OcrTextRegion>();
        using var iterator = page.GetIterator();
        iterator.Begin();
        do
        {
            token.ThrowIfCancellationRequested();
            string word = iterator.GetText(PageIteratorLevel.Word)?.Trim() ?? "";
            if (word.Length == 0 || !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds)) continue;
            var rect = Domain.SelectionMath.FromPoints(new Point(bounds.X1 / scale, bounds.Y1 / scale),
                new Point(bounds.X2 / scale, bounds.Y2 / scale), original.PixelWidth, original.PixelHeight);
            regions.Add(new OcrTextRegion(word, rect, Math.Clamp(iterator.GetConfidence(PageIteratorLevel.Word) / 100.0, 0, 1)));
        }
        while (iterator.Next(PageIteratorLevel.Word));
        token.ThrowIfCancellationRequested();
        return new OcrResult(NormalizeText(text), regions.AsReadOnly());
    }

    private static string NormalizeText(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Replace("\n", Environment.NewLine);

    private OcrResult RecognizeLong(BitmapSource original, string language, CancellationToken token)
    {
        var texts = new List<string>();
        var regions = new List<OcrTextRegion>();
        for (int top = 0; top < original.PixelHeight;)
        {
            token.ThrowIfCancellationRequested();
            int bottom = Math.Min(top + 8000, original.PixelHeight);
            if (bottom < original.PixelHeight) bottom = FindQuietRow(original, bottom - 300, bottom);
            var tile = new CroppedBitmap(original, new Int32Rect(0, top, original.PixelWidth, bottom - top)); tile.Freeze();
            var result = Recognize(tile, language, token);
            if (!string.IsNullOrWhiteSpace(result.Text)) texts.Add(result.Text);
            regions.AddRange(result.Regions.Select(r => r with { Bounds = new Int32Rect(r.Bounds.X, r.Bounds.Y + top, r.Bounds.Width, r.Bounds.Height) }));
            top = bottom;
        }
        return new OcrResult(string.Join(Environment.NewLine + Environment.NewLine, texts), regions.AsReadOnly());
    }

    private static int FindQuietRow(BitmapSource image, int start, int end)
    {
        // Preferir un espacio entre líneas antes de cortar el bloque OCR.
        var sample = new FormatConvertedBitmap(new CroppedBitmap(image, new Int32Rect(0, start, image.PixelWidth, end - start)), PixelFormats.Bgr24, null, 0);
        int stride = image.PixelWidth * 3;
        byte[] pixels = new byte[stride * (end - start)]; sample.CopyPixels(pixels, stride, 0);
        for (int y = end - start - 1; y >= 0; y--)
        {
            int offset = y * stride; bool quiet = true;
            for (int x = 3; x < stride && quiet; x += 3)
                for (int c = 0; c < 3; c++)
                    if (Math.Abs(pixels[offset + x + c] - pixels[offset + c]) > 10) { quiet = false; break; }
            if (quiet) return start + y;
        }
        return end;
    }

    private static bool IsNativeDependencyError(Exception ex) =>
        ex is DllNotFoundException or BadImageFormatException ||
        ex.InnerException is not null && IsNativeDependencyError(ex.InnerException);
}
