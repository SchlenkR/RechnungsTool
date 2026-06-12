using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using RechnungsTool.Models;

namespace RechnungsTool.ViewModels;

public record BalkenZeile(string Beschriftung, string BetragText, double Balken);

public record KennzahlenZeile(
    string Jahr,
    string Umsatz,
    string Anzahl,
    string Durchschnitt,
    string GroessteRechnung,
    Avalonia.Media.FontWeight Gewicht);

/// <summary>
/// Kennzahlen über alle aktiven Rechnungen (ohne Papierkorb, ohne defekte Dateien).
/// Wird beim Öffnen des Auswertungs-Dialogs frisch berechnet.
/// </summary>
public class AuswertungViewModel : ViewModelBase
{
    static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");
    const double MaxBalken = 280;

    public List<KennzahlenZeile> KennzahlenProJahr { get; }
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

        // Kennzahlen pro Jahr (Zuordnung wie die Gruppierung in der Übersicht),
        // darunter eine Gesamtzeile
        var proJahr = alle
            .GroupBy(JahrVon)
            .OrderByDescending(g => g.Key)
            .ToList();
        KennzahlenProJahr = proJahr
            .Select(g => Kennzahlen(g.Key.ToString(), g.ToList(), Avalonia.Media.FontWeight.Normal))
            .ToList();
        if (alle.Count > 0)
            KennzahlenProJahr.Add(Kennzahlen("Gesamt", alle, Avalonia.Media.FontWeight.SemiBold));

        UmsatzProJahr = Balken(proJahr
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

    /// <summary>Jahreszuordnung wie in der Übersicht: aus der Nummer, sonst aus dem Datum.</summary>
    static int JahrVon(Rechnung r) =>
        Services.Rechnungsnummern.TryParse(r.Nummer, out var jahr, out _) ? jahr : r.Datum.Year;

    static KennzahlenZeile Kennzahlen(string jahr, List<Rechnung> rechnungen, Avalonia.Media.FontWeight gewicht)
    {
        var summe = rechnungen.Sum(r => r.Gesamtbetrag);
        var groesste = rechnungen.OrderByDescending(r => r.Gesamtbetrag).First();
        return new KennzahlenZeile(
            jahr,
            Euro(summe),
            rechnungen.Count.ToString(DeDe),
            Euro(summe / rechnungen.Count),
            $"{Euro(groesste.Gesamtbetrag)} ({groesste.Nummer})",
            gewicht);
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
