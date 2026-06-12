using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RechnungsTool.Models;
using RechnungsTool.Services;

namespace RechnungsTool.ViewModels;

/// <summary>
/// Eintrag in der Rechnungs- bzw. Papierkorbübersicht links.
/// Entweder dateibasiert (Datei gesetzt) oder eine neue, noch ungespeicherte
/// Rechnung (nur Editor gesetzt).
/// </summary>
public partial class ListenEintrag : ObservableObject
{
    public RechnungsDatei? Datei { get; init; }

    /// <summary>Offener Editor dieser Rechnung (Quelle des Dirty-Flags), null wenn nicht geöffnet.</summary>
    [ObservableProperty] private RechnungEditorViewModel? editor;

    /// <summary>Zusammenfassung der Validierungsfehler, null wenn valide.</summary>
    public string? Problem { get; init; }

    // Die Anzeige spiegelt live die Eingabemaske (offener Editor),
    // sonst den Dateiinhalt. Die Sortierung bleibt davon unberührt.

    public string Titel
    {
        get
        {
            var nummer = Editor?.Nummer.Trim() ?? Datei?.Rechnung?.Nummer;
            if (nummer is { Length: > 0 })
                return nummer;
            return Datei?.DateiName ?? "Neue Rechnung";
        }
    }

    public string EmpfaengerZeile
    {
        get
        {
            if (Datei is { HatFehler: true })
                return "Datei nicht lesbar";

            var (name, zusatz) = Editor is { } e
                ? (e.EmpfaengerName.Trim(), e.EmpfaengerZusatz.Trim())
                : (Datei?.Rechnung?.Empfaenger.Name ?? "", Datei?.Rechnung?.Empfaenger.Zusatz ?? "");
            if (name.Length == 0)
                return "—";
            return zusatz.Length > 0 ? $"{name} · {zusatz}" : name;
        }
    }

    public string DatumZeile
    {
        get
        {
            if (Datei is { HatFehler: true })
                return "";
            var datum = Editor?.Datum?.Date ?? Datei?.Rechnung?.Datum;
            return datum?.ToString("dd.MM.yyyy") ?? "";
        }
    }

    /// <summary>Bei offenem Editor live aus dessen Validierung, sonst aus dem Lade-Scan.</summary>
    public string? ProblemText
    {
        get
        {
            if (Datei is { HatFehler: true })
                return $"⚠ {Datei.Fehler}";
            if (Editor is { } e)
                return e.FehlerAnzahl > 0 ? ProblemTextFuer(e.FehlerAnzahl) : null;
            return Problem;
        }
    }

    public bool HatProblem => ProblemText is not null;
    public bool KannVorlageSein => Datei?.Rechnung is not null;
    public bool Gesperrt => Editor?.Gesperrt ?? Datei?.Rechnung?.Gesperrt ?? false;

    public static string ProblemTextFuer(int fehlerAnzahl) =>
        $"⚠ {fehlerAnzahl} Pflichtangabe{(fehlerAnzahl == 1 ? "" : "n")} fehlt/ungültig";

    partial void OnEditorChanged(RechnungEditorViewModel? oldValue, RechnungEditorViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= EditorGeaendert;
        if (newValue is not null)
            newValue.PropertyChanged += EditorGeaendert;
        AnzeigeAktualisieren();
    }

    void EditorGeaendert(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RechnungEditorViewModel.Nummer)
            or nameof(RechnungEditorViewModel.EmpfaengerName)
            or nameof(RechnungEditorViewModel.EmpfaengerZusatz)
            or nameof(RechnungEditorViewModel.Datum)
            or nameof(RechnungEditorViewModel.FehlerAnzahl)
            or nameof(RechnungEditorViewModel.Gesperrt))
        {
            AnzeigeAktualisieren();
        }
    }

    void AnzeigeAktualisieren()
    {
        OnPropertyChanged(nameof(Titel));
        OnPropertyChanged(nameof(EmpfaengerZeile));
        OnPropertyChanged(nameof(DatumZeile));
        OnPropertyChanged(nameof(ProblemText));
        OnPropertyChanged(nameof(HatProblem));
        OnPropertyChanged(nameof(Gesperrt));
    }
}

