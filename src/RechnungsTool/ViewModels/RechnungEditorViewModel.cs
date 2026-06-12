using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RechnungsTool.Models;
using RechnungsTool.Services;

namespace RechnungsTool.ViewModels;

/// <summary>Anzeige-Wrapper für Befunde, die nicht in place an einem Feld hängen.</summary>
public class BefundAnzeige
{
    public BefundAnzeige(Befund befund)
    {
        Symbol = befund.IstFehler ? "✖" : "⚠";
        Farbe = befund.IstFehler ? Brushes.IndianRed : Brushes.DarkOrange;
        Text = befund.Text;
    }

    public string Symbol { get; }
    public IBrush Farbe { get; }
    public string Text { get; }
}

public partial class RechnungEditorViewModel : ViewModelBase
{
    static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");

    readonly MainWindowViewModel _main;
    readonly DispatcherTimer _vorschauTimer;
    string? _pfad;
    string? _ursprünglicheNummer;
    string? _gespeicherteXml;
    List<Befund> _befunde = new();
    bool _initialisiert;
    bool _vorschauLaeuft;
    bool _vorschauErneut;

    [ObservableProperty] private string nummer = "";
    [ObservableProperty] private DateTimeOffset? datum = DateTimeOffset.Now;
    [ObservableProperty] private string leistungszeitraum = "";
    [ObservableProperty] private string empfaengerName = "";
    [ObservableProperty] private string empfaengerZusatz = "";
    [ObservableProperty] private string empfaengerStrasse = "";
    [ObservableProperty] private string empfaengerPlz = "";
    [ObservableProperty] private string empfaengerOrt = "";
    [ObservableProperty] private string hinweis = "";

    [ObservableProperty] private bool vorschauSichtbar;
    [ObservableProperty] private Bitmap? vorschauBild;
    [ObservableProperty] private string vorschauStatus = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string summeText = "";
    [ObservableProperty] private string nummernHinweis = "";

    /// <summary>Ungespeicherte Änderungen? Ermittelt über XML-Serialisierungsvergleich.</summary>
    [ObservableProperty] private bool istDirty;

    /// <summary>Anzahl der Validierungsfehler des aktuellen Eingabezustands.</summary>
    [ObservableProperty] private int fehlerAnzahl;

    /// <summary>Nach PDF-Export gesperrt; Freigabe nur über den geführten Dialog.</summary>
    [ObservableProperty] private bool gesperrt;

    // In-place-Validierung: ein Text pro Feld, leer = valide
    [ObservableProperty] private string nummerFehler = "";
    [ObservableProperty] private string nummerWarnung = "";
    [ObservableProperty] private string leistungszeitraumFehler = "";
    [ObservableProperty] private string empfaengerNameFehler = "";
    [ObservableProperty] private string empfaengerStrasseFehler = "";
    [ObservableProperty] private string empfaengerPlzOrtFehler = "";

    public ObservableCollection<PositionViewModel> Positionen { get; } = new();

    /// <summary>Befunde ohne In-place-Feld (Stammdaten, Allgemeines, fehlende Positionen).</summary>
    public ObservableCollection<BefundAnzeige> UebrigeBefunde { get; } = new();

    public bool NurLesen { get; }
    public bool Editierbar => !NurLesen && !Gesperrt;
    public bool LoeschenSichtbar => Editierbar && _pfad is not null;
    public bool VerwerfenSichtbar => Editierbar && _pfad is null;
    public bool AenderungenVerwerfenSichtbar => Editierbar && IstDirty && _pfad is not null;
    public bool FreigebenSichtbar => Gesperrt && !NurLesen;
    public bool AusVorlageSichtbar => !NurLesen;
    public string? Pfad => _pfad;

    partial void OnGesperrtChanged(bool value)
    {
        OnPropertyChanged(nameof(Editierbar));
        OnPropertyChanged(nameof(LoeschenSichtbar));
        OnPropertyChanged(nameof(VerwerfenSichtbar));
        OnPropertyChanged(nameof(AenderungenVerwerfenSichtbar));
        OnPropertyChanged(nameof(FreigebenSichtbar));
        SpeichernCommand.NotifyCanExecuteChanged();
    }

    partial void OnIstDirtyChanged(bool value) =>
        OnPropertyChanged(nameof(AenderungenVerwerfenSichtbar));

    public string Titel => (_pfad, NurLesen) switch
    {
        (null, _) => "Neue Rechnung",
        (_, true) => $"Rechnung {_ursprünglicheNummer} (Papierkorb)",
        _ => $"Rechnung {_ursprünglicheNummer}",
    };

