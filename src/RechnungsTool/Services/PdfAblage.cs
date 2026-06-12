using System.IO;
using System.Linq;
using RechnungsTool.Models;

namespace RechnungsTool.Services;

/// <summary>Ablage- und Namensregeln für exportierte PDFs (Unterordner „pdf“ im Datenordner).</summary>
public static class PdfAblage
{
    public static string PdfOrdner(string datenOrdner) => Path.Combine(datenOrdner, "pdf");

    public static string StandardPfad(string datenOrdner, Rechnung rechnung) =>
        Path.Combine(PdfOrdner(datenOrdner), Basisname(rechnung) + ".pdf");

    /// <summary>
    /// Nächster freier Korrektur-Dateiname: …_Korrektur.pdf, dann …_Korrektur2.pdf usw.
    /// </summary>
    public static string KorrekturPfad(string datenOrdner, Rechnung rechnung)
    {
        var ordner = PdfOrdner(datenOrdner);
        var basis = Basisname(rechnung);

        var pfad = Path.Combine(ordner, basis + "_Korrektur.pdf");
        for (var n = 2; File.Exists(pfad); n++)
            pfad = Path.Combine(ordner, $"{basis}_Korrektur{n}.pdf");

        return pfad;
    }

    /// <summary>Namensschema: Rechnung_MaxMustermann_2026-01.pdf</summary>
    static string Basisname(Rechnung rechnung)
    {
        var name = new string(rechnung.Empfaenger.Name.Where(char.IsLetterOrDigit).ToArray());
        var nummer = XmlStore.DateinamenSicher(rechnung.Nummer.Trim());
        return name.Length > 0 ? $"Rechnung_{name}_{nummer}" : $"Rechnung_{nummer}";
    }
}
