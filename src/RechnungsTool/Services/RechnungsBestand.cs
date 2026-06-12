using System;
using System.Collections.Generic;
using System.Linq;
using RechnungsTool.Models;

namespace RechnungsTool.Services;

/// <summary>
/// Geladener Datenordner mit Suche und Nummernvergabe – gemeinsame Basis
/// für CLI-Befehle (die GUI hält ihren Zustand im MainWindowViewModel).
/// </summary>
public class RechnungsBestand
{
    public string Ordner { get; }
    public XmlStore.OrdnerInhalt Inhalt { get; }
    public Stammdaten? Stammdaten => Inhalt.Stammdaten;

    RechnungsBestand(string ordner, XmlStore.OrdnerInhalt inhalt)
    {
        Ordner = ordner;
        Inhalt = inhalt;
    }

    public static RechnungsBestand Laden(string? ordnerOverride)
    {
        var ordner = ordnerOverride ?? AppConfig.Laden().DatenOrdnerAbsolut;
        return new RechnungsBestand(ordner, XmlStore.OrdnerLaden(ordner));
    }

    public IEnumerable<RechnungsDatei> Alle => Inhalt.Rechnungen.Concat(Inhalt.Papierkorb);

    /// <summary>Findet eine Rechnung über ihre Nummer (aktiv vor Papierkorb).</summary>
    public RechnungsDatei? Finden(string nummer) =>
        Alle.FirstOrDefault(d => string.Equals(
            d.Rechnung?.Nummer.Trim(), nummer.Trim(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<string> NummernAusser(string? pfad) =>
        Alle.Where(d => d.Rechnung is not null && d.Pfad != pfad)
            .Select(d => d.Rechnung!.Nummer)
            .ToList();

    public string NaechsteNummer() =>
        Rechnungsnummern.Naechste(
            NummernAusser(null),
            Stammdaten?.Nummernkreis.StartNummer ?? "",
            DateTime.Today.Year);

    public List<Befund> Validieren(RechnungsDatei datei) =>
        RechnungsValidator.Pruefen(
            datei.Rechnung!,
            Stammdaten,
            NummernAusser(datei.Pfad),
            erwarteteNummer: datei.Rechnung!.Nummer,
            ursprünglicheNummer: datei.Rechnung!.Nummer);
}
