using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DeskShot;
using DeskShot.Application;
using DeskShot.Domain;
using DeskShot.Infrastructure;
using DeskShot.Presentation;

internal static partial class Program
{
    private sealed class FixtureTargets : ICaptureTargetDetector
    {
        public CaptureTarget Window = new(new(30, 30, 600, 450), "Ventana", 1);
        public CaptureTarget Panel = new(new(160, 90, 400, 300), "Panel", 1);
        public TaskCompletionSource<IReadOnlyList<CaptureTarget>>? Pending;
        public bool Fail;
        public CaptureTarget? WindowAt(Point point) => CaptureTargetGeometry.Contains(Window.Region, point) ? Window : null;
        public Task<IReadOnlyList<CaptureTarget>> QueryAsync(Point point, CancellationToken token) => Pending?.Task ?? (Fail ? Task.FromException<IReadOnlyList<CaptureTarget>>(new IOException("Proveedor no disponible")) : Task.FromResult<IReadOnlyList<CaptureTarget>>([Panel, Window]));
    }
    private static void TestTargetGeometry()
    {
        var desktop = new PixelBounds(-1920, -200, 4800, 1800);
        Check(CaptureTargetGeometry.Clip(new(-2000, -250, 1000, 700), desktop) == new Int32Rect(0, 0, 920, 650), "El recorte mezcla píxeles físicos o coordenadas negativas.");
        Check(CaptureTargetGeometry.Clip(new(2900, 10, 100, 100), desktop) is null, "Devuelve un área fuera del escritorio.");
        Check(!CaptureTargetGeometry.Contains(new(10, 10, 100, 100), new(110, 20)), "Un borde derecho se asigna a dos ventanas.");
        var fixture = new FixtureTargets();
        var ordered = CaptureTargetGeometry.Order([fixture.Window, fixture.Panel, fixture.Panel, new(new(0, 0, 20, 20), "Fuera", 2)], new(200, 150));
        Check(ordered.Count == 2 && ordered[0] == fixture.Panel && ordered[1] == fixture.Window, "No ordena los niveles o repite rectángulos.");
        Check(CaptureTargetGeometry.IsClick(new(0, 0), new(5, 0), 1) && !CaptureTargetGeometry.IsClick(new(0, 0), new(20, 0), 1), "Un arrastre manual se interpreta como un clic.");
        Check(CaptureTargetGeometry.IsClick(new(0, 0), new(7, 0), 1.5), "El umbral del clic depende del escalado de Windows.");
    }
    private static void TestTargetPreview(App app)
    {
        var fixture = new FixtureTargets();
        var image = Pattern(800, 600);
        var window = new FloatingCaptureWindow(app, new(image, new(0, 0, 800, 600)), targets: fixture);
        long revision = window.Document.Revision;
        window.TrackHover(new(200, 150)); window.DetectHoverAsync().GetAwaiter().GetResult();
        Check(window.HoverTarget == fixture.Panel && !window.HasSelection && window.Document.Revision == revision, "La vista previa modifica la captura o no detecta el panel.");
        RenderContent(window, 800, 600, "target-selector.png");
        window.CycleTarget(); Check(window.HoverTarget == fixture.Window, "Tab no permite elegir el área superior.");
        window.DetectHoverAsync().GetAwaiter().GetResult(); Check(window.HoverTarget == fixture.Window, "Una consulta repetida pierde el nivel elegido.");
        window.ToggleAutomatic(); Check(window.HoverTarget is null, "El modo manual mantiene una selección automática.");
        window.ToggleWindowOnly(); Check(window.HoverTarget == fixture.Window, "El modo ventana selecciona un control.");
        window.SelectRegion(fixture.Panel.Region);
        Check(window.HoverTarget is null && window.HasSelection, "La detección sigue activa después de confirmar una región.");
        Check(Pixels(new ImageExporter().Render(window.Document.CreateRegionDocument())).SequenceEqual(Pixels(ScrollAreaDetector.Crop(image, fixture.Panel.Region))), "Se exportó el borde de previsualización o se redujo la captura.");
        window.Close();
    }
    private static void TestTargetAsync(App app)
    {
        var fixture = new FixtureTargets { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var window = new FloatingCaptureWindow(app, new(Pattern(800, 600), new(0, 0, 800, 600)), targets: fixture);
        window.TrackHover(new(200, 150)); var job = window.DetectHoverAsync();
        window.TrackHover(new(700, 500)); fixture.Pending.SetResult([fixture.Panel, fixture.Window]); PumpUntil(job);
        Check(window.HoverTarget is null, "Un resultado tardío resalta el área anterior del cursor.");
        fixture.Pending = null; fixture.Fail = true; window.TrackHover(new(200, 150)); window.DetectHoverAsync().GetAwaiter().GetResult();
        Check(window.HoverTarget == fixture.Window, "Un fallo de accesibilidad elimina la selección de ventana.");
        fixture.Fail = false; fixture.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeJob = window.DetectHoverAsync(); window.Close(); fixture.Pending.SetResult([fixture.Panel]); PumpUntil(closeJob);
        Check(window.HoverTarget != fixture.Panel, "Una consulta actualiza un selector cerrado.");
    }
    private static int ShowTargetDocument(App app)
    {
        var grid = new Grid { Background = Brushes.White };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new GroupBox { Header = "Navegación", Margin = new Thickness(12), Foreground = Brushes.Black, Content = new TextBlock { Text = "Proyecto de prueba\n\nCapturas\nDocumentos\nConfiguración", Foreground = Brushes.Black, Margin = new Thickness(12), FontSize = 20 } });
        var text = new TextBox { Text = "Panel de aplicación detectado.\nInformación local con acentos.\nEnglish: local screenshot. Total: 12345.", Margin = new Thickness(22), FontSize = 22, Foreground = Brushes.Black, Background = Brushes.AliceBlue, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, IsReadOnly = true };
        var group = new GroupBox { Header = "Documento · Selecciona este panel", Content = text, Margin = new Thickness(12), Foreground = Brushes.Black };
        Grid.SetColumn(group, 1); grid.Children.Add(group);
        var window = new Window { Title = "DeskShot · Aplicación de prueba del selector", Content = grid, Width = 900, Height = 560, Left = 100, Top = 90, Topmost = true };
        window.Loaded += (_, _) =>
        {
            NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds);
            var top = text.PointToScreen(new Point()); var bottom = text.PointToScreen(new Point(text.ActualWidth, text.ActualHeight));
            File.WriteAllText(Path.Combine(_output, "target-demo-bounds.json"), JsonSerializer.Serialize(new { Frame = new PixelBounds(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), Panel = new PixelBounds((int)top.X, (int)top.Y, (int)(bottom.X - top.X), (int)(bottom.Y - top.Y)) }));
        };
        window.Closed += (_, _) => app.Shutdown(); app.Run(window); return 0;
    }
    private static int ShowTargetSelector(App app)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "target-demo-bounds.json")));
        var bounds = json.RootElement.GetProperty("Frame").Deserialize<PixelBounds>();
        var frame = new DesktopFrame(new GdiCaptureService().CaptureRegion(bounds), bounds);
        var detector = new WindowsCaptureTargetDetector(bounds);
        var window = new FloatingCaptureWindow(app, frame, targets: detector) { ShowInTaskbar = true, Title = "DeskShot · Selector de áreas de prueba" };
        if (window.ShowDialog() == true && window.ResultAction == FloatingCaptureAction.OpenEditor)
        {
            new ImageExporter().SaveTo(new ImageExporter().Render(window.Document.CreateRegionDocument()), Path.Combine(_output, "target-demo-result.png"), ImageFileFormat.Png, 95);
            File.WriteAllText(Path.Combine(_output, "target-demo-result.json"), JsonSerializer.Serialize(window.Document.Region));
        }
        app.Shutdown(); return 0;
    }
    private static void VerifyTargetResult()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "target-demo-bounds.json")));
        var frame = json.RootElement.GetProperty("Frame").Deserialize<PixelBounds>();
        var panel = json.RootElement.GetProperty("Panel").Deserialize<PixelBounds>();
        var region = JsonSerializer.Deserialize<Int32Rect>(File.ReadAllText(Path.Combine(_output, "target-demo-result.json")));
        Check(Math.Abs(region.X - (panel.X - frame.X)) <= 2 && Math.Abs(region.Y - (panel.Y - frame.Y)) <= 2 && Math.Abs(region.Width - panel.Width) <= 2 && Math.Abs(region.Height - panel.Height) <= 2, "El clic no eligió el panel esperado: " + region);
        var image = Load(Path.Combine(_output, "target-demo-result.png"));
        Check(image.PixelWidth == region.Width && image.PixelHeight == region.Height, "Se redujo la resolución del panel.");
        var ocr = new TesseractOcrEngine().RecognizeAsync(image, "spa+eng", CancellationToken.None).GetAwaiter().GetResult();
        Check(ocr.Text.Contains("12345") && ocr.Text.Contains("English"), "El panel perdió texto: " + ocr.Text);
        Console.WriteLine($"  Panel real: {region.Width} × {region.Height} px, elegido con un clic y OCR local correcto.");
    }
}
