using System.Windows.Automation;
using DeskShot.Domain;

namespace DeskShot.Presentation;

public sealed class AnnotationOptions : StackPanel, IDisposable
{
    private readonly ComboBox _effect = new() { Width = 105, Margin = new Thickness(6, 0, 8, 0), ItemsSource = new[] { 6, 12, 20, 32 } };
    private readonly TextBox _number = new() { Width = 65, MaxLength = 4, Margin = new Thickness(6, 0, 8, 0) };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _apply = new() { Content = "Aplicar número", Padding = new Thickness(10, 7, 10, 7) };
    private AnnotationSurface? _surface;
    private bool _sync;
    public AnnotationOptions()
    {
        Orientation = Orientation.Horizontal; Visibility = Visibility.Collapsed;
        Children.Add(_label); Children.Add(_effect); Children.Add(_number); Children.Add(_apply);
        _effect.SetValue(AutomationProperties.NameProperty, "Intensidad de censura en píxeles");
        _number.SetValue(AutomationProperties.NameProperty, "Número del paso");
        _effect.SelectionChanged += (_, _) =>
        {
            if (_sync || _surface is null || _effect.SelectedItem is not int size) return;
            _surface.EffectSize = size;
            if (_surface.Editor.Selected is { Tool: AnnotationTool.Pixelate or AnnotationTool.Blur } selected)
                _surface.Editor.UpdateSelected(selected with { EffectSize = size });
        };
        _apply.Click += (_, _) =>
        {
            if (_surface?.Editor.Selected is not { Tool: AnnotationTool.Step } selected) return;
            if (!int.TryParse(_number.Text, out int number) || number is < 1 or > 9999)
            { _number.ToolTip = "Introduce un número entre 1 y 9999."; _number.Focus(); return; }
            _surface.Editor.UpdateSelected(selected with { StepNumber = number }); _surface.Focus();
        };
    }
    public void Bind(AnnotationSurface surface)
    {
        Dispose(); _surface = surface;
        surface.Editor.SelectionChanged += Refresh; surface.ToolChanged += Refresh; Refresh(this, EventArgs.Empty);
    }
    private void Refresh(object? sender, EventArgs e)
    {
        if (_surface is null) return;
        var selected = _surface.Editor.Selected;
        bool filter = selected?.Tool is AnnotationTool.Pixelate or AnnotationTool.Blur || selected is null && _surface.Tool is EditorTool.Pixelate or EditorTool.Blur;
        bool step = selected?.Tool == AnnotationTool.Step;
        Visibility = filter || step ? Visibility.Visible : Visibility.Collapsed;
        _effect.Visibility = filter ? Visibility.Visible : Visibility.Collapsed;
        _number.Visibility = _apply.Visibility = step ? Visibility.Visible : Visibility.Collapsed;
        _label.Text = filter ? "Intensidad (px)" : "Paso";
        _sync = true;
        try { _effect.SelectedItem = selected?.EffectSize ?? _surface.EffectSize; _number.Text = selected?.StepNumber.ToString() ?? ""; }
        finally { _sync = false; }
    }
    public void Dispose()
    {
        if (_surface is null) return;
        _surface.Editor.SelectionChanged -= Refresh; _surface.ToolChanged -= Refresh; _surface = null;
    }
}