public partial class MainWindowViewModel : ViewModelBase
{
    readonly AppConfig _config;

    // Offene Editoren überleben den Wechsel zwischen Rechnungen (Dirty-Zustand bleibt erhalten)
    readonly Dictionary<string, RechnungEditorViewModel> _offeneEditoren = new();
    readonly List<RechnungEditorViewModel> _neueEditoren = new();

    StammdatenViewModel? _stammdatenSeite;
    EinstellungenViewModel? _einstellungenSeite;

    [ObservableProperty] private ViewModelBase? aktuelleSeite;
    [ObservableProperty] private ListenEintrag? ausgewaehlterEintrag;
    [ObservableProperty] private ListenEintrag? ausgewaehlterPapierkorbEintrag;
    [ObservableProperty] private string? ordnerWarnung;
    [ObservableProperty] private bool papierkorbVorhanden;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AlleSpeichernAusfuehrenCommand))]
    private bool hatUngespeicherte;

    [ObservableProperty] private bool stammdatenDirty;

    // Over-the-air-Update
    readonly UpdateDienst _updates = new();
    UpdateInfo? _update;
    [ObservableProperty] private bool updateVerfuegbar;
    [ObservableProperty] private string updateText = "";

    // Externe Änderungen im Datenordner
    readonly OrdnerWaechter _waechter = new();
    [ObservableProperty] private bool externeAenderung;

    public ObservableCollection<ListenEintrag> Eintraege { get; } = new();
    public ObservableCollection<ListenEintrag> PapierkorbEintraege { get; } = new();

    public Stammdaten? Stammdaten { get; private set; }
    public PdfDienst Pdf { get; } = new();
    public IDialoge Dialoge { get; }
    public string DatenOrdner => _config.DatenOrdnerAbsolut;
    public string StatusZeile => $"Datenordner: {DatenOrdner}";

    /// <summary>Beenden wurde im Dialog bestätigt – Schließen nicht mehr abfangen.</summary>
    public bool BeendenBestaetigt { get; private set; }

    bool _unterdrueckeOeffnen;
    bool _beendenDialogLaeuft;

    public MainWindowViewModel() : this(new KeineDialoge())
    {
    }

    public MainWindowViewModel(IDialoge dialoge)
    {
        Dialoge = dialoge;
        _config = AppConfig.Laden();
        ListeAktualisieren(null);

        // Ohne Stammdaten direkt auf der Stammdatenseite starten,
        // sonst die neueste Rechnung öffnen
        if (Stammdaten is null)
            StammdatenOeffnen();
        else
            AusgewaehlterEintrag = Eintraege.FirstOrDefault(e => e.Datei is { HatFehler: false });

        _ = UpdatePruefenAsync();

        _waechter.ExterneAenderung += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ExterneAenderung = true);
        _waechter.Ueberwachen(DatenOrdner);
    }

    // --- Over-the-air-Update ------------------------------------------------

    async Task UpdatePruefenAsync()
    {
        // Nur sinnvoll, wenn die App als installiertes Bundle läuft (nicht bei dotnet run)
        if (UpdateDienst.BundlePfad() is null)
            return;

        try
        {
            _update = await _updates.PruefenAsync();
            if (_update is not null)
            {
                UpdateText = $"Version {_update.Tag} ist verfügbar (installiert: v{UpdateDienst.AktuelleVersion}).";
                UpdateVerfuegbar = true;
            }
        }
        catch
        {
            // offline oder Rate-Limit – beim nächsten Start erneut versuchen
        }
    }

    [RelayCommand]
    async Task UpdateInstallierenAsync()
    {
        if (_update is null)
            return;
        if (!await DarfBeendenAsync())
            return;

        try
        {
            UpdateText = $"Update {_update.Tag} wird heruntergeladen…";
            var zip = await _updates.HerunterladenAsync(_update);
            _updates.InstallierenNachBeenden(zip);
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.Shutdown();
        }
        catch (Exception ex)
        {
            BeendenBestaetigt = false;
            UpdateText = $"Update fehlgeschlagen: {ex.Message}";
        }
    }

    // --- Übersicht --------------------------------------------------------

    public void ListeAktualisieren(string? auswaehlenPfad)
    {
        var inhalt = XmlStore.OrdnerLaden(DatenOrdner);
        Stammdaten = inhalt.Stammdaten;

        var warnungen = new List<string>();
        if (inhalt.StammdatenFehler is not null)
            warnungen.Add($"stammdaten.xml fehlerhaft: {inhalt.StammdatenFehler}");
        if (inhalt.FremdeDateien.Count > 0)
            warnungen.Add("Nicht erlaubte XML-Dateien im Datenordner: "
                          + string.Join(", ", inhalt.FremdeDateien)
                          + " (erlaubt sind nur stammdaten.xml und rechnung-*.xml)");
        OrdnerWarnung = warnungen.Count > 0 ? string.Join("\n", warnungen) : null;

        // Editoren verschwundener Dateien (gelöscht, verschoben, Ordnerwechsel) verwerfen
        var vorhandenePfade = inhalt.Rechnungen.Select(d => d.Pfad).ToHashSet();
        foreach (var pfad in _offeneEditoren.Keys.Where(p => !vorhandenePfade.Contains(p)).ToList())
            _offeneEditoren.Remove(pfad);

        var alleNummern = AlleNummern(inhalt);

        _unterdrueckeOeffnen = true;
        try
        {
            // Alte Einträge vom Editor-PropertyChanged abmelden
            foreach (var alter in Eintraege)
                alter.Editor = null;

            Eintraege.Clear();
            foreach (var editor in _neueEditoren)
                Eintraege.Add(new ListenEintrag { Editor = editor });
            foreach (var datei in Sortiert(inhalt.Rechnungen))
                Eintraege.Add(EintragErzeugen(datei, alleNummern));

            PapierkorbEintraege.Clear();
            foreach (var datei in Sortiert(inhalt.Papierkorb))
                PapierkorbEintraege.Add(new ListenEintrag { Datei = datei });
            PapierkorbVorhanden = PapierkorbEintraege.Count > 0;

            AuswahlWiederherstellen(auswaehlenPfad);
        }
        finally
        {
            _unterdrueckeOeffnen = false;
        }

        DirtyNeuBerechnen();
    }

    void AuswahlWiederherstellen(string? pfad)
    {
        // Der gerade geöffnete Editor behält Vorrang vor dem zuletzt gespeicherten Pfad,
        // damit z. B. Speichern aus der Liste die Auswahl nicht verschiebt
        var eintrag =
            Eintraege.FirstOrDefault(e => e.Editor is not null && ReferenceEquals(e.Editor, AktuelleSeite))
            ?? (pfad is null ? null : Eintraege.FirstOrDefault(e => e.Datei?.Pfad == pfad));
        AusgewaehlterEintrag = eintrag;
        AusgewaehlterPapierkorbEintrag = eintrag is not null || pfad is null
            ? null
            : PapierkorbEintraege.FirstOrDefault(e => e.Datei?.Pfad == pfad);
    }

    ListenEintrag EintragErzeugen(RechnungsDatei datei, List<(string Pfad, string Nummer)> alleNummern)
    {
        string? problem = null;
        if (datei.Rechnung is { } r)
        {
            var andere = alleNummern.Where(n => n.Pfad != datei.Pfad).Select(n => n.Nummer).ToList();
            var fehler = RechnungsValidator
                .Pruefen(r, Stammdaten, andere, erwarteteNummer: r.Nummer, ursprünglicheNummer: r.Nummer)
                .Count(b => b.IstFehler);
            if (fehler > 0)
                problem = ListenEintrag.ProblemTextFuer(fehler);
        }
        return new ListenEintrag
        {
            Datei = datei,
            Problem = problem,
            Editor = _offeneEditoren.GetValueOrDefault(datei.Pfad),
        };
    }

    static IEnumerable<RechnungsDatei> Sortiert(IEnumerable<RechnungsDatei> dateien) =>
        dateien
            .OrderByDescending(d => d.HatFehler)
            .ThenByDescending(d => Rechnungsnummern.TryParse(d.Rechnung?.Nummer, out var jahr, out var lfd)
                ? (jahr, lfd)
                : (int.MaxValue, int.MaxValue))
            .ThenByDescending(d => d.Rechnung?.Nummer);

    // --- Auswahl / Navigation ----------------------------------------------

    partial void OnAusgewaehlterEintragChanged(ListenEintrag? value)
    {
        if (_unterdrueckeOeffnen || value is null)
            return;

        AuswahlSetzen(value, papierkorb: false);
        EintragOeffnen(value, nurLesen: false);
    }

    partial void OnAusgewaehlterPapierkorbEintragChanged(ListenEintrag? value)
    {
        if (_unterdrueckeOeffnen || value is null)
            return;

        AuswahlSetzen(value, papierkorb: true);
        EintragOeffnen(value, nurLesen: true);
    }

    void AuswahlSetzen(ListenEintrag? eintrag, bool papierkorb)
    {
        _unterdrueckeOeffnen = true;
        AusgewaehlterEintrag = papierkorb ? null : eintrag;
        AusgewaehlterPapierkorbEintrag = papierkorb ? eintrag : null;
        _unterdrueckeOeffnen = false;
    }

    void EintragOeffnen(ListenEintrag eintrag, bool nurLesen)
    {
        if (eintrag.Editor is { } offener)
        {
            AktuelleSeite = offener;
            return;
        }

        if (eintrag.Datei?.Rechnung is { } rechnung)
        {
            var editor = EditorErzeugen(rechnung, eintrag.Datei.Pfad, nurLesen);
            eintrag.Editor = nurLesen ? null : editor;
            AktuelleSeite = editor;
        }
        else if (eintrag.Datei is { } datei)
        {
            AktuelleSeite = new FehlerViewModel(this, datei);
        }
    }

    RechnungEditorViewModel EditorErzeugen(Rechnung rechnung, string? pfad, bool nurLesen)
    {
        var editor = new RechnungEditorViewModel(this, rechnung, pfad, nurLesen);
        if (!nurLesen)
        {
            if (pfad is null)
                _neueEditoren.Add(editor);
            else
                _offeneEditoren[pfad] = editor;
            editor.PropertyChanged += EditorGeaendert;
            DirtyNeuBerechnen();
        }
        return editor;
    }

    void EditorGeaendert(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RechnungEditorViewModel.IstDirty))
            DirtyNeuBerechnen();
    }

    [RelayCommand]
    void NeueRechnung()
    {
        var rechnung = new Rechnung
        {
            Nummer = NaechsteNummer(null),
            Datum = DateTime.Today,
        };
        rechnung.Positionen.Add(new Position());
        NeuenEditorOeffnen(rechnung);
    }

    const string UebernahmeMarker = "";

    [RelayCommand]
    void NeueRechnungFuerEmpfaenger(ListenEintrag? eintrag)
    {
        if (eintrag?.Datei?.Rechnung is { } vorlage)
            NeueRechnungAusVorlage(vorlage);
    }

    /// <summary>
    /// Übernimmt eine vorhandene Rechnung in eine neue, ungespeicherte Rechnung.
    /// Die Positionen werden mitgenommen, jeder Leistungstext bekommt den
    /// ÄNDERN-Marker vorangestellt, damit nichts ungeprüft durchrutscht.
    /// </summary>
    public void NeueRechnungAusVorlage(Rechnung vorlage)
    {
        var rechnung = new Rechnung
        {
            Nummer = NaechsteNummer(null),
            Datum = DateTime.Today,
            Empfaenger = new Empfaenger
            {
                Name = vorlage.Empfaenger.Name,
                Zusatz = vorlage.Empfaenger.Zusatz,
                Strasse = vorlage.Empfaenger.Strasse,
                Plz = vorlage.Empfaenger.Plz,
                Ort = vorlage.Empfaenger.Ort,
            },
            Positionen = vorlage.Positionen.Select(p => new Position
            {
                Text = UebernahmeMarker + p.Text,
                Einheit = p.Einheit,
                Menge = p.Menge,
                Einzelpreis = p.Einzelpreis,
            }).ToList(),
        };
        if (rechnung.Positionen.Count == 0)
            rechnung.Positionen.Add(new Position());
        NeuenEditorOeffnen(rechnung);
    }

    /// <summary>Verwirft eine neue, noch nicht gespeicherte Rechnung nach Rückfrage.</summary>
    public async Task<bool> NeueRechnungVerwerfenAsync(RechnungEditorViewModel editor)
    {
        var ok = await Dialoge.BestaetigenAsync(
            "Rechnung verwerfen",
            $"Die neue Rechnung „{editor.Nummer}“ wurde noch nicht gespeichert und wird verworfen.",
            "Verwerfen");
        if (!ok)
            return false;

        _neueEditoren.Remove(editor);
        AktuelleSeite = null;
        ListeAktualisieren(null);
        return true;
    }

    void NeuenEditorOeffnen(Rechnung rechnung)
    {
        var editor = EditorErzeugen(rechnung, null, nurLesen: false);
        AktuelleSeite = editor;
        ListeAktualisieren(null);
        AuswahlSetzen(Eintraege.FirstOrDefault(e => ReferenceEquals(e.Editor, editor)), papierkorb: false);
    }

    [RelayCommand]
    void StammdatenOeffnen()
    {
        AuswahlSetzen(null, papierkorb: false);
        _stammdatenSeite ??= StammdatenSeiteErzeugen();
        AktuelleSeite = _stammdatenSeite;
    }

    StammdatenViewModel StammdatenSeiteErzeugen()
    {
        var seite = new StammdatenViewModel(this);
        seite.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StammdatenViewModel.IstDirty))
                DirtyNeuBerechnen();
        };
        return seite;
    }

    [RelayCommand]
    Task EinstellungenOeffnenAsync()
    {
        _einstellungenSeite ??= new EinstellungenViewModel(this);
        return Dialoge.EinstellungenAnzeigenAsync(_einstellungenSeite);
    }

    /// <summary>Lädt die komplette Anwendung neu (verwirft offene Editoren nach Rückfrage).</summary>
    [RelayCommand]
    async Task VollNeuLadenAsync()
    {
        if (HatUngespeicherte)
        {
            var ok = await Dialoge.BestaetigenAsync(
                "Anwendung neu laden",
                "Alle Daten werden neu vom Datenträger gelesen.\n\nUngespeicherte Änderungen gehen dabei verloren.",
                "Neu laden");
            if (!ok)
                return;
        }

        _neueEditoren.Clear();
        _offeneEditoren.Clear();
        _stammdatenSeite = null;
        AktuelleSeite = null;
        ListeAktualisieren(null);
        AusgewaehlterEintrag = Eintraege.FirstOrDefault(e => e.Datei is { HatFehler: false });
        ExterneAenderung = false;
    }

    // --- Speichern / Dirty-Tracking ----------------------------------------

    IEnumerable<RechnungEditorViewModel> OffeneEditoren =>
        _offeneEditoren.Values.Concat(_neueEditoren);

    void DirtyNeuBerechnen()
    {
        StammdatenDirty = _stammdatenSeite?.IstDirty == true;
        HatUngespeicherte = OffeneEditoren.Any(e => e.IstDirty) || StammdatenDirty;
    }

    /// <summary>Speichert die Rechnung eines Listeneintrags direkt aus der Übersicht.</summary>
    [RelayCommand]
    async Task EintragSpeichernAsync(ListenEintrag? eintrag)
    {
        if (eintrag?.Editor is not { IstDirty: true } editor)
            return;
        if (!editor.SpeichernVersuchen(out var grund))
            await Dialoge.InfoAsync($"„{eintrag.Titel}“ kann nicht gespeichert werden", grund);
    }

    [RelayCommand(CanExecute = nameof(HatUngespeicherte))]
    Task AlleSpeichernAusfuehrenAsync() => AlleSpeichernAsync();

    /// <summary>Speichert alle dirty Rechnungen und Stammdaten; false, wenn etwas nicht speicherbar war.</summary>
    public async Task<bool> AlleSpeichernAsync()
    {
        var fehlschlaege = new List<string>();

        foreach (var editor in OffeneEditoren.Where(e => e.IstDirty).ToList())
        {
            if (!editor.SpeichernVersuchen(out var grund))
                fehlschlaege.Add($"Rechnung „{editor.Nummer}“: {grund}");
        }

        if (_stammdatenSeite is { IstDirty: true } stammdaten
            && !stammdaten.SpeichernVersuchen(out var stammdatenGrund))
        {
            fehlschlaege.Add($"Stammdaten: {stammdatenGrund}");
        }

        if (fehlschlaege.Count > 0)
        {
            await Dialoge.InfoAsync(
                "Nicht alles konnte gespeichert werden",
                string.Join("\n", fehlschlaege));
            return false;
        }
        return true;
    }

    /// <summary>
    /// Vor dem Beenden: bei ungespeicherten Änderungen nachfragen.
    /// true = Beenden darf fortgesetzt werden.
    /// </summary>
    public async Task<bool> DarfBeendenAsync()
    {
        if (BeendenBestaetigt || !HatUngespeicherte)
            return true;
        if (_beendenDialogLaeuft)
            return false;

        _beendenDialogLaeuft = true;
        try
        {
            var anzahl = OffeneEditoren.Count(e => e.IstDirty) + (StammdatenDirty ? 1 : 0);
            var weiter = await Dialoge.SchliessenAbfragenAsync(anzahl) switch
            {
                SchliessenWahl.AlleSpeichern => await AlleSpeichernAsync(),
                SchliessenWahl.NichtSpeichern => true,
                _ => false,
            };
            BeendenBestaetigt = weiter;
            return weiter;
        }
        finally
        {
            _beendenDialogLaeuft = false;
        }
    }

    /// <summary>Wird vom Editor nach erfolgreichem Speichern aufgerufen (Datei ggf. neu/umbenannt).</summary>
    public void NachSpeichern(RechnungEditorViewModel editor, string? bisherigerPfad)
    {
        _neueEditoren.Remove(editor);
        if (bisherigerPfad is not null)
            _offeneEditoren.Remove(bisherigerPfad);
        if (editor.Pfad is { } pfad)
            _offeneEditoren[pfad] = editor;

        ListeAktualisieren(editor.Pfad);
    }

    public void NachStammdatenSpeichern(Stammdaten stammdaten)
    {
        Stammdaten = stammdaten;
        ListeAktualisieren(null);
        foreach (var editor in OffeneEditoren)
            editor.NeuValidieren();
    }

    // --- Papierkorb-Flows ---------------------------------------------------

    public async Task<bool> InPapierkorbVerschiebenAsync(string pfad)
    {
        var name = Path.GetFileName(pfad);
        var ok = await Dialoge.BestaetigenAsync(
            "In den Papierkorb verschieben",
            $"Die Rechnung „{name}“ wird nicht gelöscht, sondern in den Papierkorb verschoben.\n\n" +
            "Aus dem Papierkorb kann sie wiederhergestellt oder endgültig gelöscht werden.",
            "In den Papierkorb");
        if (!ok)
            return false;

        _offeneEditoren.Remove(pfad);
        XmlStore.InPapierkorb(pfad);
        ListeAktualisieren(null);
        AktuelleSeite = null;
        return true;
    }

    public async Task<bool> WiederherstellenAsync(string pfad)
    {
        try
        {
            var neuerPfad = XmlStore.AusPapierkorb(pfad);
            AktuelleSeite = null;
            ListeAktualisieren(neuerPfad);

            // Wiederhergestellte Rechnung direkt (editierbar) öffnen
            if (Eintraege.FirstOrDefault(e => e.Datei?.Pfad == neuerPfad) is { } eintrag)
            {
                AuswahlSetzen(eintrag, papierkorb: false);
                EintragOeffnen(eintrag, nurLesen: false);
            }
            return true;
        }
        catch (IOException ex)
        {
            await Dialoge.InfoAsync("Wiederherstellen nicht möglich", ex.Message);
            return false;
        }
    }

    public async Task<bool> EndgueltigLoeschenAsync(string pfad)
    {
        var name = Path.GetFileName(pfad);
        var ok = await Dialoge.BestaetigenAsync(
            "Endgültig löschen",
            $"Die Datei „{name}“ wird unwiderruflich aus dem Dateisystem gelöscht.",
            "Endgültig löschen");
        if (!ok)
            return false;

        XmlStore.Loeschen(pfad);
        ListeAktualisieren(null);
        AktuelleSeite = null;
        return true;
    }

    // --- Konfiguration ------------------------------------------------------

    public AppConfig Config => _config;

    /// <summary>
    /// Schreibt den neuen Datenordner in die Config und lädt alles neu;
    /// offene Editoren und die Stammdatenseite werden verworfen (Rückfrage
    /// erfolgt vorher auf der Einstellungsseite).
    /// </summary>
    public void DatenOrdnerWechseln(string ordner)
    {
        _config.DatenOrdner = ordner;
        _config.Speichern();
        _neueEditoren.Clear();
        _offeneEditoren.Clear();
        _stammdatenSeite = null;
        AktuelleSeite = null;
        OnPropertyChanged(nameof(DatenOrdner));
        OnPropertyChanged(nameof(StatusZeile));
        ListeAktualisieren(null);
        _waechter.Ueberwachen(DatenOrdner);
        ExterneAenderung = false;
    }

    // --- Nummernkreis -------------------------------------------------------

    /// <summary>
    /// Nummern aller anderen Rechnungen: Dateien (inkl. Papierkorb) und offene,
    /// noch ungespeicherte Editoren.
    /// </summary>
    public IReadOnlyCollection<string> NummernAusser(RechnungEditorViewModel? ausser)
    {
        var dateiNummern = Eintraege.Concat(PapierkorbEintraege)
            .Where(e => e.Datei?.Rechnung is not null && e.Datei.Pfad != ausser?.Pfad)
            .Select(e => e.Datei!.Rechnung!.Nummer);
        var neueNummern = _neueEditoren
            .Where(e => !ReferenceEquals(e, ausser))
            .Select(e => e.Nummer);
        return dateiNummern.Concat(neueNummern).ToList();
    }

    public string NaechsteNummer(RechnungEditorViewModel? ausser) =>
        Rechnungsnummern.Naechste(
            NummernAusser(ausser),
            Stammdaten?.Nummernkreis.StartNummer ?? "",
            DateTime.Today.Year);

    static List<(string Pfad, string Nummer)> AlleNummern(XmlStore.OrdnerInhalt inhalt) =>
        inhalt.Rechnungen.Concat(inhalt.Papierkorb)
            .Where(d => d.Rechnung is not null)
            .Select(d => (d.Pfad, d.Rechnung!.Nummer))
            .ToList();

    /// <summary>No-op-Dialoge für den XAML-Designer.</summary>
    class KeineDialoge : IDialoge
    {
        public Task<bool> BestaetigenAsync(string titel, string text, string aktion) => Task.FromResult(false);
        public Task InfoAsync(string titel, string text) => Task.CompletedTask;
        public Task<PdfKonfliktWahl> PdfKonfliktAsync(string dateiName) => Task.FromResult(PdfKonfliktWahl.Abbrechen);
        public Task<SchliessenWahl> SchliessenAbfragenAsync(int anzahl) => Task.FromResult(SchliessenWahl.Abbrechen);
        public Task<FreigebenWahl> FreigebenAbfragenAsync(string nummer) => Task.FromResult(FreigebenWahl.Abbrechen);
        public Task EinstellungenAnzeigenAsync(EinstellungenViewModel einstellungen) => Task.CompletedTask;
    }
}
