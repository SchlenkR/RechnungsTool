using System.IO;

namespace RechnungsTool.Models;

/// <summary>
/// Eine XML-Datei im Datenordner: entweder erfolgreich geparste Rechnung
/// oder eine Datei mit Parse-Fehler (taucht trotzdem in der Übersicht auf).
/// </summary>
public class RechnungsDatei
{
    public string Pfad { get; set; } = "";
    public Rechnung? Rechnung { get; set; }
    public string? Fehler { get; set; }
    public bool ImPapierkorb { get; set; }

    public string DateiName => Path.GetFileName(Pfad);
    public bool HatFehler => Fehler is not null;
}
