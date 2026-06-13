using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using RechnungsTool.ViewModels;

namespace RechnungsTool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += BeimSchliessen;
        RechnungsListe.ContainerPrepared += JahresKoepfeDeaktivieren;
        DataContextChanged += (_, _) => SkalierungBeobachten();
        SkalierungBeobachten();

        // Zoom-Shortcuts layout-robust (deutsche Mac-Tastatur: +/− liegen anders als OemPlus/OemMinus).
        // Tunnel, damit auch ein fokussiertes Textfeld die Tasten nicht abfängt.
        AddHandler(KeyDownEvent, ZoomTasten, RoutingStrategies.Tunnel);
    }

    void ZoomTasten(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) || DataContext is not MainWindowViewModel vm)
            return;

        var z = e.KeySymbol;
        if (e.Key is Key.OemPlus or Key.Add || z is "+" or "=")
            vm.VergroessernCommand.Execute(null);
        else if (e.Key is Key.OemMinus or Key.Subtract || z is "-" or "−")
            vm.VerkleinernCommand.Execute(null);
        else if (e.Key is Key.D0 or Key.NumPad0 || z is "0")
            vm.ZoomZuruecksetzenCommand.Execute(null);
        else
            return;

        e.Handled = true;
    }

    /// <summary>Hält die Anzeige-Skalierung an der Einstellung (UiSkalierung) aktuell.</summary>
    void SkalierungBeobachten()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        SkalierungAnwenden(vm.UiSkalierung);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.UiSkalierung))
                SkalierungAnwenden(vm.UiSkalierung);
        };
    }

    void SkalierungAnwenden(double faktor)
    {
        if (Skalierer.LayoutTransform is ScaleTransform st)
        {
            st.ScaleX = faktor;
            st.ScaleY = faktor;
        }
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
