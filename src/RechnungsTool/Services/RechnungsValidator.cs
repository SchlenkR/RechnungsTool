using System.Collections.Generic;
using System.Linq;
using RechnungsTool.Models;

namespace RechnungsTool.Services;

public enum BefundArt
{
    Fehler,   // blockiert PDF-Erstellung (Gesetzeskonformität nach § 14 UStG)
    Warnung,
}

/// <summary>Feldbezug eines Befunds, damit die UI ihn in place anzeigen kann.</summary>
public enum RechnungsFeld
{
    Allgemein,
    Nummer,
    Leistungszeitraum,
    EmpfaengerName,
    EmpfaengerStrasse,
    EmpfaengerPlzOrt,
    Positionen,
    Stammdaten,
}

public record Befund(BefundArt Art, RechnungsFeld Feld, string Text, int? PositionIndex = null)
{
    public bool IstFehler => Art == BefundArt.Fehler;
}

public static class RechnungsValidator
{
    /// <param name="andereNummern">Nummern aller anderen Rechnungen inkl. Papierkorb (Eindeutigkeit).</param>
    /// <param name="erwarteteNummer">Nächste fortlaufende Nummer laut Nummernkreis.</param>
    /// <param name="ursprünglicheNummer">Nummer beim Laden der Datei; null bei neuer Rechnung.</param>
    public static List<Befund> Pruefen(
        Rechnung r,
        Stammdaten? stammdaten,
        IReadOnlyCollection<string> andereNummern,
        string erwarteteNummer,
        string? ursprünglicheNummer)
    {
        var befunde = new List<Befund>();
        void Fehler(RechnungsFeld feld, string text, int? position = null) =>
            befunde.Add(new Befund(BefundArt.Fehler, feld, text, position));
        void Warnung(RechnungsFeld feld, string text) =>
            befunde.Add(new Befund(BefundArt.Warnung, feld, text));

        // Rechnungsnummer (§ 14 Abs. 4 Nr. 4 UStG: fortlaufend und einmalig)
        var nummer = r.Nummer.Trim();
        if (nummer.Length == 0)
        {
            Fehler(RechnungsFeld.Nummer, "Rechnungsnummer fehlt (Pflichtangabe, § 14 UStG).");
        }
        else
        {
            if (andereNummern.Any(n => string.Equals(n.Trim(), nummer, System.StringComparison.OrdinalIgnoreCase)))
                Fehler(RechnungsFeld.Nummer, $"Nummer „{nummer}“ wird bereits verwendet – Rechnungsnummern müssen eindeutig sein.");

            if (!Rechnungsnummern.TryParse(nummer, out _, out _))
                Warnung(RechnungsFeld.Nummer, $"Nummer „{nummer}“ entspricht nicht dem Format JJJJ-NN.");
            else if (nummer != ursprünglicheNummer && nummer != erwarteteNummer)
                Warnung(RechnungsFeld.Nummer, $"Nummer ist nicht fortlaufend – erwartet wäre „{erwarteteNummer}“.");
        }

        // Empfänger: nur der Name ist Pflicht; Straße/PLZ/Ort sind optional.
        if (r.Empfaenger.Name.Trim().Length == 0)
            Fehler(RechnungsFeld.EmpfaengerName, "Name fehlt (Pflichtangabe, § 14 UStG).");

        // Leistungszeitpunkt (§ 14 Abs. 4 Nr. 6 UStG)
        if (r.Leistungszeitraum.Trim().Length == 0)
            Fehler(RechnungsFeld.Leistungszeitraum, "Leistungsdatum bzw. -zeitraum fehlt (Pflichtangabe, § 14 UStG).");

        // Positionen (§ 14 Abs. 4 Nr. 5 UStG: Menge und Art der Leistung)
        if (r.Positionen.Count == 0)
        {
            Fehler(RechnungsFeld.Positionen, "Mindestens eine Leistungsposition ist erforderlich.");
        }
        else
        {
            for (var i = 0; i < r.Positionen.Count; i++)
            {
                var p = r.Positionen[i];
                if (p.Text.Trim().Length == 0)
                    Fehler(RechnungsFeld.Positionen, "Leistungstext fehlt.", i);
                if (p.Menge <= 0)
                    Fehler(RechnungsFeld.Positionen, "Menge muss größer als 0 sein.", i);
                if (p.Einzelpreis < 0)
                    Fehler(RechnungsFeld.Positionen, "Einzelpreis darf nicht negativ sein.", i);
            }
        }

        if (r.Gesamtbetrag == 0)
            Warnung(RechnungsFeld.Allgemein, "Rechnungsbetrag ist 0,00 €.");

        // Stammdaten (Angaben des leistenden Unternehmers, § 14 Abs. 4 Nr. 1–2 UStG)
        if (stammdaten is null)
        {
            Fehler(RechnungsFeld.Stammdaten, "Stammdaten fehlen – bitte zuerst die Stammdaten pflegen und speichern.");
        }
        else
        {
            var f = stammdaten.Firma;
            if (f.Name.Trim().Length == 0)
                Fehler(RechnungsFeld.Stammdaten, "Stammdaten: Name fehlt (Pflichtangabe, § 14 UStG).");
            if (f.Strasse.Trim().Length == 0 || f.Plz.Trim().Length == 0 || f.Ort.Trim().Length == 0)
                Fehler(RechnungsFeld.Stammdaten, "Stammdaten: vollständige Anschrift fehlt (Pflichtangabe, § 14 UStG).");
            if (f.Steuernummer.Trim().Length == 0 && f.UstIdNr.Trim().Length == 0)
                Fehler(RechnungsFeld.Stammdaten, "Stammdaten: Steuernummer oder USt-IdNr. fehlt (Pflichtangabe, § 14 UStG).");
        }

        return befunde;
    }
}
