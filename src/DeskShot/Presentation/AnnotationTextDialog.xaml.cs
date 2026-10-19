using DeskShot.Domain;

namespace DeskShot.Presentation;

public partial class AnnotationTextDialog : Window
{
    private readonly Func<string, double, bool, Annotation> _create;
    public Annotation? Result { get; private set; }

    public AnnotationTextDialog(Func<string, double, bool, Annotation> create, Annotation? existing = null)
    {
        _create = create;
        InitializeComponent();
        AnnotationTextBox.Text = existing?.Text ?? "";
        FontSizeBox.Text = (existing?.FontSize ?? 32).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        BoldBox.IsChecked = existing?.IsBold ?? false;
        Loaded += (_, _) => { AnnotationTextBox.Focus(); AnnotationTextBox.CaretIndex = AnnotationTextBox.Text.Length; };
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Apply();
    private void Apply()
    {
        try
        {
            if (!double.TryParse(FontSizeBox.Text, out double size)) throw new ArgumentException("Introduce un tamaño de texto válido en píxeles.");
            Result = _create(AnnotationTextBox.Text, size, BoldBox.IsChecked == true);
            DialogResult = true;
        }
        catch (ArgumentException ex) { ValidationText.Text = ex.Message; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Apply(); }
    }
}