    public RechnungEditorViewModel(MainWindowViewModel main, Rechnung rechnung, string? pfad, bool nurLesen)
    {
        _main = main;
        _pfad = pfad;
        _ursprünglicheNummer = pfad is null ? null : rechnung.Nummer;
        NurLesen = nurLesen;

        nummer = rechnung.Nummer;
        datum = new DateTimeOffset(rechnung.Datum);
        leistungszeitraum = rechnung.Leistungszeitraum;
        empfaengerName = rechnung.Empfaenger.Name;
        empfaengerZusatz = rechnung.Empfaenger.Zusatz;
        empfaengerStrasse = rechnung.Empfaenger.Strasse;
        empfaengerPlz = rechnung.Empfaenger.Plz;
        empfaengerOrt = rechnung.Empfaenger.Ort;
        hinweis = rechnung.Hinweis;
        gesperrt = rechnung.Gesperrt;

        foreach (var p in rechnung.Positionen)
            PositionAnhaengen(new PositionViewModel(p));

        // Referenz für den Dirty-Vergleich: der Zustand beim Öffnen einer
        // gespeicherten Datei; neue Rechnungen sind bis zum ersten Speichern dirty
        _gespeicherteXml = pfad is null || nurLesen ? null : XmlStore.AlsXml(ZuModel());

        _vorschauTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _vorschauTimer.Tick += (_, _) =>
        {
            _vorschauTimer.Stop();
            _ = VorschauRendernAsync();
        };

        _initialisiert = true;
        Aktualisieren();
        _ = VorschauRendernAsync();
    }

