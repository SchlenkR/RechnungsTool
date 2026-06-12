using System.Xml.Serialization;

namespace RechnungsTool.Models;

[XmlRoot("Stammdaten")]
public class Stammdaten
{
    public Firma Firma { get; set; } = new();
    public Bank Bank { get; set; } = new();
    public Nummernkreis Nummernkreis { get; set; } = new();
    public int ZahlungszielTage { get; set; } = 14;
}

public class Firma
{
    public string Name { get; set; } = "";
    public string Inhaber { get; set; } = "";
    public string Strasse { get; set; } = "";
    public string Plz { get; set; } = "";
    public string Ort { get; set; } = "";
    public string Telefon { get; set; } = "";
    public string Email { get; set; } = "";
    public string Steuernummer { get; set; } = "";
    public string UstIdNr { get; set; } = "";
}

public class Bank
{
    public string Kontoinhaber { get; set; } = "";
    public string Name { get; set; } = "";
    public string Iban { get; set; } = "";
    public string Bic { get; set; } = "";
}

public class Nummernkreis
{
    /// <summary>
    /// Letzte bereits vergebene Rechnungsnummer (Format JJJJ-NN), bevor das Tool
    /// eingeführt wurde. Die fortlaufende Nummerierung setzt dahinter auf;
    /// leer bedeutet: das Jahr beginnt bei JJJJ-01.
    /// </summary>
    public string StartNummer { get; set; } = "";
}
