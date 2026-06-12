using Avalonia.Controls;
using RechnungsTool.ViewModels;

namespace RechnungsTool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += BeimSchliessen;
        RechnungsListe.ContainerPrepared += JahresKoepfeDeaktivieren;
    }

    /// <summary>
    /// Jahres-Überschriften sind reine Anzeige: nicht klickbar, nicht fokussierbar.
    /// Container werden recycelt, daher muss beides auch zurückgesetzt werden.
    /// </summary>
    void JahresKoepfeDeaktivieren(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem item)
            return;

        var istKopf = item.DataContext is JahresKopf;
        item.IsHitTestVisible = !istKopf;
        item.Focusable = !istKopf;
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
