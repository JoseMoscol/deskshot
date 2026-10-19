using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using DeskShot;
using DeskShot.Application;
using DeskShot.Domain;
using DeskShot.Infrastructure;
using DeskShot.Presentation;

internal static partial class Program
{
    private static BitmapSource ScrollPattern(int width = 240, int height = 2400)
    {
        byte[] pixels = new byte[width * height * 4];
        var random = new Random(2718);
        for (int i = 0; i < pixels.Length; i += 4)
        { pixels[i] = (byte)random.Next(256); pixels[i + 1] = (byte)random.Next(256); pixels[i + 2] = (byte)random.Next(256); pixels[i + 3] = 255; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        image.Freeze(); return image;
    }
    private static BitmapSource ScrollCrop(BitmapSource image, int top, int height = 600)
    { var crop = new CroppedBitmap(image, new Int32Rect(0, top, image.PixelWidth, height)); crop.Freeze(); return crop; }

    private static void TestScrollPixels()
    {
        var original = ScrollPattern();
        var stitcher = new ScrollStitcher(ScrollCrop(original, 0));
        foreach (int y in new[] { 180, 360, 580, 800, 1000, 1220, 1440, 1660, 1800 })
            Check(stitcher.Append(ScrollCrop(original, y)).Status == StitchStatus.Added, "Falló la unión en " + y);
        Check(stitcher.Height == 2400 && Pixels(stitcher.BuildImage()).SequenceEqual(Pixels(original)), "La unión perdió, duplicó o alteró píxeles.");
        Check(stitcher.Append(ScrollCrop(original, 1800)).Status == StitchStatus.Unchanged && stitcher.Height == 2400, "Se duplicó el final.");
        // El pequeño último avance también debe reconocerse.
        var last = new ScrollStitcher(ScrollCrop(original, 0));
        Check(last.Append(ScrollCrop(original, 1)).AddedRows == 1, "No reconoce el avance de un píxel.");
    }
    private static void TestScrollAmbiguity()
    {
        var original = ScrollPattern();
        var stitcher = new ScrollStitcher(ScrollCrop(original, 0));
        Check(stitcher.Append(ScrollCrop(original, 900)).Status == StitchStatus.Uncertain, "Aceptó un salto sin solapamiento.");
        Check(stitcher.Append(ScrollCrop(original, 0)).Status == StitchStatus.Unchanged, "Un fallo cambió el historial.");
        byte[] repeated = new byte[240 * 1000 * 4];
        for (int y = 0; y < 1000; y++)
        for (int x = 0; x < 240; x++)
        { int i = (y * 240 + x) * 4; repeated[i] = (byte)(y % 20 * 10); repeated[i + 1] = (byte)x; repeated[i + 2] = 120; }
        var pattern = BitmapSource.Create(240, 1000, 96, 96, PixelFormats.Bgr32, null, repeated, 960); pattern.Freeze();
        var ambiguous = new ScrollStitcher(ScrollCrop(pattern, 0));
        Check(ambiguous.Append(ScrollCrop(pattern, 7)).Status == StitchStatus.Uncertain, "Adivinó la longitud de un patrón repetido.");
        // Encabezado fijo suficientemente grande: no inventar una unión.
        var next = Pixels(ScrollCrop(original, 160));
        var top = Pixels(ScrollCrop(original, 0));
        Buffer.BlockCopy(top, 0, next, 0, 240 * 80 * 3);
        var sticky = BitmapSource.Create(240, 600, 96, 96, PixelFormats.Bgr24, null, next, 720); sticky.Freeze();
        Check(stitcher.Append(sticky).Status == StitchStatus.Uncertain, "Ignoró un encabezado fijo.");
    }
    private static void TestScrollLimits()
    {
        var original = ScrollPattern();
        var stitcher = new ScrollStitcher(ScrollCrop(original, 0), 240 * 700);
        Check(stitcher.Append(ScrollCrop(original, 180)).Status == StitchStatus.LimitReached, "No aplica el límite de memoria.");
        Check(stitcher.Height == 600 && Pixels(stitcher.BuildImage()).SequenceEqual(Pixels(ScrollCrop(original, 0))), "Alteró la imagen al alcanzar el límite.");
        Throws<ArgumentException>(() => new ScrollStitcher(ScrollPattern(63, 300)));
        Throws<ArgumentException>(() => stitcher.Append(ScrollPattern(200, 600)));
    }
    private sealed class FrameSource : IScrollingCaptureSource
    {
        private readonly BitmapSource[] _images;
        private int _index;
        public bool FailScroll { get; set; }
        public CancellationTokenSource? StopAfterScroll { get; set; }
        public FrameSource(params BitmapSource[] images) => _images = images;
        public Task<BitmapSource> CaptureAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(_images[Math.Min(_index, _images.Length - 1)]); }
        public Task ScrollDownAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (FailScroll) throw new InvalidOperationException("Documento cerrado."); _index++; StopAfterScroll?.Cancel(); return Task.CompletedTask; }
    }
    private static void TestScrollSession()
    {
        var image = ScrollPattern();
        var result = new ScrollingCaptureSession().CaptureAsync(new FrameSource(ScrollCrop(image, 0), ScrollCrop(image, 180), ScrollCrop(image, 360)), null, CancellationToken.None).GetAwaiter().GetResult();
        Check(result.Frames == 3 && result.Image.PixelHeight == 960 && result.Message.Contains("No se detectó"), "No detectó el final o duplicó fotogramas.");
        Check(Pixels(result.Image).SequenceEqual(Pixels(ScrollCrop(image, 0, 960))), "El resultado de la sesión perdió píxeles.");
        var failed = new ScrollingCaptureSession().CaptureAsync(new FrameSource(ScrollCrop(image, 0)) { FailScroll = true }, null, CancellationToken.None).GetAwaiter().GetResult();
        Check(failed.Image.PixelHeight == 600 && failed.Message.Contains("Documento cerrado"), "No conservó el parcial ante un error.");
    }
    private static void TestScrollCancellation()
    {
        using var stop = new CancellationTokenSource();
        var source = new FrameSource(ScrollCrop(ScrollPattern(), 0)) { StopAfterScroll = stop };
        var result = new ScrollingCaptureSession().CaptureAsync(source, null, stop.Token).GetAwaiter().GetResult();
        Check(result.Frames == 1 && result.Image.PixelHeight == 600 && result.Message.Contains("Terminaste"), "La cancelación descartó la captura parcial.");
        Throws<OperationCanceledException>(() => new ScrollingCaptureSession().CaptureAsync(source, null, stop.Token).GetAwaiter().GetResult());
    }
    private static void TestLongCapture(App app)
    {
        var original = ScrollPattern(180, 22000);
        var exporter = new ImageExporter();
        string path = Path.Combine(_output, "scroll-long.png");
        exporter.SaveTo(original, path, ImageFileFormat.Png, 95);
        Check(Pixels(Load(path)).SequenceEqual(Pixels(original)), "PNG largo perdió píxeles.");
        var doc = new CaptureDocument(original);
        doc.Add(new Annotation(AnnotationTool.Rectangle, Colors.Red, 4, [new Point(20, 21500), new Point(140, 21900)]));
        var rendered = exporter.Render(doc);
        Check(rendered.PixelHeight == 22000 && rendered.PixelWidth == 180, "La anotación redujo la captura larga.");
        Check(Pixels(ScrollCrop(rendered, 100, 100)).SequenceEqual(Pixels(ScrollCrop(original, 100, 100))), "El render largo cambió contenido no anotado.");
        Check(!Pixels(ScrollCrop(rendered, 21500, 300)).SequenceEqual(Pixels(ScrollCrop(original, 21500, 300))), "Se perdió la anotación al final de una imagen larga.");
        var editor = new EditorWindow(app, doc, saved: true); RenderContent(editor, 1164, 781, "scroll-editor.png");
    }
    private static void TestScrollWindow(App app)
    {
        var window = new ScrollingCaptureWindow(new PixelBounds(100, 100, 900, 700));
        RenderContent(window, 494, 520, "scroll-setup.png");
        var main = new MainWindow(app); main.SetBusy(true);
        Check(!((Button)main.FindName("ScrollButton")).IsEnabled, "El botón permite capturas simultáneas.");
        main.SetBusy(false); Check(((Button)main.FindName("ScrollButton")).IsEnabled, "No rehabilita el botón.");
    }

    private static void TestLongOcr()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 700, 34000));
            foreach (var entry in new[] { (100, "Inicio del documento 12345"), (8050, "Segunda parte 67890"), (33200, "Final del documento 24680") })
                dc.DrawText(new FormattedText(entry.Item2, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 30, Brushes.Black, 1), new Point(30, entry.Item1));
        }
        var image = new RenderTargetBitmap(700, 34000, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        var result = new TesseractOcrEngine().RecognizeAsync(image, "spa", CancellationToken.None).GetAwaiter().GetResult();
        Check(new[] { "12345", "67890", "24680" }.All(result.Text.Contains), "El OCR largo perdió bloques de texto.");
        Check(result.Regions.Any(r => r.Text.Contains("24680") && r.Bounds.Y > 33000), "Las regiones OCR perdieron su posición original.");
    }

    // Documento de prueba independiente: no modifica aplicaciones ni archivos del usuario.
    private static int ShowScrollDocument(App app)
    {
        var content = new StackPanel { Background = Brushes.White, Width = 700 };
        for (int i = 1; i <= 24; i++)
        {
            var block = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };
            block.Children.Add(new TextBlock { Text = $"Sección {i:00} · Captura completa", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Brushes.DarkSlateBlue });
            block.Children.Add(new TextBlock { Text = $"Documento de verificación {i * 173}. Texto nítido y OCR local.\nLos píxeles se conservan sin escalado ni compresión.\nÚltima sección: 24. Fin del documento.", FontSize = 18, Foreground = Brushes.Black, Margin = new Thickness(0, 10, 0, 0) });
            content.Children.Add(block);
        }
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, CanContentScroll = false };
        var window = new Window { Title = "DeskShot · Documento de prueba de desplazamiento", Content = scroll, Width = 740, Height = 600, Left = 100, Top = 70, Background = Brushes.White, UseLayoutRounding = true };
        window.Loaded += (_, _) =>
        {
            var origin = scroll.PointToScreen(new Point(4, 4)); var end = scroll.PointToScreen(new Point(scroll.ActualWidth - 22, scroll.ActualHeight - 4));
            File.WriteAllText(Path.Combine(_output, "scroll-demo-bounds.txt"), $"{(int)origin.X},{(int)origin.Y},{(int)(end.X - origin.X)},{(int)(end.Y - origin.Y)}");
        };
        window.Closed += (_, _) => app.Shutdown();
        app.Run(window); return 0;
    }
    private static int ShowScrollDemo(App app)
    {
        int[] b = File.ReadAllText(Path.Combine(_output, "scroll-demo-bounds.txt")).Split(',').Select(int.Parse).ToArray();
        var window = new ScrollingCaptureWindow(new PixelBounds(b[0], b[1], b[2], b[3]));
        if (window.ShowDialog() == true && window.Result is { } result)
        {
            new ImageExporter().SaveTo(result.Image, Path.Combine(_output, "scroll-demo-result.png"), ImageFileFormat.Png, 95);
            Console.WriteLine($"{result.Image.PixelWidth} × {result.Image.PixelHeight}; partes={result.Frames}; {result.Message}");
        }
        app.Shutdown(); return 0;
    }
    private static void TestRealScrollResult()
    {
        var image = Load(Path.Combine(_output, "scroll-demo-result.png"));
        var result = new TesseractOcrEngine().RecognizeAsync(image, "spa", CancellationToken.None).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(_output, "scroll-demo-ocr.txt"), result.Text);
        Check(image.PixelWidth == 1049 && image.PixelHeight > 5000, "La captura real se cortó o cambió el ancho.");
        for (int i = 1; i <= 24; i++)
            Check(result.Text.Contains($"Sección {i:00}") || result.Text.Contains($"Sección {i}"), "No se encuentra la sección " + i);
        Check(!result.Text.Contains("Esc para terminar") && !result.Text.Contains("Terminar y abrir editor"), "La captura incluye los controles de progreso.");
        Console.WriteLine($"  PNG real: {image.PixelWidth} × {image.PixelHeight} px; 24 secciones; OCR local.");
    }
}
