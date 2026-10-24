using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskShot;
using DeskShot.Application;
using DeskShot.Domain;
using DeskShot.Infrastructure;
using DeskShot.Presentation;

internal static partial class Program
{
    private static Annotation Mask(AnnotationTool tool, double x = 40, double y = 50, double width = 100, double height = 90) =>
        new(tool, Colors.Black, 4, [new Point(x, y), new Point(x + width, y + height)]);

    private static void TestSolidRedaction()
    {
        var image = Pattern(320, 240); byte[] original = Pixels(image);
        var doc = new CaptureDocument(image);
        var mask = Mask(AnnotationTool.Redact, 40.2, 50.3, 99.1, 89.1) with { Color = Color.FromArgb(0, 0, 0, 0) };
        doc.Add(mask);
        doc.Add(AnnotationTextLayout.Create("SECRETO", new Point(50, 70), Colors.White, 16, true, new Size(320, 240)));
        var result = new ImageExporter().Render(doc); byte[] pixels = Pixels(result);
        for (int y = 50; y < 140; y++)
        for (int x = 40; x < 140; x++)
        for (int c = 0; c < 3; c++) Check(pixels[(y * 320 + x) * 3 + c] == 0, "La censura contiene transparencia o anotaciones visibles.");
        Check(Pixels(ScrollCrop(result, 160, 60)).SequenceEqual(Pixels(ScrollCrop(image, 160, 60))), "Cambió contenido fuera de la censura.");
        Check(Pixels(doc.Original).SequenceEqual(original), "La censura destruyó el original del editor.");
        using var editor = new AnnotationEditor(doc);
        Check(editor.HitTest(new Point(80, 80), 1)?.Id == mask.Id, "La selección no coincide con la prioridad visual de la censura.");
        string path = Path.Combine(_output, "redaction-solid.png"); new ImageExporter().SaveTo(result, path, ImageFileFormat.Png, 95);
        Check(Pixels(Load(path)).SequenceEqual(pixels), "El PNG perdió la censura.");
        doc.Remove(mask.Id); doc.Undo(); Check(doc.Find(mask.Id) is not null, "Deshacer no recupera la censura.");
    }

