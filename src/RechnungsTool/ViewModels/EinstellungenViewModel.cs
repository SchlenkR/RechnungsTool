using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RechnungsTool.Services;

namespace RechnungsTool.ViewModels;

public partial class EinstellungenViewModel : ViewModelBase
{
    readonly MainWindowViewModel _main;

    [ObservableProperty] private string datenOrdner;
    [ObservableProperty] private string status = "";

    public string ConfigPfad => AppConfig.ConfigPfad;

    public EinstellungenViewModel(MainWindowViewModel main)
    {
        _main = main;
        datenOrdner = main.Config.DatenOrdner;
    }

    [RelayCommand]
    async Task SpeichernAsync()
    {
        var ordner = DatenOrdner.Trim();
        if (ordner.Length == 0)
        {
            Status = "Bitte einen Ordner angeben.";
            return;
        }
        if (ordner == _main.Config.DatenOrdner)
        {
            Status = "Datenordner ist unverändert.";
            return;
        }

        var ok = await _main.Dialoge.BestaetigenAsync(
            "Datenordner wechseln",
            $"Der Datenordner wird auf\n{ordner}\ngeändert und alle Rechnungen werden von dort neu geladen.\n\n" +
            "Ungespeicherte Änderungen an offenen Rechnungen gehen dabei verloren.",
            "Übernehmen und neu laden");
        if (!ok)
        {
            Status = "Wechsel abgebrochen.";
            return;
        }

        _main.DatenOrdnerWechseln(ordner);
        Status = $"Neu geladen – {_main.Eintraege.Count} Rechnung(en) in {_main.DatenOrdner} gefunden.";
    }
}
