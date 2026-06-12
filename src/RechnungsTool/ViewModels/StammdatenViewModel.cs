using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RechnungsTool.Models;
using RechnungsTool.Services;

namespace RechnungsTool.ViewModels;

public partial class StammdatenViewModel : ViewModelBase
{
    readonly MainWindowViewModel _main;
    string _gespeicherteXml;
    bool _initialisiert;

    [ObservableProperty] private string firmaName = "";
    [ObservableProperty] private string inhaber = "";
    [ObservableProperty] private string strasse = "";
    [ObservableProperty] private string plz = "";
    [ObservableProperty] private string ort = "";
    [ObservableProperty] private string telefon = "";
    [ObservableProperty] private string email = "";
    [ObservableProperty] private string steuernummer = "";
    [ObservableProperty] private string ustIdNr = "";

    [ObservableProperty] private string kontoinhaber = "";
    [ObservableProperty] private string bankName = "";
    [ObservableProperty] private string iban = "";
    [ObservableProperty] private string bic = "";

    [ObservableProperty] private string startNummer = "";
    [ObservableProperty] private string zahlungszielTageText = "14";

    [ObservableProperty] private string status = "";

    /// <summary>Ungespeicherte Änderungen? Ermittelt über XML-Serialisierungsvergleich.</summary>
    [ObservableProperty] private bool istDirty;

    public string InfoText =>
        $"Gespeichert wird nach: {_main.DatenOrdner}/{XmlStore.StammdatenDateiName}";

    public StammdatenViewModel(MainWindowViewModel main)
    {
        _main = main;
        var s = main.Stammdaten ?? new Stammdaten();

        firmaName = s.Firma.Name;
        inhaber = s.Firma.Inhaber;
        strasse = s.Firma.Strasse;
        plz = s.Firma.Plz;
        ort = s.Firma.Ort;
        telefon = s.Firma.Telefon;
        email = s.Firma.Email;
        steuernummer = s.Firma.Steuernummer;
        ustIdNr = s.Firma.UstIdNr;

        kontoinhaber = s.Bank.Kontoinhaber;
        bankName = s.Bank.Name;
        iban = s.Bank.Iban;
        bic = s.Bank.Bic;

        startNummer = s.Nummernkreis.StartNummer;
        zahlungszielTageText = s.ZahlungszielTage.ToString();

        // Neu angelegte Stammdaten sind dirty, bis sie erstmals gespeichert wurden
        _gespeicherteXml = main.Stammdaten is null ? "" : XmlStore.AlsXml(ZuModel());
        _initialisiert = true;
        DirtyNeuBerechnen();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_initialisiert && e.PropertyName is not (nameof(Status) or nameof(IstDirty)))
            DirtyNeuBerechnen();
    }

    void DirtyNeuBerechnen() => IstDirty = XmlStore.AlsXml(ZuModel()) != _gespeicherteXml;

    Stammdaten ZuModel() => new()
    {
        Firma = new Firma
        {
            Name = FirmaName.Trim(),
            Inhaber = Inhaber.Trim(),
            Strasse = Strasse.Trim(),
            Plz = Plz.Trim(),
            Ort = Ort.Trim(),
            Telefon = Telefon.Trim(),
            Email = Email.Trim(),
            Steuernummer = Steuernummer.Trim(),
            UstIdNr = UstIdNr.Trim(),
        },
        Bank = new Bank
        {
            Kontoinhaber = Kontoinhaber.Trim(),
            Name = BankName.Trim(),
            Iban = Iban.Trim(),
            Bic = Bic.Trim(),
        },
        Nummernkreis = new Nummernkreis { StartNummer = StartNummer.Trim() },
        ZahlungszielTage = int.TryParse(ZahlungszielTageText.Trim(), out var tage) ? tage : -1,
    };

    [RelayCommand]
    void Speichern()
    {
        Status = SpeichernVersuchen(out var grund)
            ? $"Stammdaten gespeichert ({XmlStore.StammdatenDateiName})."
            : grund;
    }

    /// <summary>Speichert die Stammdaten; false mit Begründung, wenn nicht möglich.</summary>
    public bool SpeichernVersuchen(out string grund)
    {
        grund = "";
        if (!int.TryParse(ZahlungszielTageText.Trim(), out var zahlungsziel) || zahlungsziel < 0)
        {
            grund = "Zahlungsziel muss eine Zahl (Tage) sein.";
            return false;
        }
        if (StartNummer.Trim().Length > 0 && !Rechnungsnummern.TryParse(StartNummer, out _, out _))
        {
            grund = "Start-Rechnungsnummer muss dem Format JJJJ-NN entsprechen (z. B. 2026-18).";
            return false;
        }

        var s = ZuModel();
        XmlStore.StammdatenSpeichern(_main.DatenOrdner, s);
        _gespeicherteXml = XmlStore.AlsXml(s);
        DirtyNeuBerechnen();
        _main.NachStammdatenSpeichern(s);
        return true;
    }
}
