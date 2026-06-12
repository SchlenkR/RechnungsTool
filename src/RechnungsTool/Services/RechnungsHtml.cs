using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using RechnungsTool.Models;

namespace RechnungsTool.Services;

/// <summary>Füllt das HTML-Template (Assets/RechnungTemplate.html) mit Rechnungsdaten.</summary>
public static class RechnungsHtml
{
    static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");

    public static string Erzeugen(Rechnung r, Stammdaten s)
    {
        var template = TemplateLaden();
        var f = s.Firma;

        var positionen = new StringBuilder();
        for (var i = 0; i < r.Positionen.Count; i++)
        {
            var p = r.Positionen[i];
            positionen.Append("<tr>")
                .Append($"<td class=\"num\">{i + 1}</td>")
                .Append($"<td>{H(p.Text)}</td>")
                .Append($"<td class=\"num\">{H(p.Menge.ToString("0.##", DeDe))}</td>")
                .Append($"<td>{H(p.Einheit)}</td>")
                .Append($"<td class=\"num\">{Euro(p.Einzelpreis)}</td>")
                .Append($"<td class=\"num\">{Euro(p.Betrag)}</td>")
                .AppendLine("</tr>");
        }

        var empfaenger = string.Join("<br>",
            new[] { r.Empfaenger.Name, r.Empfaenger.Zusatz, r.Empfaenger.Strasse, $"{r.Empfaenger.Plz} {r.Empfaenger.Ort}".Trim() }
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Select(H));

        // Steuernummer ODER USt-IdNr. ist Pflicht (§ 14 Abs. 4 Nr. 2 UStG)
        var steuerZeile = !string.IsNullOrWhiteSpace(f.UstIdNr)
            ? $"<tr><td class=\"label\">USt-IdNr.</td><td class=\"wert\">{H(f.UstIdNr)}</td></tr>"
            : $"<tr><td class=\"label\">Steuernummer</td><td class=\"wert\">{H(f.Steuernummer)}</td></tr>";

        var kontaktZeilen = new[] { f.Telefon, f.Email }
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Select(z => $"<div>{H(z)}</div>");

        var fussKontakt = string.Join("<br>",
            new[]
                {
                    string.IsNullOrWhiteSpace(f.Telefon) ? null : $"Tel. {f.Telefon}",
                    string.IsNullOrWhiteSpace(f.Email) ? null : f.Email,
                    !string.IsNullOrWhiteSpace(f.UstIdNr) ? $"USt-IdNr. {f.UstIdNr}" : $"Steuernummer {f.Steuernummer}",
                }
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Select(z => H(z!)));

        var fussBank = string.Join("<br>",
            new[]
                {
                    string.IsNullOrWhiteSpace(s.Bank.Kontoinhaber) ? null : s.Bank.Kontoinhaber,
                    string.IsNullOrWhiteSpace(s.Bank.Iban) ? null : $"IBAN {s.Bank.Iban}",
                    string.IsNullOrWhiteSpace(s.Bank.Bic) ? null : $"BIC {s.Bank.Bic}",
                    s.Bank.Name,
                }
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Select(z => H(z!)));

        var hinweisBlock = string.IsNullOrWhiteSpace(r.Hinweis)
            ? ""
            : $"<p class=\"freitext\">{H(r.Hinweis)}</p>";

        var zahlungsziel = r.Datum.AddDays(s.ZahlungszielTage);

        return template
            .Replace("{{NUMMER}}", H(r.Nummer))
            .Replace("{{DATUM}}", r.Datum.ToString("dd.MM.yyyy", DeDe))
            .Replace("{{LEISTUNGSZEITRAUM}}", H(r.Leistungszeitraum))
            .Replace("{{STEUER_ZEILE}}", steuerZeile)
            .Replace("{{ABSENDERZEILE}}", H($"{f.Name} · {f.Strasse} · {f.Plz} {f.Ort}"))
            .Replace("{{EMPFAENGER_BLOCK}}", empfaenger)
            .Replace("{{POSITIONEN}}", positionen.ToString())
            .Replace("{{SUMME}}", Euro(r.Gesamtbetrag))
            .Replace("{{ZAHLUNGSZIEL}}", zahlungsziel.ToString("dd.MM.yyyy", DeDe))
            .Replace("{{HINWEIS_BLOCK}}", hinweisBlock)
            .Replace("{{UNTERSCHRIFT}}", H(string.IsNullOrWhiteSpace(f.Inhaber) ? f.Name : f.Inhaber))
            .Replace("{{FIRMA_NAME}}", H(f.Name))
            .Replace("{{FIRMA_INHABER_ZEILE}}", string.IsNullOrWhiteSpace(f.Inhaber) ? "" : $"<div>{H(f.Inhaber)}</div>")
            .Replace("{{FIRMA_STRASSE}}", H(f.Strasse))
            .Replace("{{FIRMA_PLZ}}", H(f.Plz))
            .Replace("{{FIRMA_ORT}}", H(f.Ort))
            .Replace("{{FIRMA_KONTAKT_ZEILEN}}", string.Concat(kontaktZeilen))
            .Replace("{{FUSS_KONTAKT}}", fussKontakt)
            .Replace("{{FUSS_BANK}}", fussBank);
    }

    static string TemplateLaden()
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "Assets", "RechnungTemplate.html");
        return File.ReadAllText(pfad, Encoding.UTF8);
    }

    static string Euro(decimal betrag) => H(betrag.ToString("N2", DeDe) + " €");

    static string H(string text) => WebUtility.HtmlEncode(text);
}
