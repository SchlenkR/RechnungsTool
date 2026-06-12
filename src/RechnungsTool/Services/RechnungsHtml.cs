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

    /// <param name="fuerDruck">true: PDF-Export (Ränder/Fußzeile kommen von Chromium); false: Vorschau.</param>
    public static string Erzeugen(Rechnung r, Stammdaten s, bool fuerDruck = false)
    {
        var template = TemplateLaden().Replace("{{MODUS}}", fuerDruck ? "druck" : "vorschau");
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

        var fussKontakt = FussKontakt(f);
        var fussBank = FussBank(s.Bank);

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

    /// <summary>
    /// Fußzeile für den PDF-Export, die Chromium auf jeder Seite wiederholt –
    /// inkl. Rechnungsnummer und „Seite x von y“ (pageNumber/totalPages füllt Chromium).
    /// Nur Inline-Styles, externe Ressourcen stehen hier nicht zur Verfügung.
    /// </summary>
    public static string PdfFusszeile(Rechnung r, Stammdaten s)
    {
        var f = s.Firma;
        return $"""
            <div style="width:100%; box-sizing:border-box; padding:0 20mm 10mm 25mm;
                        font-size:10px; font-family:Helvetica,Arial,sans-serif; color:#666666;">
              <div style="border-top:0.5px solid #bbbbbb; padding-top:5px;
                          display:flex; justify-content:space-between; gap:16px;">
                <div><b>{H(f.Name)}</b><br>{H(f.Strasse)}<br>{H($"{f.Plz} {f.Ort}".Trim())}</div>
                <div>{FussKontakt(f)}</div>
                <div>{FussBank(s.Bank)}</div>
                <div style="text-align:right;">Rechnung {H(r.Nummer)}<br>
                  Seite <span class="pageNumber"></span> von <span class="totalPages"></span></div>
              </div>
            </div>
            """;
    }

    static string FussKontakt(Firma f) => string.Join("<br>",
        new[]
            {
                string.IsNullOrWhiteSpace(f.Telefon) ? null : $"Tel. {f.Telefon}",
                string.IsNullOrWhiteSpace(f.Email) ? null : f.Email,
                !string.IsNullOrWhiteSpace(f.UstIdNr) ? $"USt-IdNr. {f.UstIdNr}" : $"Steuernummer {f.Steuernummer}",
            }
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Select(z => H(z!)));

    static string FussBank(Bank bank) => string.Join("<br>",
        new[]
            {
                string.IsNullOrWhiteSpace(bank.Kontoinhaber) ? null : bank.Kontoinhaber,
                string.IsNullOrWhiteSpace(bank.Iban) ? null : $"IBAN {bank.Iban}",
                string.IsNullOrWhiteSpace(bank.Bic) ? null : $"BIC {bank.Bic}",
                bank.Name,
            }
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Select(z => H(z!)));

    static string TemplateLaden()
    {
        // Eigenes Template im App-Datenordner überschreibt das eingebettete
        var anpassung = Path.Combine(AppConfig.AppDatenOrdner, "RechnungTemplate.html");
        if (File.Exists(anpassung))
            return File.ReadAllText(anpassung, Encoding.UTF8);

        using var stream = typeof(RechnungsHtml).Assembly
            .GetManifestResourceStream("RechnungsTool.Assets.RechnungTemplate.html")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static string Euro(decimal betrag) => H(betrag.ToString("N2", DeDe) + " €");

    static string H(string text) => WebUtility.HtmlEncode(text);
}
