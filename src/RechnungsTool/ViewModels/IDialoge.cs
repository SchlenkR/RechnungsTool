using System.Threading.Tasks;

namespace RechnungsTool.ViewModels;

public enum PdfKonfliktWahl
{
    Abbrechen,
    Ueberschreiben,
    Korrektur,
}

public enum SchliessenWahl
{
    Abbrechen,
    AlleSpeichern,
    NichtSpeichern,
}

public enum FreigebenWahl
{
    Abbrechen,
    Entsperren,
    KopieAnlegen,
}

/// <summary>
/// Modale Dialoge, von den ViewModels angefordert und in der View-Schicht umgesetzt.
/// </summary>
public interface IDialoge
{
    Task<bool> BestaetigenAsync(string titel, string text, string aktion);
    Task InfoAsync(string titel, string text);
    Task<PdfKonfliktWahl> PdfKonfliktAsync(string dateiName);
    Task<SchliessenWahl> SchliessenAbfragenAsync(int anzahlUngespeichert);
    Task<FreigebenWahl> FreigebenAbfragenAsync(string nummer);
    Task EinstellungenAnzeigenAsync(EinstellungenViewModel einstellungen);
    Task StammdatenAnzeigenAsync(StammdatenViewModel stammdaten);
    Task AuswertungAnzeigenAsync(AuswertungViewModel auswertung);
}
