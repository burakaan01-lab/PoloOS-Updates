using Avalonia.Controls;

namespace PoloOS.PC.Views;

public partial class MainWindow
{
    private T? FindControl<T>(string name) where T : Control
        => ControlExtensions.FindControl<T>(this, name);
}
