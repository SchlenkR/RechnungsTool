using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RechnungsTool.ViewModels;

namespace RechnungsTool.Views;

public partial class EinstellungenView : UserControl
{
    public EinstellungenView()
    {
        InitializeComponent();
    }

    async void OrdnerWaehlen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EinstellungenViewModel vm
            || TopLevel.GetTopLevel(this) is not { } top)
            return;

        var auswahl = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Datenordner wählen",
            AllowMultiple = false,
        });

        if (auswahl.Count > 0 && auswahl[0].TryGetLocalPath() is { } pfad)
            vm.DatenOrdner = pfad;
    }
}
