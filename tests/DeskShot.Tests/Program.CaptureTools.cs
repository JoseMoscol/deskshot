using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskShot;
using DeskShot.Application;
using DeskShot.Domain;
using DeskShot.Infrastructure;
using DeskShot.Presentation;

internal static partial class Program
{
    private static BitmapSource WithFixedChrome(BitmapSource image, int offset, bool scrollbar = false)
    {
        int w = image.PixelWidth + 70, h = 720;
        var bytes = Pixels(ScrollPattern(w, h));
        var body = Pixels(ScrollCrop(image, offset));
        for (int y = 0; y < 600; y++) Buffer.BlockCopy(body, y * image.PixelWidth * 3, bytes, ((y + 70) * w + 40) * 3, image.PixelWidth * 3);
        if (scrollbar)
            for (int y = 70; y < 670; y++)
            for (int x = 40 + image.PixelWidth; x < 40 + image.PixelWidth + 18; x++)
            {
                int p = (y * w + x) * 3;
                byte value = y >= 70 + offset / 3 && y < 170 + offset / 3 ? (byte)70 : (byte)220;
                bytes[p] = bytes[p + 1] = bytes[p + 2] = value;
            }
        var frame = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, bytes, w * 3); frame.Freeze(); return frame;
    }
    private static void TestSmartScroll()
    {
        var original = ScrollPattern();
        foreach (bool scrollbar in new[] { false, true })
        {
            var frames = new[] { 0, 180, 360, 540 }.Select(y => WithFixedChrome(original, y, scrollbar)).ToArray();
            var area = ScrollAreaDetector.Detect(frames[0], frames[1]);
            Check(area == new Int32Rect(40, 70, 240, 600), "No excluye encabezado, pie, laterales o scrollbar: " + area);
            var result = new ScrollingCaptureSession().CaptureAsync(new FrameSource(frames), null, CancellationToken.None, detectFixedEdges: true).GetAwaiter().GetResult();
            Check(result.Frames == 4 && result.DetectedRegion == area && result.Image.PixelHeight == 1140, "Perdió una parte al fijar el recorte automático.");
            Check(Pixels(result.Image).SequenceEqual(Pixels(ScrollCrop(original, 0, 1140))), "El recorte automático perdió o duplicó píxeles del contenido.");
        }
    }
    private static void TestSmartScrollAmbiguity()
    {
        var original = ScrollPattern();
        Check(ScrollAreaDetector.Detect(WithFixedChrome(original, 0), WithFixedChrome(original, 900)) is null, "Acepta un salto sin solapamiento.");
        Check(ScrollAreaDetector.Detect(WithFixedChrome(original, 0), WithFixedChrome(original, 0)) is null, "Inventa movimiento.");
        var result = new ScrollingCaptureSession().CaptureAsync(new FrameSource(WithFixedChrome(original, 0), WithFixedChrome(original, 900)), null, CancellationToken.None, detectFixedEdges: true).GetAwaiter().GetResult();
        Check(result.DetectedRegion is null && result.Frames == 1 && Pixels(result.Image).SequenceEqual(Pixels(WithFixedChrome(original, 0))), "Un fallo de detección alteró el parcial original.");
        byte[] stripes = new byte[240 * 2400 * 4];
        for (int y = 0; y < 2400; y++) for (int x = 0; x < 240; x++) { int p = (y * 240 + x) * 4; stripes[p] = (byte)(y % 20 * 10); stripes[p + 1] = (byte)x; stripes[p + 3] = 255; }
        var repeated = BitmapSource.Create(240, 2400, 96, 96, PixelFormats.Bgr32, null, stripes, 960); repeated.Freeze();
        Check(ScrollAreaDetector.Detect(WithFixedChrome(repeated, 0), WithFixedChrome(repeated, 7)) is null, "Adivina el desplazamiento de un patrón repetido.");
    }
    private static void TestPdfLayout()
    {
        var captures = new[] { new PdfCapture("Larga", ScrollPattern(700, 3400)), new PdfCapture("Corta", Pattern(500, 300)) };
        foreach (var options in new[] { new PdfOptions(), new PdfOptions(PdfPaper.Letter, true) })
        {
            var pages = PdfLayout.Plan(captures, options);
            Check(pages.Count > 2, "No dividió la imagen larga.");
            foreach (var group in pages.GroupBy(p => p.CaptureIndex))
            {
                int row = 0;
                foreach (var page in group)
                {
                    Check(page.Pixels.Y == row && page.Pixels.Width == captures[group.Key].Image.PixelWidth, "PDF salta filas o reduce el ancho en píxeles.");
                    Check(page.Width <= options.PageSize.Width - 48 + .001 && page.Height <= options.PageSize.Height - 48 + .001, "La imagen sale del papel.");
                    row += page.Pixels.Height;
                }
                Check(row == captures[group.Key].Image.PixelHeight, "PDF perdió el final de la captura.");
            }
        }
        Throws<ArgumentException>(() => PdfLayout.Plan([], new()));
        Throws<ArgumentException>(() => PdfLayout.Plan(Enumerable.Repeat(captures[0], 51).ToArray(), new()));
        Throws<ArgumentException>(() => PdfLayout.Plan(captures, new((PdfPaper)99)));
    }
    private static void TestPdfWriter()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 720, 2400));
            for (int i = 0; i < 12; i++)
            {
                dc.DrawRectangle(i % 2 == 0 ? Brushes.AliceBlue : Brushes.Honeydew, null, new Rect(20, i * 200 + 10, 680, 180));
                dc.DrawText(new FormattedText($"Sección {i + 1:00} · Información local\nCaptura sin pérdida. Total: {i * 173}.", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 28, Brushes.Black, 1), new Point(40, i * 200 + 30));
            }
        }
        var image = new RenderTargetBitmap(720, 2400, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        var doc = new CaptureDocument(image); doc.Add(new Annotation(AnnotationTool.Redact, Colors.Black, 1, [new Point(30, 120), new Point(400, 165)]));
        var rendered = new ImageExporter().Render(doc);
        byte[] alphaPixels = new byte[320 * 160 * 4];
        for (int y = 0; y < 160; y++) for (int x = 0; x < 320; x++)
        { int p = (y * 320 + x) * 4; alphaPixels[p] = 60; alphaPixels[p + 1] = 120; alphaPixels[p + 2] = 220; alphaPixels[p + 3] = (byte)(x * 255 / 319); }
        var alpha = BitmapSource.Create(320, 160, 96, 96, PixelFormats.Bgra32, null, alphaPixels, 1280); alpha.Freeze();
        var captures = new[] { new PdfCapture("Documento", rendered), new PdfCapture("Captura corta", OcrFixture()), new PdfCapture("Transparencia", alpha) };
        for (int i = 0; i < captures.Length; i++) new ImageExporter().SaveTo(captures[i].Image, Path.Combine(_output, $"pdf-source-{i}.png"), ImageFileFormat.Png, 95);
        string path = Path.Combine(_output, "capture-tools.pdf");
        var oldCulture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES"); new PdfExporter().Save(captures, new(), path); }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        byte[] before = File.ReadAllBytes(path);
        Check(Encoding.ASCII.GetString(before[..8]) == "%PDF-1.4" && Encoding.ASCII.GetString(before[^6..]) == "%%EOF\n", "PDF incompleto.");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Throws<OperationCanceledException>(() => new PdfExporter().Save(captures, new(), path, token: stop.Token));
        Check(File.ReadAllBytes(path).SequenceEqual(before) && !Directory.EnumerateFiles(_output, "capture-tools.pdf.*.tmp").Any(), "La cancelación dañó un archivo anterior o dejó temporales.");
        Throws<ArgumentException>(() => new PdfExporter().Save(captures, new(), Path.Combine(_output, "falso.png")));
    }
    private static void TestPdfWindow()
    {
        var first = new PdfCapture("Primera captura", Pattern(300, 200));
        var second = new PdfCapture("Segunda captura", ScrollPattern(300, 1600));
        var window = new PdfExportWindow(_output, first); window.Captures.Add(second); window.MoveCapture(1, 0);
        Check(window.Captures[0] == second && ((Button)window.FindName("ExportButton")).IsEnabled, "No permite ordenar capturas.");
        ((ComboBox)window.FindName("PaperBox")).SelectedIndex = 1;
        ((ComboBox)window.FindName("OrientationBox")).SelectedIndex = 1;
        RenderContent(window, 744, 621, "pdf-export.png");
        window.MoveCapture(0, 1); window.RemoveSelected();
        Check(window.Captures.Count == 1 && window.Captures[0] == first, "Quitar una captura elimina otra.");
        var loaded = PdfExportWindow.LoadImage(Path.Combine(_output, "pdf-source-0.png"), PdfLayout.MaxTotalPixels);
        Check(loaded.IsFrozen && loaded.PixelHeight == 2400, "No conserva una imagen cargada del disco.");
        Throws<ArgumentException>(() => PdfExportWindow.LoadImage(Path.Combine(_output, "pdf-source-0.png"), 10));
    }
    private sealed class QuickOcrFixture : IOcrEngine
    {
        public string Name => "Prueba local";
        public TaskCompletionSource<OcrResult>? Pending;
        public string Text = "Acción local. Español e inglés. 12345.";
        public bool Fail;
        public Task<OcrResult> RecognizeAsync(BitmapSource image, string language, CancellationToken token) => Pending?.Task ?? (Fail ? Task.FromException<OcrResult>(new IOException("Error simulado")) : Task.FromResult(new OcrResult(Text, [])));
    }
    private static void TestQuickOcr(App app)
    {
        foreach (var engine in new[] { new QuickOcrFixture(), new QuickOcrFixture { Text = "  " }, new QuickOcrFixture { Fail = true } })
        {
            int copies = 0; string? text = null;
            var window = new OcrWindow(engine, OcrFixture(), value => { copies++; text = value; return Task.CompletedTask; }, autoCopy: true);
            window.ExtractAsync().GetAwaiter().GetResult();
            Check(copies == (engine.Fail || string.IsNullOrWhiteSpace(engine.Text) ? 0 : 1), "Copia un OCR vacío o fallido, o no copia el resultado válido.");
            if (copies == 1) { Check(text == engine.Text, "El OCR rápido perdió Unicode."); RenderContent(window, 804, 641, "quick-ocr.png"); }
            window.Close();
        }
        var delayed = new QuickOcrFixture { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) }; int lateCopies = 0;
        var unavailableClipboard = new OcrWindow(new QuickOcrFixture(), OcrFixture(), _ => throw new IOException("Portapapeles ocupado"), autoCopy: true);
        unavailableClipboard.ExtractAsync().GetAwaiter().GetResult();
        Check(((TextBox)unavailableClipboard.FindName("ResultBox")).Text.Contains("Acción") && ((Button)unavailableClipboard.FindName("CopyTextButton")).IsEnabled && ((TextBlock)unavailableClipboard.FindName("OcrStatus")).Text.Contains("no se pudo copiar"), "Un fallo del portapapeles pierde el texto o no permite reintentar.");
        unavailableClipboard.Close();
        var canceled = new OcrWindow(delayed, OcrFixture(), _ => { lateCopies++; return Task.CompletedTask; }, autoCopy: true);
        var job = canceled.ExtractAsync(); canceled.Close(); delayed.Pending.SetResult(new("Resultado tardío", []));
        PumpUntil(job); Check(lateCopies == 0, "Cerrar el OCR cambia el portapapeles con un resultado tardío.");
        var selector = new FloatingCaptureWindow(app, new DesktopFrame(Pattern(800, 600), new PixelBounds(0, 0, 800, 600)), quickOcr: true);
        selector.SelectRegion(new(20, 30, 300, 180));
        Check(((FrameworkElement)selector.FindName("ActionsPanel")).Visibility == Visibility.Collapsed && ((FrameworkElement)selector.FindName("ToolsPanel")).Visibility == Visibility.Collapsed, "El OCR rápido abre las herramientas de imagen.");
        selector.Close();
    }
    private static void PumpUntil(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        Check(task.IsCompleted, "Una operación de interfaz no terminó."); task.GetAwaiter().GetResult();
    }
    private static void TestOcrSettingsMigration()
    {
        var settings = new AppSettings { SaveFolder = _output, Shortcut = new(ModifierKeys.Control | ModifierKeys.Alt, Key.F7), OcrShortcut = new(ModifierKeys.Control | ModifierKeys.Alt, Key.F8) };
        var store = new JsonSettingsStore(Path.Combine(_output, "ocr-shortcuts.json")); store.Save(settings);
        Check(store.Load() == settings, "No persisten ambos atajos.");
        Throws<ArgumentException>(() => (settings with { OcrShortcut = settings.Shortcut }).Validate());
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(settings with { Shortcut = ShortcutSettings.OcrDefault }))!;
        legacy.AsObject().Remove(nameof(AppSettings.OcrShortcut)); File.WriteAllText(Path.Combine(_output, "ocr-shortcuts.json"), legacy.ToJsonString());
        var migrated = store.Load();
        Check(migrated.SaveFolder == _output && migrated.Shortcut == ShortcutSettings.OcrDefault && migrated.Shortcut != migrated.OcrShortcut && store.LoadWarning is null, "Migrar OCR perdió las preferencias o el atajo anterior.");
        File.WriteAllText(Path.Combine(_output, "ocr-shortcuts.json"), "null");
        Check(store.Load() == new AppSettings(), "Una configuración nula impide iniciar la aplicación.");
    }
    private static void TestHotkeyPair()
    {
        using var source = new HwndSource(new HwndSourceParameters("DeskShotPair") { ParentWindow = new IntPtr(-3) });
        using var otherSource = new HwndSource(new HwndSourceParameters("DeskShotConflict") { ParentWindow = new IntPtr(-3) });
        using var bindings = new CaptureHotkeys(source.Handle); using var occupied = new GlobalHotkey(otherSource.Handle);
        var capture = new ShortcutSettings(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F17);
        var ocr = capture with { Key = Key.F18 }; var busy = capture with { Key = Key.F21 };
        bindings.Apply(capture, ocr); bindings.Apply(ocr, capture);
        Check(bindings.Capture == ocr && bindings.Ocr == capture, "No permite intercambiar los atajos.");
        occupied.Register(busy);
        Throws<System.ComponentModel.Win32Exception>(() => bindings.Apply(capture, busy));
        Check(bindings.Capture == ocr && bindings.Ocr == capture, "Un conflicto perdió los registros anteriores.");
        Throws<IOException>(() => bindings.Apply(capture with { Key = Key.F22 }, ocr, () => throw new IOException("No se pudo guardar")));
        Check(bindings.Capture == ocr && bindings.Ocr == capture, "Un fallo de persistencia cambia los atajos.");
    }
    private static int ShowSmartDocument(App app)
    {
        var content = new StackPanel { Background = Brushes.White };
        for (int i = 1; i <= 8; i++)
        {
            var block = new StackPanel { Height = 220, Margin = new Thickness(20, 0, 20, 0), Background = i % 2 == 0 ? Brushes.AliceBlue : Brushes.Honeydew };
            block.Children.Add(new TextBlock { Text = $"Bloque {i:00} · Documento completo", Foreground = Brushes.Navy, FontSize = 28, Margin = new Thickness(12, 24, 0, 12) });
            block.Children.Add(new TextBlock { Text = $"Información local {i * 173}.\nOCR sin internet. Píxeles originales.\nFINAL BLOQUE {i:00}", Foreground = Brushes.Black, FontSize = 22, Margin = new Thickness(12) });
            content.Children.Add(block);
        }
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, CanContentScroll = false };
        var viewport = new Grid(); viewport.Children.Add(scroll);
        var sticky = new Border { Height = 48, Background = Brushes.MidnightBlue, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Child = new TextBlock { Text = "ENCABEZADO FIJO DE PRUEBA", Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(12) } };
        viewport.Children.Add(sticky);
        // Dejar los primeros 48 px del contenido vacíos: así excluir la superposición no oculta texto.
        content.Children.Insert(0, new Border { Height = 48 });
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) }); root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(new TextBlock { Text = "BARRA EXTERIOR · NO INCLUIR", FontSize = 22, Foreground = Brushes.White, Margin = new Thickness(20) });
        Grid.SetRow(viewport, 1); root.Children.Add(viewport);
        var window = new Window { Title = "DeskShot · Documento de prueba inteligente", Content = root, Width = 760, Height = 580, Left = 80, Top = 80, Topmost = true, Background = Brushes.DarkSlateGray, UseLayoutRounding = true };
        window.Loaded += (_, _) =>
        {
            // Selección deliberadamente imprecisa: incluye barra exterior y scrollbar.
            var start = root.PointToScreen(new Point()); var end = root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight));
            File.WriteAllText(Path.Combine(_output, "smart-demo-bounds.txt"), $"{(int)start.X},{(int)start.Y},{(int)(end.X - start.X)},{(int)(end.Y - start.Y)}");
        };
        window.Closed += (_, _) => app.Shutdown(); app.Run(window); return 0;
    }
    private static int ShowSmartDemo(App app)
    {
        int[] b = File.ReadAllText(Path.Combine(_output, "smart-demo-bounds.txt")).Split(',').Select(int.Parse).ToArray();
        var window = new ScrollingCaptureWindow(new(b[0], b[1], b[2], b[3]));
        if (window.ShowDialog() == true && window.Result is { } result)
        {
            new ImageExporter().SaveTo(result.Image, Path.Combine(_output, "smart-demo-result.png"), ImageFileFormat.Png, 95);
            File.WriteAllText(Path.Combine(_output, "smart-demo-result.txt"), $"{result.Image.PixelWidth} × {result.Image.PixelHeight}; partes={result.Frames}; {result.Message}");
        }
        app.Shutdown(); return 0;
    }
    private static int ShowCaptureTools(App app)
    {
        var window = new PdfExportWindow(_output, new("Captura de prueba", OcrFixture()));
        window.Captures.Add(new("Documento largo", ScrollPattern(300, 1800)));
        window.Closed += (_, _) => app.Shutdown(); app.Run(window); return 0;
    }
    private static int ShowQuickDemo(App app)
    {
        var image = OcrFixture();
        var selector = new FloatingCaptureWindow(app, new(image, new(140, 140, image.PixelWidth, image.PixelHeight)), quickOcr: true) { ShowInTaskbar = true, Title = "DeskShot · Selector OCR de prueba" };
        if (selector.ShowDialog() == true && selector.ResultAction == FloatingCaptureAction.QuickOcr)
        {
            var window = new OcrWindow(app.OcrEngine, ImageExporter.RenderForOcr(selector.Document), text =>
            {
                File.WriteAllText(Path.Combine(_output, "quick-demo-text.txt"), text); return Task.CompletedTask;
            }, autoCopy: true);
            window.ShowDialog();
        }
        app.Shutdown(); return 0;
    }
    private static void VerifySmartResult()
    {
        var image = Load(Path.Combine(_output, "smart-demo-result.png"));
        var result = new TesseractOcrEngine().RecognizeAsync(image, "spa+eng", CancellationToken.None).GetAwaiter().GetResult();
        for (int i = 1; i <= 8; i++)
            Check(System.Text.RegularExpressions.Regex.IsMatch(result.Text, @"BLOQUE\s+0?" + i + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), "Falta texto del bloque " + i + ": " + result.Text);
        Check(!result.Text.Contains("ENCABEZADO", StringComparison.OrdinalIgnoreCase) && !result.Text.Contains("BARRA EXTERIOR", StringComparison.OrdinalIgnoreCase), "Se incluyó un encabezado fijo.");
        Console.WriteLine($"  Documento real: {image.PixelWidth} × {image.PixelHeight} px; ocho bloques y ausencia de encabezados comprobados con OCR local.");
    }
}
