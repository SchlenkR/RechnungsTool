using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RechnungsTool.Services;

/// <summary>Fortlaufende Rechnungsnummern im Format JJJJ-NN (z. B. 2026-18).</summary>
public static partial class Rechnungsnummern
{
    [GeneratedRegex(@"^(\d{4})-(\d{1,6})$")]
    private static partial Regex Muster();

    public static bool TryParse(string? nummer, out int jahr, out int laufnummer)
    {
        jahr = 0;
        laufnummer = 0;
        if (nummer is null)
            return false;

        var match = Muster().Match(nummer.Trim());
        if (!match.Success)
            return false;

        jahr = int.Parse(match.Groups[1].Value);
        laufnummer = int.Parse(match.Groups[2].Value);
        return true;
    }

    /// <summary>
    /// Nächste freie Nummer für das gegebene Jahr, basierend auf allen vorhandenen
    /// Rechnungen und der konfigurierten StartNummer (letzte vor dem Tool vergebene Nummer).
    /// </summary>
    public static string Naechste(IEnumerable<string> vorhandeneNummern, string startNummer, int jahr)
    {
        var max = 0;
        foreach (var nummer in vorhandeneNummern.Append(startNummer))
        {
            if (TryParse(nummer, out var n_jahr, out var lfd) && n_jahr == jahr)
                max = Math.Max(max, lfd);
        }
        return Formatieren(jahr, max + 1);
    }

    public static string Formatieren(int jahr, int laufnummer) => $"{jahr}-{laufnummer:00}";
}
