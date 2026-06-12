using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace RechnungsTool.Models;

[XmlRoot("Rechnung")]
public class Rechnung
{
    public string Nummer { get; set; } = "";

    [XmlElement(DataType = "date")]
    public DateTime Datum { get; set; } = DateTime.Today;

    /// <summary>Leistungsdatum bzw. -zeitraum, Pflichtangabe nach § 14 Abs. 4 UStG.</summary>
    public string Leistungszeitraum { get; set; } = "";

    public Empfaenger Empfaenger { get; set; } = new();

    [XmlArray("Positionen")]
    [XmlArrayItem("Position")]
    public List<Position> Positionen { get; set; } = new();

    /// <summary>Optionaler Freitext, erscheint unter der Positionstabelle.</summary>
    public string Hinweis { get; set; } = "";

    /// <summary>
    /// Wird beim PDF-Export gesetzt: Die Rechnung gilt als verschickt und ist
    /// gegen versehentliches Ändern gesperrt, bis sie explizit freigegeben wird.
    /// </summary>
    public bool Gesperrt { get; set; }

    [XmlIgnore]
    public decimal Gesamtbetrag => Positionen.Sum(p => p.Betrag);
}

public class Empfaenger
{
    public string Name { get; set; } = "";
    public string Zusatz { get; set; } = "";
    public string Strasse { get; set; } = "";
    public string Plz { get; set; } = "";
    public string Ort { get; set; } = "";
}

public class Position
{
    public string Text { get; set; } = "";
    public string Einheit { get; set; } = "Std.";
    public decimal Menge { get; set; } = 1m;
    public decimal Einzelpreis { get; set; }

    [XmlIgnore]
    public decimal Betrag => Math.Round(Menge * Einzelpreis, 2, MidpointRounding.AwayFromZero);
}
