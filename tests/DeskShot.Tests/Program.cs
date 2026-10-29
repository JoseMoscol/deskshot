using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskShot;
using DeskShot.Domain;
using DeskShot.Infrastructure;
using DeskShot.Presentation;
using DeskShot.Application;

internal static partial class Program
{
    private static int _passed;
    private static int _failed;
    private static string _output = "";

    [STAThread]
    private static int Main(string[] args)
    {
        _output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/verification");
        Directory.CreateDirectory(_output);
        var app = new App(verificationMode: true);
        app.InitializeComponent();
        if (args.Contains("--target-document")) return ShowTargetDocument(app);
        if (args.Contains("--target-selector")) return ShowTargetSelector(app);
        if (args.Contains("--verify-target-selector")) { Run("Selector real: clic en panel y OCR local", VerifyTargetResult); return _failed == 0 ? 0 : 1; }
        if (args.Contains("--floating-demo")) return ShowFloatingDemo(app);
        if (args.Contains("--features-demo")) return ShowFeaturesDemo(app);
        if (args.Contains("--updates-demo")) return ShowUpdatesDemo(app);
        if (args.Contains("--capture-tools-demo")) return ShowCaptureTools(app);
        if (args.Contains("--quick-ocr-demo")) return ShowQuickDemo(app);
        if (args.Contains("--smart-scroll-document")) return ShowSmartDocument(app);
        if (args.Contains("--smart-scroll-demo")) return ShowSmartDemo(app);
        if (args.Contains("--verify-smart-scroll-demo")) { Run("Documento real: ocho bloques y exclusión de encabezados", VerifySmartResult); return _failed == 0 ? 0 : 1; }
        if (args.Contains("--update-network")) { Run("Actualizaciones: descarga real de GitHub sin instalar", TestUpdateNetwork); return _failed == 0 ? 0 : 1; }
        if (args.Contains("--update-handoff")) { Run("Actualizaciones: espera, instalación y reinicio con ejecutables sintéticos", TestUpdateHandoff); return _failed == 0 ? 0 : 1; }
        if (args.Contains("--scroll-document")) return ShowScrollDocument(app);
        if (args.Contains("--scroll-demo")) return ShowScrollDemo(app);
        if (args.Contains("--verify-scroll-demo")) { Run("Documento real: 24 secciones capturadas y OCR local", TestRealScrollResult); return _failed == 0 ? 0 : 1; }
        Run("Selección inversa, subpíxeles y límites", TestSelections);
        Run("Historial de anotaciones y original inmutable", TestHistory);
        Run("PNG 4K: dimensiones y todos los píxeles intactos", TestLossless4K);
        Run("JPG 4K: formato, dimensiones y calidad validada", TestJpeg4K);
        Run("Anotaciones exportadas en coordenadas de imagen", TestAnnotations);
        Run("Configuración persistente y recuperación de JSON inválido", TestSettings);
        Run("Atajos válidos y formato legible", TestShortcuts);
        if (!args.Contains("--skip-hotkeys")) Run("Atajo ocupado conserva el registro anterior", TestHotkeys);
        else Console.WriteLine("SKIP Registro global de atajos (entorno restringido)");
        Run("Interfaz principal, configuración y editor renderizables", () => RenderPreviews(app));
        Run("OCR local bilingüe: acentos, números y líneas", TestOcrBilingual);
        Run("OCR local: texto blanco en fondo oscuro", TestOcrDark);
        Run("OCR local: imagen sin texto", TestOcrBlank);
        Run("OCR local: modelos ausentes, idioma inválido y cancelación", TestOcrErrors);
        Run("Ventana OCR: extraer, corregir y copiar texto Unicode", TestOcrWindow);
        Run("Selección: prioridad visual y detección precisa de trazos", TestAnnotationSelection);
        Run("Mover y redimensionar: un gesto, un deshacer", TestAnnotationGestures);
        Run("Geometría: esquinas, flechas, límites y líneas planas", TestAnnotationGeometry);
        Run("Texto: Unicode, tamaño, edición y exportación nativa", TestTextAnnotations);
        Run("Historial: eliminar, restaurar y volver a la revisión guardada", TestEditHistory);
        Run("Editor: herramientas, propiedades de selección y eliminación", TestEditorControls);
        Run("Área flotante: ocho tiradores, desplazamiento y límites", TestRegionGeometry);
        Run("Exportación flotante: recorte exacto sin barras ni máscara", TestRegionExport);
        Run("Recorte y anotaciones: historial y traslado al editor", TestRegionHistory);
        Run("Barras flotantes: posiciones dentro de cada pantalla", TestFloatingLayout);
        Run("OCR local de la región original tras anotar", TestRegionOcr);
        Run("Modo flotante: herramientas y previsualización", () => TestFloatingWindow(app));
        Run("Desplazamiento: unión exacta y último avance pequeño", TestScrollPixels);
        Run("Desplazamiento: contenido repetido y uniones inseguras", TestScrollAmbiguity);
        Run("Desplazamiento: límites sin reducción ni modificación", TestScrollLimits);
        Run("Desplazamiento: fin sin duplicados y resultado parcial", TestScrollSession);
        Run("Desplazamiento: cancelación conserva la captura", TestScrollCancellation);
        Run("Captura larga: PNG, anotaciones y editor a resolución nativa", () => TestLongCapture(app));
        Run("Interfaz de captura con desplazamiento", () => TestScrollWindow(app));
        Run("OCR local de captura larga por bloques y coordenadas originales", TestLongOcr);
        Run("Censura sólida: opaca, prioritaria y sin modificar el original", TestSolidRedaction);
        Run("Pixelado y desenfoque: bordes, intensidad y píxeles fuera intactos", TestRedactionFilters);
        Run("Censura y recorte: continuidad exacta al abrir el editor", TestRedactionCrop);
        Run("Pasos: numeración, edición, tamaño e historial", TestSteps);
        Run("OCR local respeta censura y omite otras anotaciones", TestRedactedOcr);
        Run("Anclado: copia independiente, zoom y opacidad", () => TestPinnedCapture(app));
        Run("Interfaz: censura, pasos y propiedades compartidas", () => TestFeatureControls(app));
        Run("Actualizaciones: versiones estables y origen del instalador", TestUpdateMetadata);
        Run("Actualizaciones: descarga, SHA-256 y caché verificada", TestUpdateDownload);
        Run("Actualizaciones: datos corruptos, truncados y rutas ajenas", TestUpdateFailures);
        Run("Actualizaciones: redirecciones HTTPS permitidas y bloqueadas", TestUpdateRedirects);
        Run("Actualizaciones: cancelación limpia archivos incompletos", TestUpdateCancellation);
        Run("Actualizaciones: controles de búsqueda, descarga y errores", () => TestUpdateWindow(app));
        Run("Desplazamiento inteligente: encabezados, laterales y scrollbar", TestSmartScroll);
        Run("Desplazamiento inteligente: ambigüedad y conservación del parcial", TestSmartScrollAmbiguity);
        Run("PDF: reparto de filas, papel y límites", TestPdfLayout);
        Run("PDF: archivo completo, cultura, censura y cancelación", TestPdfWriter);
        Run("PDF: controles, orden y carga de imágenes", TestPdfWindow);
        Run("OCR rápido: copia local, vacío, error y cierre", () => TestQuickOcr(app));
        Run("OCR rápido: dos atajos y migración de preferencias", TestOcrSettingsMigration);
        Run("Selector de áreas: coordenadas físicas, jerarquía y clic", TestTargetGeometry);
        Run("Selector de áreas: vista previa, niveles y salida sin bordes", () => TestTargetPreview(app));
        Run("Selector de áreas: consultas tardías, errores y cierre", () => TestTargetAsync(app));
        if (!args.Contains("--skip-hotkeys")) Run("Atajos de captura y OCR: intercambio y rollback", TestHotkeyPair);
        if (args.Contains("--desktop")) Run("Captura real del escritorio a dimensiones nativas", TestDesktopCapture);
        Console.WriteLine($"\n{_passed} pruebas correctas; {_failed} fallidas. Evidencias: {_output}");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + "\n" + ex); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Se esperaba " + typeof(T).Name);
    }

    private static BitmapSource Pattern(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = (y * width + x) * 4;
            pixels[index] = (byte)((x / 8 + y / 8) % 256);
            pixels[index + 1] = (byte)((y / 6) % 256);
            pixels[index + 2] = (byte)((x / 9) % 256);
            pixels[index + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        byte[] bytes = new byte[rgb.PixelWidth * rgb.PixelHeight * 3];
        rgb.CopyPixels(bytes, rgb.PixelWidth * 3, 0);
        return bytes;
    }

    private static BitmapSource Load(string path)
    {
        using var file = File.OpenRead(path);
        return BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static void TestSelections()
    {
        Check(SelectionMath.FromPoints(new Point(150, 120), new Point(10, 20), 1920, 1080) == new Int32Rect(10, 20, 140, 100), "Arrastre inverso incorrecto.");
        Check(SelectionMath.FromPoints(new Point(-50, -10), new Point(2000, 1200), 1920, 1080) == new Int32Rect(0, 0, 1920, 1080), "No se ajusta al escritorio.");
        Check(SelectionMath.FromPoints(new Point(1.3, 2.2), new Point(3.8, 5.1), 50, 50) == new Int32Rect(1, 2, 3, 4), "Se pierden píxeles en bordes fraccionarios.");
        Check(SelectionMath.FromPoints(new Point(40, 40), new Point(40, 40), 50, 50).Width == 0, "Un clic no debe seleccionar un área.");
    }

    private static Annotation Line(int x = 10) => new(AnnotationTool.Pen, Colors.Red, 4, new[] { new Point(x, 10), new Point(x + 30, 40) });

    private static void TestHistory()
    {
        var writable = new WriteableBitmap(Pattern(100, 100));
        var document = new CaptureDocument(writable);
        var before = Pixels(document.Original);
        writable.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 0, 0, 0, 0 }, 4, 0);
        document.Add(Line()); document.Add(Line(20));
        Check(document.Annotations.Count == 2 && document.CanUndo, "No se añaden anotaciones.");
        document.Undo(); document.Undo(); document.Undo();
        Check(document.Annotations.Count == 0 && document.CanRedo, "Deshacer no funciona.");
        document.Redo(); document.Add(Line(50));
        Check(!document.CanRedo && document.Annotations.Count == 2, "Nueva edición debe borrar la rama de rehacer.");
        Check(Pixels(document.Original).SequenceEqual(before), "La imagen original fue modificada.");
        var points = new List<Point> { new(0, 0), new(9, 9) };
        document.Add(new Annotation(AnnotationTool.Arrow, Colors.White, 3, points));
        points.Clear();
        Check(document.Annotations[^1].Points.Count == 2, "El historial comparte una lista mutable.");
    }

    private static void TestLossless4K()
    {
        var image = Pattern(3840, 2160);
        var exporter = new ImageExporter();
        var doc = new CaptureDocument(image);
        string path = exporter.Save(exporter.Render(doc), _output, ImageFileFormat.Png, 95);
        var result = Load(path);
        Check(result.PixelWidth == 3840 && result.PixelHeight == 2160, "Se redujo la resolución 4K.");
        Check(Pixels(image).SequenceEqual(Pixels(result)), "PNG modificó los píxeles.");
        string second = exporter.Save(image, _output, ImageFileFormat.Png, 95);
        Check(path != second && File.Exists(path) && File.Exists(second), "Se sobrescribió una captura.");
        Check(!Directory.EnumerateFiles(_output, "*.tmp").Any(), "Quedaron archivos temporales.");
    }

    private static void TestJpeg4K()
    {
        var exporter = new ImageExporter();
        var image = Pattern(3840, 2160);
        string path = exporter.Save(image, _output, ImageFileFormat.Jpeg, 95);
        var result = Load(path);
        Check(result.PixelWidth == 3840 && result.PixelHeight == 2160, "JPG cambió las dimensiones.");
        var signature = File.ReadAllBytes(path).Take(2).ToArray();
        Check(signature.SequenceEqual(new byte[] { 255, 216 }), "El archivo no es JPEG.");
        Throws<ArgumentOutOfRangeException>(() => exporter.Save(image, _output, ImageFileFormat.Jpeg, 101));
        Throws<ArgumentOutOfRangeException>(() => exporter.Save(image, _output, ImageFileFormat.Jpeg, 0));
    }

    private static void TestAnnotations()
    {
        var doc = new CaptureDocument(Pattern(800, 600));
        var original = Pixels(doc.Original);
        doc.Add(new Annotation(AnnotationTool.Rectangle, Colors.Red, 8, new[] { new Point(100, 100), new Point(700, 500) }));
        doc.Add(new Annotation(AnnotationTool.Arrow, Colors.Lime, 6, new[] { new Point(150, 200), new Point(500, 350) }));
        doc.Add(new Annotation(AnnotationTool.Highlight, Colors.Yellow, 4, new[] { new Point(200, 400), new Point(600, 450) }));
        var exporter = new ImageExporter();
        var rendered = exporter.Render(doc);
        Check(rendered.PixelWidth == 800 && rendered.PixelHeight == 600, "El editor cambió el tamaño.");
        var pixels = Pixels(rendered);
        int topEdge = (100 * 800 + 400) * 3;
        Check(pixels[topEdge] == 0 && pixels[topEdge + 1] == 0 && pixels[topEdge + 2] == 255, "El rectángulo no está en sus coordenadas originales.");
        Check(pixels.Take(3).SequenceEqual(original.Take(3)), "Se modificó una zona no anotada.");
        Check(Pixels(doc.Original).SequenceEqual(original), "Exportar modificó el original.");
        exporter.SaveTo(rendered, Path.Combine(_output, "annotations.png"), ImageFileFormat.Png, 95);
    }

    private static void TestSettings()
    {
        string path = Path.Combine(_output, "settings-test.json");
        var store = new JsonSettingsStore(path);
        var settings = new AppSettings
        {
            SaveFolder = _output, Format = ImageFileFormat.Jpeg, JpegQuality = 88, CaptureDelaySeconds = 3,
            AutoSave = true, CopyAfterCapture = false, CheckUpdatesOnStartup = true, Shortcut = new(ModifierKeys.Control | ModifierKeys.Alt, Key.F8)
        };
        store.Save(settings);
        Check(store.Load() == settings, "La configuración no persiste correctamente.");
        Throws<ArgumentException>(() => store.Save(settings with { JpegQuality = 101 }));
        Check(store.Load() == settings, "Un valor inválido dañó la configuración anterior.");
        File.WriteAllText(path, "JSON roto");
        Check(store.Load().Format == ImageFileFormat.Png && store.LoadWarning is not null, "No se recuperó la configuración inválida.");
        Check(File.ReadAllText(path) == "JSON roto", "Se sobrescribió el JSON inválido sin pedir guardar.");
        Throws<ArgumentException>(() => new AppSettings { SaveFolder = "relativa" }.Validate());
    }

    private static void TestShortcuts()
    {
        Check(ShortcutSettings.Default.ToString() == "Ctrl + Shift + 2", "Formato de atajo inesperado.");
        Check(ShortcutSettings.Default.IsValid, "El atajo inicial no es válido.");
        Check(!new ShortcutSettings(ModifierKeys.None, Key.A).IsValid, "Se permiten letras sin modificador.");
        Check(!new ShortcutSettings(ModifierKeys.Control, Key.LeftCtrl).IsValid, "Se acepta un modificador sin tecla.");
        Check(new ShortcutSettings(ModifierKeys.Alt, Key.F9).IsValid, "No se aceptan teclas F.");
    }

    private static void TestHotkeys()
    {
        // Ventanas de mensajes sin interfaz; verifica RegisterHotKey real y su rollback.
        using var source1 = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("DeskShotTest1") { ParentWindow = new IntPtr(-3) });
        using var source2 = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("DeskShotTest2") { ParentWindow = new IntPtr(-3) });
        using var first = new GlobalHotkey(source1.Handle);
        using var second = new GlobalHotkey(source2.Handle);
        var previous = new ShortcutSettings(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F19);
        var occupied = new ShortcutSettings(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F20);
        first.Register(previous);
        second.Register(occupied);
        Throws<System.ComponentModel.Win32Exception>(() => first.Register(occupied));
        Check(first.Current == previous, "Un conflicto perdió el atajo anterior.");
        first.Register(previous);
    }

    private static void RenderPreviews(App app)
    {
        var main = new MainWindow(app);
        Check(main.Foreground is SolidColorBrush { Color: var foreground } && foreground == Color.FromRgb(241, 245, 250), "El tema oscuro no aplica el color legible de texto.");
        RenderContent(main, 1064, 741, "home.png");
        ((FrameworkElement)main.FindName("HomePanel")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)main.FindName("SettingsPanel")).Visibility = Visibility.Visible;
        RenderContent(main, 1064, 741, "settings.png");
        var document = new CaptureDocument(DemoImage());
        document.Add(new Annotation(AnnotationTool.Rectangle, Color.FromRgb(255, 107, 107), 5, new[] { new Point(90, 230), new Point(650, 440) }));
        document.Add(new Annotation(AnnotationTool.Arrow, Color.FromRgb(120, 231, 195), 5, new[] { new Point(1100, 600), new Point(730, 370) }));
        var text = AnnotationTextLayout.Create("Tu idea, bien explicada.", new Point(760, 630), Colors.Black, 36, true, new Size(1440, 900));
        document.Add(text);
        var editor = new EditorWindow(app, document, true, true);
        var surface = (AnnotationSurface)((Border)editor.FindName("SurfaceHost")).Child;
        surface.Editor.Select(text.Id);
        RenderContent(editor, 1164, 781, "editor.png");
        RenderContent(editor, 944, 581, "editor-small.png");
        var dialog = new AnnotationTextDialog((value, size, bold) => AnnotationTextLayout.Create(value, new Point(100, 100), Colors.Black,
            size, bold, new Size(1440, 900)), text);
        RenderContent(dialog, 544, 391, "text-dialog.png");
    }

    private static BitmapSource DemoImage()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(237, 242, 248)), null, new Rect(0, 0, 1440, 900));
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(52, 50, 1336, 800), 18, 18);
            void Text(string text, double x, double y, int size, Color color) => dc.DrawText(new FormattedText(text,
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, new SolidColorBrush(color), 1), new Point(x, y));
            Text("PROYECTO / IDEAS", 100, 98, 18, Color.FromRgb(78, 99, 125));
            Text("Los detalles hacen la diferencia.", 100, 144, 45, Color.FromRgb(28, 43, 62));
            Text("Una captura clara. Una idea bien explicada.", 110, 260, 29, Color.FromRgb(52, 79, 108));
            Text("Anota lo que quieres destacar y compártelo con tu equipo.", 110, 320, 22, Color.FromRgb(82, 101, 125));
            Text("Todo lo que dibujas se guarda sobre la imagen original.", 110, 370, 22, Color.FromRgb(82, 101, 125));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(220, 242, 233)), null, new Rect(110, 520, 510, 230), 12, 12);
            Text("01", 138, 546, 40, Color.FromRgb(31, 113, 88));
            Text("Captura a resolución nativa", 138, 620, 24, Color.FromRgb(31, 113, 88));
            Text("Texto nítido, hasta el último píxel.", 138, 668, 19, Color.FromRgb(31, 113, 88));
        }
        var image = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); image.Freeze(); return image;
    }

    private static void RenderContent(Window window, int width, int height, string file)
    {
        var content = (FrameworkElement)window.Content;
        if (content is Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(14, 19, 27));
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 19, 27)), null, new Rect(0, 0, width, height));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        new ImageExporter().SaveTo(bitmap, Path.Combine(_output, file), ImageFileFormat.Png, 95);
        Check(bitmap.PixelWidth == width, "El render de la interfaz no coincide.");
    }

    private static void TestDesktopCapture()
    {
        var frame = new GdiCaptureService().CaptureDesktop();
        Check(frame.Image.PixelWidth == frame.Bounds.Width && frame.Image.PixelHeight == frame.Bounds.Height, "La captura real no coincide con el escritorio físico.");
        Check(frame.Image.IsFrozen, "La captura no puede enviarse a otro hilo.");
        // Solo se registra el tamaño; no se guarda el contenido del escritorio.
        Console.WriteLine($"  Escritorio físico: {frame.Bounds.Width} × {frame.Bounds.Height} px; origen {frame.Bounds.X}, {frame.Bounds.Y}");
    }

    private static BitmapSource OcrFixture(bool dark = false, bool blank = false)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(dark ? Brushes.Black : Brushes.White, null, new Rect(0, 0, 1300, 420));
            if (!blank)
            {
                string[] lines = ["DeskShot OCR funciona sin internet.", "Español: información, acción y número.", "English: Copy this text. Total: 12345."];
                for (int i = 0; i < lines.Length; i++)
                    dc.DrawText(new FormattedText(lines[i], System.Globalization.CultureInfo.GetCultureInfo("es-PE"), FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 40, dark ? Brushes.White : Brushes.Black, 1), new Point(55, 55 + i * 100));
            }
        }
        var image = new RenderTargetBitmap(1300, 420, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); image.Freeze(); return image;
    }

    private static void TestOcrBilingual()
    {
        var image = OcrFixture();
        var before = Pixels(image);
        var result = new TesseractOcrEngine().RecognizeAsync(image, "spa+eng", CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("  OCR: " + result.Text.Replace(Environment.NewLine, " | "));
        Check(result.Text.Contains("sin internet") && result.Text.Contains("12345") && result.Text.Contains("Copy this text"), "El OCR no reconoce el texto bilingüe esperado.");
        Check(result.Text.Contains("información") && result.Text.Contains("acción"), "Se perdieron las tildes.");
        Check(result.Text.Contains(Environment.NewLine), "No se conservaron las líneas.");
        Check(result.Regions.Count > 10 && result.Regions.All(r => r.Bounds.X >= 0 && r.Bounds.Y >= 0 && r.Bounds.X + r.Bounds.Width <= image.PixelWidth && r.Bounds.Y + r.Bounds.Height <= image.PixelHeight), "Las regiones no corresponden a la imagen original.");
        Check(result.Regions.All(r => r.Confidence is >= 0 and <= 1), "Confianza fuera de rango.");
        Check(Pixels(image).SequenceEqual(before), "El OCR modificó la captura.");
        new ImageExporter().SaveTo(image, Path.Combine(_output, "ocr-fixture.png"), ImageFileFormat.Png, 95);
        File.WriteAllText(Path.Combine(_output, "ocr-result.txt"), result.Text);
    }

    private static void TestOcrDark()
    {
        var result = new TesseractOcrEngine().RecognizeAsync(OcrFixture(dark: true), "spa+eng", CancellationToken.None).GetAwaiter().GetResult();
        Check(result.Text.Contains("DeskShot") && result.Text.Contains("12345"), "El OCR no reconoce el fondo oscuro.");
    }

    private static void TestOcrBlank()
    {
        var result = new TesseractOcrEngine().RecognizeAsync(OcrFixture(blank: true), "spa", CancellationToken.None).GetAwaiter().GetResult();
        Check(string.IsNullOrWhiteSpace(result.Text) && result.Regions.Count == 0, "Se inventó texto en una imagen en blanco.");
    }

    private static void TestOcrErrors()
    {
        var image = OcrFixture();
        var engine = new TesseractOcrEngine();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Throws<OperationCanceledException>(() => engine.RecognizeAsync(image, "spa", cancellation.Token).GetAwaiter().GetResult());
        Throws<ArgumentException>(() => engine.RecognizeAsync(image, "../eng", CancellationToken.None).GetAwaiter().GetResult());
        Throws<FileNotFoundException>(() => new TesseractOcrEngine(Path.Combine(_output, "missing-models")).RecognizeAsync(image, "spa", CancellationToken.None).GetAwaiter().GetResult());
        using var midFlight = new CancellationTokenSource();
        var job = engine.RecognizeAsync(image, "eng", midFlight.Token);
        midFlight.Cancel();
        Throws<OperationCanceledException>(() => job.GetAwaiter().GetResult());
    }

    private sealed class FixtureOcrEngine : IOcrEngine
    {
        public string Name => "Prueba de interfaz";
        public int Calls { get; private set; }
        public Task<OcrResult> RecognizeAsync(BitmapSource image, string language, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new OcrResult("Información local.\r\nTotal: 12345.", []));
        }
    }

    private static void TestOcrWindow()
    {
        string? copied = null;
        var engine = new FixtureOcrEngine();
        var window = new OcrWindow(engine, OcrFixture(), text => { copied = text; return Task.CompletedTask; });
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var text = (TextBox)window.FindName("ResultBox");
        var copy = (Button)window.FindName("CopyTextButton");
        Check(text.Text.Contains("Información") && copy.IsEnabled, "El resultado no aparece o no puede copiarse.");
        text.Text = "Corrección: español, inglés y ñ.\r\nSegunda línea.";
        copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(copied == text.Text, "Se copió una versión anterior o se perdió Unicode.");
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(engine.Calls == 1 && text.Text.StartsWith("Corrección"), "Volver a mostrar la ventana sobrescribió la corrección.");
        RenderContent(window, 804, 641, "ocr.png");
        text.Text = "   ";
        Check(!copy.IsEnabled, "Se permite copiar un resultado vacío.");
    }

    private static void TestAnnotationSelection()
    {
        var doc = new CaptureDocument(Pattern(800, 600));
        var back = new Annotation(AnnotationTool.Rectangle, Colors.Red, 4, new[] { new Point(100, 100), new Point(400, 300) });
        var front = back with { Id = Guid.NewGuid(), Color = Colors.Blue };
        doc.Add(back); doc.Add(front);
        using var editor = new AnnotationEditor(doc);
        Check(editor.HitTest(new Point(200, 200), 4)?.Id == front.Id, "No se selecciona el objeto superior.");
        editor.Begin(new Point(200, 200), 4); editor.Commit();
        Check(editor.SelectedId == front.Id, "La selección se perdió al hacer clic.");
        Check(editor.HitTest(new Point(700, 500), 4) is null, "Se selecciona fuera de los objetos.");
        var line = new Annotation(AnnotationTool.Pen, Colors.Black, 4, new[] { new Point(10, 10), new Point(80, 80) });
        Check(AnnotationGeometry.HitTest(line, new Point(50, 50), 3), "El trazo no puede seleccionarse.");
        Check(!AnnotationGeometry.HitTest(line, new Point(15, 75), 3), "Se selecciona una zona vacía del rectángulo del trazo.");
        // El área de clic de un tirador conserva aproximadamente seis DIPs al cambiar el zoom.
        Check(AnnotationGeometry.HitHandle(front, new Point(408, 300), 6 / 0.5) == ResizeHandle.BottomRight, "El tirador no es accesible al 50 %.");
    }

    private static void TestAnnotationGestures()
    {
        var doc = new CaptureDocument(Pattern(800, 600));
        var annotation = new Annotation(AnnotationTool.Rectangle, Colors.Red, 4, new[] { new Point(100, 100), new Point(300, 250) });
        doc.Add(annotation);
        long before = doc.Revision;
        using var editor = new AnnotationEditor(doc);
        editor.Begin(new Point(180, 160), 5);
        editor.Drag(new Point(190, 170)); editor.Drag(new Point(230, 200));
        Check(doc.Revision == before && doc.Find(annotation.Id)!.Points[0] == new Point(100, 100), "La vista previa modifica el documento durante el arrastre.");
        editor.Commit();
        Check(doc.Find(annotation.Id)!.Points[0] == new Point(150, 140), "El movimiento no corresponde al delta del gesto.");
        doc.Undo();
        Check(doc.Find(annotation.Id)!.Points[0] == new Point(100, 100) && doc.Revision == before, "Un arrastre requiere varios deshacer.");
        doc.Redo();
        editor.Select(annotation.Id);
        editor.Begin(new Point(350, 290), 5);
        editor.Drag(new Point(550, 440)); editor.Commit();
        Check(AnnotationGeometry.Bounds(doc.Find(annotation.Id)!) == new Rect(150, 140, 400, 300), "La esquina no redimensiona el objeto.");
        doc.Undo();
        editor.Select(annotation.Id);
        editor.Begin(new Point(200, 200), 5); editor.Drag(new Point(500, 400)); editor.Cancel();
        Check(doc.Find(annotation.Id)!.Points[0] == new Point(150, 140), "Cancelar modificó la anotación.");
    }

    private static void TestAnnotationGeometry()
    {
        var rectangle = new Annotation(AnnotationTool.Rectangle, Colors.Red, 4, new[] { new Point(100, 100), new Point(300, 250) });
        var canvas = new Size(800, 600);
        Check(AnnotationGeometry.Bounds(AnnotationGeometry.Move(rectangle, new Vector(2000, -1000), canvas)) == new Rect(600, 0, 200, 150), "Mover permite salir de la captura.");
        Check(AnnotationGeometry.Bounds(AnnotationGeometry.Resize(rectangle, ResizeHandle.TopLeft, new Point(50, 60), canvas)) == new Rect(50, 60, 250, 190), "La esquina opuesta se movió al redimensionar.");
        foreach (var handle in new[] { ResizeHandle.TopLeft, ResizeHandle.TopRight, ResizeHandle.BottomRight, ResizeHandle.BottomLeft })
        {
            var resized = AnnotationGeometry.Resize(rectangle, handle, new Point(-1000, 5000), canvas);
            var bounds = AnnotationGeometry.Bounds(resized);
            Check(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= 800 && bounds.Bottom <= 600 && bounds.Width > 0 && bounds.Height > 0, "Un tirador cruzado genera coordenadas inválidas.");
        }
        var arrow = new Annotation(AnnotationTool.Arrow, Colors.Blue, 4, new[] { new Point(100, 100), new Point(400, 300) });
        var adjusted = AnnotationGeometry.Resize(arrow, ResizeHandle.End, new Point(700, 80), canvas);
        Check(adjusted.Points[0] == arrow.Points[0] && adjusted.Points[^1] == new Point(700, 80), "Cambiar un extremo mueve el otro extremo de la flecha.");
        var small = arrow with { Points = new[] { new Point(100, 100), new Point(103, 100) } };
        Check(AnnotationGeometry.HitHandle(small, new Point(103, 100), 10) == ResizeHandle.End, "Los tiradores solapados no eligen el más cercano.");
        var horizontal = new Annotation(AnnotationTool.Pen, Colors.Blue, 4, new[] { new Point(100, 100), new Point(400, 100) });
        Check(AnnotationGeometry.Resize(horizontal, ResizeHandle.BottomRight, new Point(500, 200), canvas).Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)), "Redimensionar una línea horizontal genera NaN.");
    }

    private static void TestTextAnnotations()
    {
        var doc = new CaptureDocument(Pattern(1920, 1080));
        var before = Pixels(doc.Original);
        var text = AnnotationTextLayout.Create("Información útil\nEspañol, inglés y ñ", new Point(200, 250), Colors.Black, 36, true, new Size(1920, 1080));
        doc.Add(text);
        var bounds = AnnotationGeometry.Bounds(text);
        var twice = AnnotationGeometry.Resize(text, ResizeHandle.BottomRight, new Point(bounds.X + bounds.Width * 2, bounds.Y + bounds.Height * 2), new Size(1920, 1080));
        Check(Math.Abs(twice.FontSize - 72) < 0.001 && Math.Abs(AnnotationGeometry.Bounds(twice).Width - bounds.Width * 2) < 0.001, "El tamaño de fuente no acompaña al texto al redimensionar.");
        doc.Replace(text.Id, twice);
        var revised = AnnotationTextLayout.Create("Texto corregido", twice.Points[0], Colors.Red, 44, false, new Size(1920, 1080), text.Id);
        doc.Replace(text.Id, revised);
        Check(doc.Find(text.Id)!.Text == "Texto corregido" && doc.Annotations.Count == 1, "Editar texto crea otro objeto o pierde su identidad.");
        doc.Undo(); Check(doc.Find(text.Id)!.FontSize == 72, "No se restaura el tamaño anterior del texto.");
        var exporter = new ImageExporter();
        var output = exporter.Render(doc);
        Check(output.PixelWidth == 1920 && output.PixelHeight == 1080, "El texto reduce el tamaño exportado.");
        Check(!Pixels(output).SequenceEqual(before) && Pixels(doc.Original).SequenceEqual(before), "El texto no se exporta o modifica el original.");
        exporter.SaveTo(output, Path.Combine(_output, "text-annotations.png"), ImageFileFormat.Png, 95);
        Throws<ArgumentException>(() => AnnotationTextLayout.Create("  ", new Point(0, 0), Colors.Black, 32, false, new Size(1920, 1080)));
        Throws<ArgumentException>(() => AnnotationTextLayout.Create("Demasiado grande", new Point(0, 0), Colors.Black, 500, false, new Size(100, 50)));
    }

    private static void TestEditHistory()
    {
        var doc = new CaptureDocument(Pattern(800, 600));
        var annotation = Line();
        doc.Add(annotation);
        long savedRevision = doc.Revision;
        using var editor = new AnnotationEditor(doc);
        editor.Select(annotation.Id);
        editor.UpdateSelected(annotation with { Color = Colors.Blue, Width = 8 });
        editor.DeleteSelected();
        Check(doc.Annotations.Count == 0 && doc.CanUndo && editor.Selected is null, "Eliminar no vacía la selección o pierde el historial.");
        doc.Undo(); Check(doc.Find(annotation.Id)!.Color == Colors.Blue, "Deshacer eliminar no restaura el objeto editado.");
        doc.Undo(); Check(doc.Revision == savedRevision && doc.Find(annotation.Id)!.Color == annotation.Color, "Deshacer hasta el punto guardado sigue marcando cambios.");
        long sameRevision = doc.Revision;
        doc.Replace(annotation.Id, doc.Find(annotation.Id)!);
        Check(doc.Revision == sameRevision && doc.CanRedo, "Una edición vacía borra rehacer.");
        doc.Replace(annotation.Id, annotation with { Color = Colors.Green });
        Check(!doc.CanRedo && doc.Revision != savedRevision, "Una rama nueva reutiliza la revisión guardada.");
    }

    private static void TestEditorControls()
    {
        var app = (App)System.Windows.Application.Current;
        var doc = new CaptureDocument(Pattern(800, 600));
        var rectangle = new Annotation(AnnotationTool.Rectangle, Colors.Black, 4, new[] { new Point(100, 100), new Point(300, 250) });
        doc.Add(rectangle);
        var window = new EditorWindow(app, doc, saved: true);
        var surface = (AnnotationSurface)((Border)window.FindName("SurfaceHost")).Child;
        var colors = (ComboBox)window.FindName("ColorBox");
        var widths = (ComboBox)window.FindName("WidthBox");
        var tools = (ComboBox)window.FindName("ToolBox");
        var delete = (Button)window.FindName("DeleteButton");
        var edit = (Button)window.FindName("EditTextButton");
        surface.Editor.Select(rectangle.Id);
        Check(delete.IsEnabled && !edit.IsEnabled && widths.IsEnabled && colors.SelectedIndex == 5, "Los controles no reflejan el objeto seleccionado.");
        colors.SelectedIndex = 3; widths.SelectedIndex = 2;
        Check(doc.Find(rectangle.Id)!.Color == Color.FromRgb(87, 166, 255) && doc.Find(rectangle.Id)!.Width == 8, "Cambiar color o grosor no actualiza el objeto seleccionado.");
        tools.SelectedIndex = 5;
        Check(surface.Tool == EditorTool.Text && surface.Editor.Selected is null, "La herramienta de texto no se activa o conserva una selección anterior.");
        var text = AnnotationTextLayout.Create("Texto editable", new Point(100, 300), Colors.Black, 32, false, new Size(800, 600));
        doc.Add(text); tools.SelectedIndex = 0; surface.Editor.Select(text.Id);
        Check(edit.IsEnabled && !widths.IsEnabled, "El texto no habilita su edición o permite un grosor que no corresponde.");
        var exporter = new ImageExporter();
        var output = Pixels(exporter.Render(doc));
        surface.Editor.Begin(new Point(150, 315), 5); surface.Editor.Drag(new Point(200, 350));
        Check(output.SequenceEqual(Pixels(exporter.Render(doc))), "Una selección o vista previa se filtra al archivo exportado.");
        surface.Editor.Cancel();
        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(doc.Find(text.Id) is null && !delete.IsEnabled && !edit.IsEnabled, "El botón Eliminar no actualiza el documento y la selección.");
        doc.Undo(); Check(doc.Find(text.Id)?.Text == "Texto editable", "La eliminación desde la interfaz no puede deshacerse.");
    }

    private static int ShowFloatingDemo(App app)
    {
        void Trace(string message) => File.AppendAllText(Path.Combine(_output, "floating-startup.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
        Trace("Crear imagen de prueba");
        var image = DemoImage();
        Trace("Crear ventana flotante");
        var window = new FloatingCaptureWindow(app, new DesktopFrame(image, new PixelBounds(160, 120, 1440, 900))) { ShowInTaskbar = true };
        Trace("Seleccionar región");
        window.SelectRegion(new Int32Rect(80, 200, 1100, 500));
        window.Loaded += (_, _) => Trace("Loaded");
        window.ContentRendered += (_, _) => Trace("ContentRendered");
        Trace("ShowDialog");
        window.ShowDialog();
        Trace("Cerrado");
        if (window.ResultAction == FloatingCaptureAction.OpenEditor)
        {
            var document = window.Document.CreateRegionDocument();
            new ImageExporter().SaveTo(new ImageExporter().Render(document), Path.Combine(_output, "floating-demo-result.png"), ImageFileFormat.Png, 95);
        }
        Console.WriteLine($"Resultado: {window.ResultAction}; región: {window.Document.Region}; anotaciones: {window.Document.Annotations.Count}");
        app.Shutdown();
        return 0;
    }

    private static void TestRegionGeometry()
    {
        var original = new Int32Rect(100, 100, 400, 300);
        foreach (var (handle, point) in RegionGeometry.Handles(original))
        {
            Check(RegionGeometry.HitHandle(original, point, 5) == handle, "No se detecta uno de los ocho tiradores.");
            var adjusted = RegionGeometry.Adjust(original, handle, point, new Point(-200, 3000), 1920, 1080);
            Check(adjusted.X >= 0 && adjusted.Y >= 0 && adjusted.X + adjusted.Width <= 1920 && adjusted.Y + adjusted.Height <= 1080 && adjusted.Width >= 2 && adjusted.Height >= 2,
                "Un borde cruzado genera un recorte inválido.");
        }
        Check(RegionGeometry.HitHandle(original, new Point(200, 100), 3) == RegionHandle.Top, "Solo se puede arrastrar el punto central, no el borde completo.");
        Check(RegionGeometry.Adjust(original, RegionHandle.Right, new Point(500, 250), new Point(700, 250), 1920, 1080) == new Int32Rect(100, 100, 600, 300), "Cambiar el borde derecho mueve los otros bordes.");
        Check(RegionGeometry.Adjust(original, RegionHandle.Move, new Point(200, 200), new Point(3000, -500), 1920, 1080) == new Int32Rect(1520, 0, 400, 300), "Mover área no conserva el tamaño o sale de pantalla.");
        Check(RegionGeometry.Adjust(original, RegionHandle.New, new Point(700, 600), new Point(200, 100), 1920, 1080) == new Int32Rect(200, 100, 500, 500), "No se puede seleccionar una región arrastrando en sentido inverso.");
    }

    private static void TestRegionExport()
    {
        var original = Pattern(1920, 1080);
        var doc = new CaptureDocument(original);
        var region = new Int32Rect(100, 100, 800, 600);
        doc.SetRegion(region);
        var exporter = new ImageExporter();
        var crop = new CroppedBitmap(original, region);
        Check(Pixels(exporter.Render(doc)).SequenceEqual(Pixels(crop)), "El recorte sin anotaciones altera píxeles.");
        var rectangle = new Annotation(AnnotationTool.Rectangle, Colors.Red, 8, new[] { new Point(150, 150), new Point(700, 500) });
        doc.Add(rectangle);
        var output = exporter.Render(doc);
        Check(output.PixelWidth == 800 && output.PixelHeight == 600, "La exportación no usa las dimensiones finales del área.");
        var pixels = Pixels(output);
        int edge = (50 * 800 + 200) * 3;
        Check(pixels[edge] == 0 && pixels[edge + 1] == 0 && pixels[edge + 2] == 255, "Las anotaciones no se recortan en sus coordenadas físicas.");
        Check(Pixels(doc.OriginalRegion()).SequenceEqual(Pixels(crop)), "El original del OCR incluye anotaciones.");
        exporter.SaveTo(output, Path.Combine(_output, "floating-export.png"), ImageFileFormat.Png, 95);
        Check(doc.Find(rectangle.Id)!.Points[0] == new Point(150, 150), "Exportar traslada el objeto original.");
        Throws<ArgumentException>(() => doc.SetRegion(new Int32Rect(1910, 100, 100, 100)));
        Throws<ArgumentException>(() => doc.SetRegion(new Int32Rect(100, 100, 1, 100)));
    }

    private static void TestRegionHistory()
    {
        var doc = new CaptureDocument(Pattern(1920, 1080));
        var region = new Int32Rect(100, 100, 800, 600);
        doc.SetRegion(region);
        var rectangle = new Annotation(AnnotationTool.Rectangle, Colors.Red, 8, new[] { new Point(150, 150), new Point(700, 500) });
        var outside = new Annotation(AnnotationTool.Rectangle, Colors.Blue, 4, new[] { new Point(1500, 100), new Point(1700, 300) });
        doc.Add(rectangle); doc.Add(outside);
        var oldPoints = doc.Find(rectangle.Id)!.Points.ToArray();
        doc.SetRegion(new Int32Rect(120, 120, 500, 500));
        Check(doc.Find(rectangle.Id)!.Points.SequenceEqual(oldPoints), "Ajustar bordes deforma los dibujos.");
        doc.Undo(); Check(doc.Region == region, "No se deshace el ajuste del área.");
        var rebased = doc.CreateRegionDocument();
        Check(rebased.Width == 800 && rebased.Height == 600 && rebased.Annotations.Count == 1, "El editor recibe la región equivocada o anotaciones que no aparecen en ella.");
        Check(rebased.Find(rectangle.Id)!.Points[0] == new Point(50, 50), "Las anotaciones no se trasladan al origen del editor.");
        var exporter = new ImageExporter();
        Check(Pixels(exporter.Render(doc)).SequenceEqual(Pixels(exporter.Render(rebased))), "Abrir el editor cambia el resultado visual.");
        doc.Redo(); Check(doc.Region.Width == 500, "No se rehace el ajuste de área.");
    }

    private static void TestFloatingLayout()
    {
        var tools = new Size(50, 440); var actions = new Size(410, 58);
        Rect[] selections = [new(100, 100, 700, 500), new(1500, 700, 420, 380), new(0, 0, 1920, 1080), new(4, 4, 20, 20)];
        foreach (var selection in selections)
        {
            var viewport = new Rect(0, 0, 1920, 1080);
            var placement = FloatingToolbarLayout.Place(selection, viewport, tools, actions);
            Check(viewport.Contains(new Rect(placement.Tools, tools)) && viewport.Contains(new Rect(placement.Actions, actions)), "Las barras salen de la pantalla.");
            Check(!new Rect(placement.Tools, tools).IntersectsWith(new Rect(placement.Actions, actions)), "Las barras se solapan.");
        }
        var monitor = new Rect(1920, 100, 1920, 1080);
        var second = FloatingToolbarLayout.Place(new Rect(3000, 500, 700, 600), monitor, tools, actions);
        Check(monitor.Contains(new Rect(second.Tools, tools)) && monitor.Contains(new Rect(second.Actions, actions)), "Las barras se pierden entre monitores.");
    }

    private static void TestRegionOcr()
    {
        var doc = new CaptureDocument(OcrFixture());
        doc.SetRegion(new Int32Rect(40, 40, 1200, 350));
        doc.Add(new Annotation(AnnotationTool.Rectangle, Colors.Red, 12, new[] { new Point(50, 50), new Point(1240, 350) }));
        var result = new TesseractOcrEngine().RecognizeAsync(doc.OriginalRegion(), "spa+eng", CancellationToken.None).GetAwaiter().GetResult();
        Check(result.Text.Contains("sin internet") && result.Text.Contains("12345"), "El OCR no usa la región original de la captura flotante.");
    }

    private static void TestFloatingWindow(App app)
    {
        var image = DemoImage();
        var window = new FloatingCaptureWindow(app, new DesktopFrame(image, new PixelBounds(0, 0, 1440, 900)));
        Check(!window.HasSelection && ((Border)window.FindName("ToolsPanel")).Visibility == Visibility.Collapsed, "Las herramientas aparecen antes de elegir el área.");
        window.SelectRegion(new Int32Rect(80, 220, 1150, 510));
        var rectangle = new Annotation(AnnotationTool.Rectangle, Colors.Red, 5, new[] { new Point(110, 250), new Point(700, 410) });
        window.Document.Add(rectangle);
        ((Button)window.FindName("ArrowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var surface = (AnnotationSurface)((Grid)window.FindName("SurfaceHost")).Children[0];
        Check(surface.Tool == EditorTool.Arrow && surface.InteractionBounds == new Rect(80, 220, 1150, 510), "La herramienta flotante no usa la región elegida.");
        window.Document.Add(new Annotation(AnnotationTool.Arrow, Colors.Red, 5, new[] { new Point(1030, 650), new Point(720, 370) }));
        RenderContent(window, 1440, 900, "floating.png");
        Check(((Border)window.FindName("ToolsPanel")).Visibility == Visibility.Visible && ((Border)window.FindName("ActionsPanel")).Visibility == Visibility.Visible,
            "Falta alguna barra flotante tras elegir una región.");
        ((Button)window.FindName("SelectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        surface.Editor.Select(rectangle.Id);
        Check(((Button)window.FindName("DeleteButton")).IsEnabled, "No se puede seleccionar una anotación en el modo flotante.");
        var before = Pixels(new ImageExporter().Render(window.Document));
        RenderContent(window, 1440, 900, "floating-selected.png");
        Check(Pixels(new ImageExporter().Render(window.Document)).SequenceEqual(before), "Las barras flotantes, tiradores o máscara aparecen en la exportación.");
        window.SelectRegion(new Int32Rect(900, 600, 500, 250));
        RenderContent(window, 1440, 900, "floating-edge.png");
    }
}
