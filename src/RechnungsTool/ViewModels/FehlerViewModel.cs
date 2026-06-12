using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using RechnungsTool.Models;

namespace RechnungsTool.ViewModels;

/// <summary>Anzeige für eine Rechnungsdatei, die nicht geparst werden konnte.</summary>
public partial class FehlerViewModel : ViewModelBase
{
    readonly MainWindowViewModel _main;
    readonly RechnungsDatei _datei;

    public string DateiName { get; }
    public string Pfad { get; }
    public string Fehler { get; }
    public string Inhalt { get; }
    public bool ImPapierkorb => _datei.ImPapierkorb;
    public bool NichtImPapierkorb => !_datei.ImPapierkorb;

    public FehlerViewModel(MainWindowViewModel main, RechnungsDatei datei)
    {
        _main = main;
        _datei = datei;
        DateiName = datei.DateiName;
        Pfad = datei.Pfad;
        Fehler = datei.Fehler ?? "Unbekannter Fehler";

        try
        {
            var text = File.ReadAllText(datei.Pfad);
            Inhalt = text.Length > 4000 ? text[..4000] + "\n…" : text;
        }
        catch (Exception ex)
        {
            Inhalt = $"(Datei konnte nicht gelesen werden: {ex.Message})";
        }
    }

    [RelayCommand]
    Task LoeschenAsync() => _main.InPapierkorbVerschiebenAsync(Pfad);

    [RelayCommand]
    Task WiederherstellenAsync() => _main.WiederherstellenAsync(Pfad);

    [RelayCommand]
    Task EndgueltigLoeschenAsync() => _main.EndgueltigLoeschenAsync(Pfad);
}