    private static void TestRedactionFilters()
    {
        var original = ScrollPattern(160, 160);
        foreach (var tool in new[] { AnnotationTool.Pixelate, AnnotationTool.Blur })
        {
            var doc = new CaptureDocument(original); var mask = Mask(tool, 30, 40, 73, 61) with { EffectSize = 12 }; doc.Add(mask);
            var result = new ImageExporter().Render(doc); byte[] output = Pixels(result), source = Pixels(original);
            for (int y = 0; y < 160; y++)
            for (int x = 0; x < 160; x++)
                if (x < 30 || x >= 103 || y < 40 || y >= 101)
                    for (int c = 0; c < 3; c++) Check(output[(y * 160 + x) * 3 + c] == source[(y * 160 + x) * 3 + c], "El filtro cambió píxeles fuera de su área.");
            Check(!output.SequenceEqual(source), "No se aplicó el filtro.");
            if (tool == AnnotationTool.Pixelate)
            {
                for (int c = 0; c < 3; c++)
                {
                    long sum = 0;
                    for (int y = 40; y < 52; y++) for (int x = 30; x < 42; x++) sum += source[(y * 160 + x) * 3 + c];
                    for (int y = 40; y < 52; y++) for (int x = 30; x < 42; x++)
                        Check(output[(y * 160 + x) * 3 + c] == sum / 144, "El bloque pixelado no representa el promedio de sus píxeles.");
                }
            }
            doc.Replace(mask.Id, mask with { EffectSize = 32 });
            Check(!Pixels(new ImageExporter().Render(doc)).SequenceEqual(output), "Cambiar intensidad reutilizó una caché obsoleta.");
            doc.Undo(); Check(Pixels(new ImageExporter().Render(doc)).SequenceEqual(output), "Deshacer la intensidad no recupera la imagen.");
            // El desenfoque debe conservar un color uniforme, incluso en los bordes.
            var uniform = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgr32, null, Enumerable.Repeat((byte)177, 64 * 64 * 4).ToArray(), 256);
            var uniformDoc = new CaptureDocument(uniform); uniformDoc.Add(Mask(tool, 0, 0, 64, 64));
            Check(Pixels(new ImageExporter().Render(uniformDoc)).All(b => b == 177), "El filtro oscurece los bordes de un color uniforme.");
        }
    }

    private static void TestRedactionCrop()
    {
        foreach (var tool in new[] { AnnotationTool.Redact, AnnotationTool.Pixelate, AnnotationTool.Blur })
        {
            var doc = new CaptureDocument(ScrollPattern(320, 300)); doc.Add(Mask(tool, 10, 20, 180, 190));
            doc.SetRegion(new Int32Rect(73, 69, 180, 160));
            var before = new ImageExporter().Render(doc);
            var crop = doc.CreateRegionDocument();
            Check(Pixels(before).SequenceEqual(Pixels(new ImageExporter().Render(crop))), "Pasar al editor cambia el filtro " + tool);
            crop.SetRegion(new Int32Rect(10, 10, 140, 120));
            Check(Pixels(new ImageExporter().Render(crop)).SequenceEqual(Pixels(new ImageExporter().Render(crop.CreateRegionDocument()))), "Un segundo recorte cambia el filtro.");
            byte[] beforeMove = Pixels(new ImageExporter().Render(crop));
            using var editor = new AnnotationEditor(crop); var selected = crop.Annotations[0]; editor.Select(selected.Id); editor.Nudge(new Vector(4, 4));
            Check(!Pixels(new ImageExporter().Render(crop)).SequenceEqual(beforeMove), "No se puede mover la censura recortada.");
        }
    }

    private static void TestSteps()
    {
        var doc = new CaptureDocument(Pattern(320, 240)); var surface = new AnnotationSurface(doc);
        var first = surface.AddStep(new Point(0, 0))!; var second = surface.AddStep(new Point(160, 120))!; var third = surface.AddStep(new Point(320, 240))!;
        Check(new[] { first, second, third }.Select(a => a.StepNumber).SequenceEqual(new[] { 1, 2, 3 }), "La numeración no avanza automáticamente.");
        Check(AnnotationGeometry.Bounds(first).Left == 0 && AnnotationGeometry.Bounds(third).Right == 320, "Los pasos salen de la captura.");
        doc.Undo(); Check(doc.NextStepNumber == 3, "Deshacer salta un número."); doc.Redo();
        doc.Remove(second.Id); Check(doc.Find(third.Id)?.StepNumber == 3 && doc.NextStepNumber == 4, "Eliminar un paso renumeró los restantes.");
        doc.Replace(first.Id, first with { StepNumber = 7 }); Check(doc.NextStepNumber == 8, "No sigue la edición del número.");
        var resized = AnnotationGeometry.Resize(first, ResizeHandle.BottomRight, new Point(90, 130), new Size(320, 240));
        var bounds = AnnotationGeometry.Bounds(resized); Check(Math.Abs(bounds.Width - bounds.Height) < .01, "Redimensionar deforma el círculo.");
        Check(!AnnotationGeometry.HitTest(first, new Point(0, 0), 0), "La selección del paso usa un rectángulo en vez del círculo.");
        var rendered = new ImageExporter().Render(doc); Check(!Pixels(rendered).SequenceEqual(Pixels(doc.Original)), "No se exportan los pasos.");
        new ImageExporter().SaveTo(rendered, Path.Combine(_output, "steps.png"), ImageFileFormat.Png, 95);
        surface.Editor.Dispose();
    }

    private static void TestRedactedOcr()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 600));
            foreach (var entry in new[] { (80, "Publico 24680"), (300, "Secreto 97531") })
                dc.DrawText(new FormattedText(entry.Item2, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 42, Brushes.Black, 1), new Point(60, entry.Item1));
        }
        var image = new RenderTargetBitmap(1200, 600, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        var doc = new CaptureDocument(image); doc.Add(Mask(AnnotationTool.Redact, 30, 270, 1000, 130));
        doc.Add(AnnotationTextLayout.Create("NO INCLUIR 88888", new Point(50, 470), Colors.Red, 30, true, new Size(1200, 600)));
        var engine = new TesseractOcrEngine();
        var original = engine.RecognizeAsync(doc.Original, "spa", CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("  OCR antes de censurar: " + original.Text.Replace(Environment.NewLine, " | "));
        Check(original.Text.Contains("Secreto"), "El caso de prueba no contiene texto secreto legible.");
        var safe = engine.RecognizeAsync(ImageExporter.RenderForOcr(doc), "spa", CancellationToken.None).GetAwaiter().GetResult();
        Check(safe.Text.Contains("24680") && !safe.Text.Contains("Secreto") && !safe.Text.Contains("97531") && !safe.Text.Contains("88888"), "El OCR devuelve datos censurados o anotaciones.");
    }

    private static void TestPinnedCapture(App app)
    {
        var editable = new WriteableBitmap(Pattern(320, 240));
        var pin = new PinnedCaptureWindow(app, editable); byte[] before = Pixels(pin.Snapshot);
        editable.WritePixels(new Int32Rect(0, 0, 20, 20), new byte[20 * 20 * 4], 80, 0);
        Check(pin.Topmost && pin.Snapshot.IsFrozen && Pixels(pin.Snapshot).SequenceEqual(before), "El anclado no es una copia independiente.");
        RenderContent(pin, 650, 450, "pinned.png"); pin.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        ((ComboBox)pin.FindName("ZoomBox")).SelectedIndex = 2;
        ((Slider)pin.FindName("OpacitySlider")).Value = 60;
        Check(Math.Abs(pin.Opacity - .6) < .01 && ((Image)pin.FindName("PinnedImage")).Width == 320, "No funcionan el zoom y la opacidad.");
        Check(Pixels(pin.Snapshot).SequenceEqual(before), "La vista cambia la imagen que se copia.");
        ((ComboBox)pin.FindName("ZoomBox")).SelectedIndex = 0; ((Slider)pin.FindName("OpacitySlider")).Value = 100;
        RenderContent(pin, 650, 450, "pinned.png");
    }

    private static void TestFeatureControls(App app)
    {
        var doc = FeatureDocument(); var editor = new EditorWindow(app, doc, saved: true);
        var surface = (AnnotationSurface)((Border)editor.FindName("SurfaceHost")).Child;
        var tools = (ComboBox)editor.FindName("ToolBox"); Check(tools.Items.Count == 10, "Faltan herramientas nuevas.");
        tools.SelectedIndex = (int)EditorTool.Pixelate; Check(surface.Tool == EditorTool.Pixelate, "El selector no activa el pixelado.");
        var options = (AnnotationOptions)editor.FindName("ExtraOptions"); Check(options.Visibility == Visibility.Visible, "No se muestra la intensidad.");
        surface.Editor.Select(doc.Annotations.First(a => a.Tool == AnnotationTool.Pixelate).Id);
        var intensity = options.Children.OfType<ComboBox>().Single(); intensity.SelectedItem = 32;
        Check(surface.Editor.Selected?.EffectSize == 32, "No se edita la intensidad de la censura seleccionada.");
        tools.SelectedIndex = (int)EditorTool.Select;
        surface.Editor.Select(doc.Annotations.First(a => a.Tool == AnnotationTool.Step).Id);
        options.Children.OfType<TextBox>().Single().Text = "12";
        options.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(surface.Editor.Selected?.StepNumber == 12, "No se edita el número del paso seleccionado.");
        RenderContent(editor, 1164, 781, "features-editor.png"); RenderContent(editor, 944, 581, "features-editor-small.png");
        var floating = new FloatingCaptureWindow(app, new DesktopFrame(DemoImage(), new PixelBounds(0, 0, 1440, 900)), selectWholeDesktop: true);
        foreach (string name in new[] { "RedactButton", "PixelateButton", "BlurButton", "StepButton" })
            Check(floating.FindName(name) is Button, "Falta una herramienta flotante: " + name);
        RenderContent(floating, 1440, 900, "features-floating.png");
    }

    private static CaptureDocument FeatureDocument()
    {
        var doc = new CaptureDocument(DemoImage());
        doc.Add(Mask(AnnotationTool.Redact, 100, 305, 560, 40));
        doc.Add(Mask(AnnotationTool.Pixelate, 100, 360, 580, 40) with { EffectSize = 12 });
        doc.Add(Mask(AnnotationTool.Blur, 135, 615, 445, 48) with { EffectSize = 12 });
        var surface = new AnnotationSurface(doc); surface.AddStep(new Point(74, 270)); surface.AddStep(new Point(74, 580)); surface.Editor.Dispose();
        return doc;
    }

    private static int ShowFeaturesDemo(App app)
    {
        var doc = FeatureDocument();
        var editor = new EditorWindow(app, doc, saved: true);
        editor.Closed += (_, _) => app.Shutdown();
        app.Run(editor); return 0;
    }
}
