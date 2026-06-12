using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using RechnungsTool.Models;

namespace RechnungsTool.ViewModels;

public record BalkenZeile(string Beschriftung, string BetragText, double Balken);

/// <summary>
/// Kennzahlen über alle aktiven Rechnungen (ohne Papierkorb, ohne defekte Dateien).
/// Wird beim Öffnen des Auswertungs-Dialogs frisch berechnet.
/// </summary>
public class AuswertungViewModel : ViewModelBase
{
    static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");
    const double MaxBalken = 280;

    public string GesamtumsatzText { get; }
    public string AnzahlText { get; }
    public string DurchschnittText { get; }
    public string GroessteRechnungText { get; }
    public string LaufendesJahrText { get; }

    public List<BalkenZeile> UmsatzProJahr { get; }
    public List<BalkenZeile> TopKunden { get; }

    // Kumulierter Umsatz über die Zeit (Polyline auf 640x160)
    public Points KumulierterVerlauf { get; } = new();
    public string VerlaufVonText { get; } = "";
    public string VerlaufBisText { get; } = "";
    public bool VerlaufVorhanden { get; }

    public AuswertungViewModel(IEnumerable<Rechnung> rechnungen)
    {
        var alle = rechnungen.OrderBy(r => r.Datum).ToList();
        var gesamt = alle.Sum(r => r.Gesamtbetrag);

        GesamtumsatzText = Euro(gesamt);
        AnzahlText = alle.Count.ToString(DeDe);
        DurchschnittText = alle.Count == 0 ? "–" : Euro(gesamt / alle.Count);

        var groesste = alle.OrderByDescending(r => r.Gesamtbetrag).FirstOrDefault();
        GroessteRechnungText = groesste is null ? "–" : $"{Euro(groesste.Gesamtbetrag)} ({groesste.Nummer})";

        var jahr = DateTime.Today.Year;
        LaufendesJahrText = Euro(alle.Where(r => r.Datum.Year == jahr).Sum(r => r.Gesamtbetrag));

        UmsatzProJahr = Balken(alle
            .GroupBy(r => r.Datum.Year)
            .OrderByDescending(g => g.Key)
            .Select(g => (g.Key.ToString(), g.Sum(r => r.Gesamtbetrag))));

        TopKunden = Balken(alle
            .GroupBy(r => r.Empfaenger.Name.Trim())
            .Select(g => (Name: g.Key.Length == 0 ? "(ohne Empfänger)" : g.Key, Summe: g.Sum(r => r.Gesamtbetrag)))
            .OrderByDescending(k => k.Summe)
            .Take(5)
            .Select(k => (k.Name, k.Summe)));

        // Kumulierter Verlauf
        VerlaufVorhanden = alle.Count >= 2 && gesamt > 0;
        if (VerlaufVorhanden)
        {
            var von = alle.First().Datum;
            var bis = alle.Last().Datum;
            var spanne = Math.Max(1, (bis - von).TotalDays);
            VerlaufVonText = von.ToString("dd.MM.yyyy", DeDe);
            VerlaufBisText = bis.ToString("dd.MM.yyyy", DeDe);

            var kumuliert = 0m;
            KumulierterVerlauf.Add(new Point(0, 155));
            foreach (var r in alle)
            {
                kumuliert += r.Gesamtbetrag;
                var x = (r.Datum - von).TotalDays / spanne * 640;
                var y = 155 - (double)(kumuliert / gesamt) * 150;
                KumulierterVerlauf.Add(new Point(x, y));
            }
        }
    }

    static List<BalkenZeile> Balken(IEnumerable<(string Beschriftung, decimal Summe)> zeilen)
    {
        var liste = zeilen.ToList();
        var max = liste.Count == 0 ? 0 : liste.Max(z => z.Summe);
        return liste
            .Select(z => new BalkenZeile(
                z.Beschriftung,
                Euro(z.Summe),
                max <= 0 ? 0 : (double)(z.Summe / max) * MaxBalken))
            .ToList();
    }

    static string Euro(decimal betrag) => betrag.ToString("N2", DeDe) + " €";
}