    public Rechnung ZuModel() => new()
    {
        Nummer = Nummer.Trim(),
        Datum = (Datum ?? DateTimeOffset.Now).Date,
        Leistungszeitraum = Leistungszeitraum.Trim(),
        Empfaenger = new Empfaenger
        {
            Name = EmpfaengerName.Trim(),
            Zusatz = EmpfaengerZusatz.Trim(),
            Strasse = EmpfaengerStrasse.Trim(),
            Plz = EmpfaengerPlz.Trim(),
            Ort = EmpfaengerOrt.Trim(),
        },
        Positionen = Positionen.Select(p => p.ZuModel()).ToList(),
        Hinweis = Hinweis.Trim(),
        Gesperrt = Gesperrt,
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_initialisiert && e.PropertyName is
            nameof(Nummer) or nameof(Datum) or nameof(Leistungszeitraum) or
            nameof(EmpfaengerName) or nameof(EmpfaengerZusatz) or nameof(EmpfaengerStrasse) or
            nameof(EmpfaengerPlz) or nameof(EmpfaengerOrt) or nameof(Hinweis))
        {
            Aktualisieren();
        }
    }

    void PositionAnhaengen(PositionViewModel position)
    {
        position.Geaendert += Aktualisieren;
        Positionen.Add(position);
    }

    /// <summary>Validierung (Geschäftslogik) sofort, Vorschau entprellt.</summary>
    void Aktualisieren()
    {
        if (!_initialisiert)
            return;

        var modell = ZuModel();
        SummeText = modell.Gesamtbetrag.ToString("N2", DeDe) + " €";
        IstDirty = !NurLesen && XmlStore.AlsXml(modell) != _gespeicherteXml;

        var erwartete = _main.NaechsteNummer(this);
        NummernHinweis = $"Nächste fortlaufende Nummer: {erwartete}";

        _befunde = RechnungsValidator.Pruefen(
            modell, _main.Stammdaten, _main.NummernAusser(this), erwartete, _ursprünglicheNummer);
        FehlerAnzahl = _befunde.Count(b => b.IstFehler);
        BefundeVerteilen();

        SpeichernCommand.NotifyCanExecuteChanged();
        PdfExportierenCommand.NotifyCanExecuteChanged();

        _vorschauTimer.Stop();
        _vorschauTimer.Start();
    }

    /// <summary>Ordnet die Befunde den Feldern (in place) bzw. der Restliste zu.</summary>
    void BefundeVerteilen()
    {
        string Fuer(RechnungsFeld feld, BefundArt art) => string.Join("\n",
            _befunde.Where(b => b.Feld == feld && b.Art == art && b.PositionIndex is null)
                .Select(b => b.Text));

        NummerFehler = Fuer(RechnungsFeld.Nummer, BefundArt.Fehler);
        NummerWarnung = Fuer(RechnungsFeld.Nummer, BefundArt.Warnung);
        LeistungszeitraumFehler = Fuer(RechnungsFeld.Leistungszeitraum, BefundArt.Fehler);
        EmpfaengerNameFehler = Fuer(RechnungsFeld.EmpfaengerName, BefundArt.Fehler);
        EmpfaengerStrasseFehler = Fuer(RechnungsFeld.EmpfaengerStrasse, BefundArt.Fehler);
        EmpfaengerPlzOrtFehler = Fuer(RechnungsFeld.EmpfaengerPlzOrt, BefundArt.Fehler);

        for (var i = 0; i < Positionen.Count; i++)
        {
            var index = i;
            Positionen[i].Fehler = string.Join("\n",
                _befunde.Where(b => b.PositionIndex == index).Select(b => b.Text));
        }

        UebrigeBefunde.Clear();
        foreach (var befund in _befunde.Where(b =>
                     b.Feld is RechnungsFeld.Allgemein or RechnungsFeld.Stammdaten
                     || (b.Feld == RechnungsFeld.Positionen && b.PositionIndex is null)))
        {
            UebrigeBefunde.Add(new BefundAnzeige(befund));
        }
    }

    bool HatFehler => _befunde.Any(b => b.IstFehler);

    [RelayCommand]
    void PositionHinzufuegen()
    {
        PositionAnhaengen(new PositionViewModel());
        Aktualisieren();
    }

    /// <summary>Fügt eine identische Kopie der Position direkt darunter ein.</summary>
    [RelayCommand]
    void PositionKopieren(PositionViewModel position)
    {
        var kopie = new PositionViewModel(position.ZuModel());
        kopie.Geaendert += Aktualisieren;
        Positionen.Insert(Positionen.IndexOf(position) + 1, kopie);
        Aktualisieren();
    }

    [RelayCommand]
    void PositionEntfernen(PositionViewModel position)
    {
        position.Geaendert -= Aktualisieren;
        Positionen.Remove(position);
        Aktualisieren();
    }

    bool KannSpeichern() => Editierbar && IstDirty;

    [RelayCommand(CanExecute = nameof(KannSpeichern))]
    void Speichern()
    {
        Status = SpeichernVersuchen(out var grund)
            ? $"Gespeichert: {Path.GetFileName(_pfad)!}"
            : grund;
    }

    /// <summary>Speichert die Rechnung; false mit Begründung, wenn nicht möglich.</summary>
    public bool SpeichernVersuchen(out string grund)
    {
        grund = "";
        if (NurLesen)
        {
            grund = "Rechnung ist schreibgeschützt (Papierkorb).";
            return false;
        }
        if (Nummer.Trim().Length == 0)
        {
            grund = "Rechnungsnummer fehlt.";
            return false;
        }
        if (_main.NummernAusser(this).Any(n =>
                string.Equals(n.Trim(), Nummer.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            grund = "Rechnungsnummer wird bereits verwendet.";
            return false;
        }

        var modell = ZuModel();
        var bisherigerPfad = _pfad;
        _pfad = XmlStore.RechnungSpeichern(_main.DatenOrdner, modell, _pfad);
        _ursprünglicheNummer = modell.Nummer;
        _gespeicherteXml = XmlStore.AlsXml(modell);
        OnPropertyChanged(nameof(Titel));
        OnPropertyChanged(nameof(LoeschenSichtbar));
        OnPropertyChanged(nameof(VerwerfenSichtbar));
        _main.NachSpeichern(this, bisherigerPfad);
        Aktualisieren();
        return true;
    }

    /// <summary>Validierung erneut anstoßen (z. B. nach Stammdaten-Änderung).</summary>
    public void NeuValidieren() => Aktualisieren();

    bool KannPdfErstellen() => !NurLesen && !HatFehler;

    [RelayCommand(CanExecute = nameof(KannPdfErstellen))]
    async Task PdfExportierenAsync()
    {
        Aktualisieren();
        if (HatFehler)
        {
            Status = "PDF kann nicht erstellt werden – bitte zuerst alle Fehler (✖) beheben.";
            return;
        }

        // Vor dem Export automatisch speichern
        if ((IstDirty || _pfad is null) && !SpeichernVersuchen(out var speicherGrund))
        {
            Status = $"Speichern vor dem Export fehlgeschlagen: {speicherGrund}";
            return;
        }
        if (_pfad is null || _main.Stammdaten is null)
            return;

        try
        {
            var modell = ZuModel();
            var pdfPfad = PdfAblage.StandardPfad(_main.DatenOrdner, modell);

            if (File.Exists(pdfPfad))
            {
                switch (await _main.Dialoge.PdfKonfliktAsync(Path.GetFileName(pdfPfad)))
                {
                    case PdfKonfliktWahl.Abbrechen:
                        Status = "PDF-Erstellung abgebrochen.";
                        return;
                    case PdfKonfliktWahl.Korrektur:
                        pdfPfad = PdfAblage.KorrekturPfad(_main.DatenOrdner, modell);
                        break;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(pdfPfad)!);
            Status = "PDF wird erstellt…";
            var html = RechnungsHtml.Erzeugen(modell, _main.Stammdaten);
            await _main.Pdf.PdfSpeichernAsync(html, pdfPfad);

            // Die Rechnung gilt jetzt als verschickt → sperren und Sperre persistieren
            if (!Gesperrt)
            {
                Gesperrt = true;
                SpeichernVersuchen(out _);
            }

            Status = $"PDF erstellt: {pdfPfad}";
            Process.Start("open", new[] { "-R", pdfPfad });
        }
        catch (Exception ex)
        {
            Status = $"PDF-Erstellung fehlgeschlagen: {ex.Message}";
        }
    }

    /// <summary>Geführte Freigabe einer gesperrten Rechnung: Kopie anlegen oder entsperren.</summary>
    [RelayCommand]
    async Task FreigebenAsync()
    {
        if (!FreigebenSichtbar)
            return;

        switch (await _main.Dialoge.FreigebenAbfragenAsync(Nummer))
        {
            case FreigebenWahl.KopieAnlegen:
                _main.NeueRechnungAusVorlage(ZuModel());
                break;
            case FreigebenWahl.Entsperren:
                Gesperrt = false;
                SpeichernVersuchen(out _);
                Status = "Rechnung wurde zum Editieren freigegeben.";
                break;
        }
    }

    [RelayCommand]
    void AusVorlageErstellen() => _main.NeueRechnungAusVorlage(ZuModel());

    /// <summary>Setzt eine geänderte Rechnung auf den zuletzt gespeicherten Stand zurück.</summary>
    [RelayCommand]
    async Task AenderungenVerwerfenAsync()
    {
        if (!AenderungenVerwerfenSichtbar || _gespeicherteXml is null)
            return;

        var ok = await _main.Dialoge.BestaetigenAsync(
            "Änderungen verwerfen",
            $"Alle ungespeicherten Änderungen an „{Nummer}“ werden verworfen und der " +
            "zuletzt gespeicherte Stand wird wiederhergestellt.",
            "Änderungen verwerfen");
        if (!ok)
            return;

        var gespeichert = XmlStore.RechnungAusXml(_gespeicherteXml);
        _initialisiert = false;
        Nummer = gespeichert.Nummer;
        Datum = new DateTimeOffset(gespeichert.Datum);
        Leistungszeitraum = gespeichert.Leistungszeitraum;
        EmpfaengerName = gespeichert.Empfaenger.Name;
        EmpfaengerZusatz = gespeichert.Empfaenger.Zusatz;
        EmpfaengerStrasse = gespeichert.Empfaenger.Strasse;
        EmpfaengerPlz = gespeichert.Empfaenger.Plz;
        EmpfaengerOrt = gespeichert.Empfaenger.Ort;
        Hinweis = gespeichert.Hinweis;
        Gesperrt = gespeichert.Gesperrt;

        foreach (var position in Positionen)
            position.Geaendert -= Aktualisieren;
        Positionen.Clear();
        foreach (var p in gespeichert.Positionen)
            PositionAnhaengen(new PositionViewModel(p));

        _initialisiert = true;
        Aktualisieren();
        Status = "Änderungen verworfen – zuletzt gespeicherter Stand wiederhergestellt.";
    }

    /// <summary>Verwirft eine neue, noch nicht gespeicherte Rechnung.</summary>
    [RelayCommand]
    async Task VerwerfenAsync()
    {
        if (VerwerfenSichtbar)
            await _main.NeueRechnungVerwerfenAsync(this);
    }

    [RelayCommand]
    async Task LoeschenAsync()
    {
        if (_pfad is not null && !NurLesen)
            await _main.InPapierkorbVerschiebenAsync(_pfad);
    }

    [RelayCommand]
    async Task WiederherstellenAsync()
    {
        if (_pfad is not null && NurLesen)
            await _main.WiederherstellenAsync(_pfad);
    }

    [RelayCommand]
    async Task EndgueltigLoeschenAsync()
    {
        if (_pfad is not null && NurLesen)
            await _main.EndgueltigLoeschenAsync(_pfad);
    }

    async Task VorschauRendernAsync()
    {
        if (_main.Stammdaten is null)
        {
            VorschauStatus = "Keine Vorschau möglich – bitte zuerst die Stammdaten pflegen.";
            return;
        }
        if (_vorschauLaeuft)
        {
            _vorschauErneut = true;
            return;
        }

        _vorschauLaeuft = true;
        try
        {
            VorschauStatus = _main.Pdf.BrowserBereit
                ? "Vorschau wird aktualisiert…"
                : "Chromium wird einmalig heruntergeladen (~170 MB)…";

            var html = RechnungsHtml.Erzeugen(ZuModel(), _main.Stammdaten);
            var png = await _main.Pdf.VorschauPngAsync(html);
            VorschauBild = new Bitmap(new MemoryStream(png));
            VorschauStatus = "";
        }
        catch (Exception ex)
        {
            VorschauStatus = $"Vorschau-Fehler: {ex.Message}";
        }
        finally
        {
            _vorschauLaeuft = false;
            if (_vorschauErneut)
            {
                _vorschauErneut = false;
                _ = VorschauRendernAsync();
            }
        }
    }
}
