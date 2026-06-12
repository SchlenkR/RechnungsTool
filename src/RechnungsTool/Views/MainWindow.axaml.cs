using Avalonia.Controls;
using RechnungsTool.ViewModels;

namespace RechnungsTool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += BeimSchliessen;
    }

    /// <summary>Schließen abfangen, solange ungespeicherte Änderungen bestehen.</summary>
    async void BeimSchliessen(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.BeendenBestaetigt || !vm.HatUngespeicherte)
            return;

        e.Cancel = true;
        if (await vm.DarfBeendenAsync())
            Close();
    }
}
