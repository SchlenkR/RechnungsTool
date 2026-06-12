using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RechnungsTool.Models;
using RechnungsTool.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RechnungsTool.Cli;

/// <summary>
/// CLI-Modus: wird statt der GUI ausgeführt, sobald Argumente übergeben werden.
/// Gedacht für Automatisierung und KI-Bedienung; alle Befehle haben --help.
/// </summary>
public static class CliApp
{
    public static int Run(string[] args)
    {
        var app = new CommandApp();
        app.Configure(cfg =>
        {
            cfg.SetApplicationName("RechnungsTool");
            cfg.AddCommand<ListBefehl>("list")
                .WithDescription("Listet alle Rechnungen (inkl. Papierkorb)")
                .WithExample("list", "--json");
            cfg.AddCommand<ShowBefehl>("show")
                .WithDescription("Zeigt eine Rechnung")
                .WithExample("show", "2026-19", "--json");
            cfg.AddCommand<NewBefehl>("new")
                .WithDescription("Legt eine neue Rechnung an (Nummer wird fortlaufend vergeben)")
                .WithExample("new", "--empfaenger", "\"Acme GmbH\"", "--strasse", "\"Weg 1\"",
                    "--plz", "50667", "--ort", "Köln", "--leistungszeitraum", "\"Mai 2026\"",
                    "--position", "\"Beratung|8|Std.|95\"");
            cfg.AddCommand<EditBefehl>("edit")
                .WithDescription("Ändert Felder einer Rechnung (--position ersetzt alle Positionen)")
                .WithExample("edit", "2026-19", "--leistungszeitraum", "\"Juni 2026\"");
            cfg.AddCommand<PdfBefehl>("pdf")
                .WithDescription("Erstellt das Rechnungs-PDF (nur bei valider Rechnung) und sperrt die Rechnung")
                .WithExample("pdf", "2026-19");
            cfg.AddCommand<ValidateBefehl>("validate")
                .WithDescription("Validiert eine Rechnung (Exit-Code 1 bei Fehlern)");
            cfg.AddCommand<TrashBefehl>("trash")
                .WithDescription("Verschiebt eine Rechnung in den Papierkorb");
            cfg.AddCommand<RestoreBefehl>("restore")
                .WithDescription("Stellt eine Rechnung aus dem Papierkorb wieder her");
            cfg.AddCommand<UnlockBefehl>("unlock")
                .WithDescription("Gibt eine nach PDF-Export gesperrte Rechnung wieder zum Editieren frei");
            cfg.AddBranch("stammdaten", b =>
            {
                b.SetDescription("Stammdaten anzeigen oder ändern");
                b.AddCommand<StammdatenShowBefehl>("show")
                    .WithDescription("Zeigt die Stammdaten");
                b.AddCommand<StammdatenSetBefehl>("set")
                    .WithDescription("Setzt einzelne Stammdaten-Felder")
                    .WithExample("stammdaten", "set", "--iban", "\"DE12 ...\"");
            });
        });
        return app.Run(args);
    }
}

// --- Gemeinsame Optionen ----------------------------------------------------

public class BasisSettings : CommandSettings
{
    [CommandOption("--daten-ordner <PFAD>")]
    [Description("Datenordner überschreiben (Standard: Wert aus der config.json)")]
    public string? DatenOrdner { get; set; }
}

public class NummerSettings : BasisSettings
{
    [CommandArgument(0, "<NUMMER>")]
    [Description("Rechnungsnummer, z. B. 2026-19")]
    public string Nummer { get; set; } = "";
}

static class CliHilfe
{
    public static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Fehler(string text)
    {
        AnsiConsole.MarkupLine($"[red]Fehler:[/] {Markup.Escape(text)}");
        return 1;
    }

    public static RechnungsDatei? RechnungOderFehler(RechnungsBestand bestand, string nummer)
    {
        var datei = bestand.Finden(nummer);
        if (datei is null)
            AnsiConsole.MarkupLine($"[red]Fehler:[/] Rechnung „{Markup.Escape(nummer)}“ nicht gefunden.");
        else if (datei.Rechnung is null)
            AnsiConsole.MarkupLine($"[red]Fehler:[/] Datei {Markup.Escape(datei.DateiName)} ist nicht lesbar: {Markup.Escape(datei.Fehler ?? "")}");
        return datei?.Rechnung is null ? null : datei;
    }

    public static decimal? ZahlParsen(string eingabe)
    {
        var s = eingabe.Trim();
        if (s.Contains(','))
            s = s.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var wert) ? wert : null;
    }

    /// <summary>Format: "Text|Menge|Einheit|Einzelpreis", z. B. "Beratung|8|Std.|95".</summary>
    public static Position? PositionParsen(string eingabe)
    {
        var teile = eingabe.Split('|');
        if (teile.Length != 4)
            return null;
        var menge = ZahlParsen(teile[1]);
        var preis = ZahlParsen(teile[3]);
        if (menge is null || preis is null)
            return null;
        return new Position { Text = teile[0].Trim(), Menge = menge.Value, Einheit = teile[2].Trim(), Einzelpreis = preis.Value };
    }

    public static DateTime? DatumParsen(string eingabe) =>
        DateTime.TryParseExact(eingabe.Trim(), ["yyyy-MM-dd", "dd.MM.yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum)
            ? datum
            : null;

    public static void BefundeAusgeben(System.Collections.Generic.List<Befund> befunde)
    {
        foreach (var b in befunde)
        {
            var (farbe, symbol) = b.IstFehler ? ("red", "✖") : ("darkorange", "⚠");
            var position = b.PositionIndex is { } i ? $" (Position {i + 1})" : "";
            AnsiConsole.MarkupLine($"[{farbe}]{symbol}[/] {Markup.Escape(b.Text)}{Markup.Escape(position)}");
        }
    }

    public static string Euro(decimal betrag) => betrag.ToString("N2", DeDe) + " €";
}

// --- Befehle ------------------------------------------------------------------

public class ListSettings : BasisSettings
{
    [CommandOption("--json")]
    [Description("Ausgabe als JSON (für Automatisierung)")]
    public bool AlsJson { get; set; }
}

public class ListBefehl : Command<ListSettings>
{
    protected override int Execute(CommandContext context, ListSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);

        if (settings.AlsJson)
        {
            var daten = bestand.Alle.Select(d => new
            {
                Nummer = d.Rechnung?.Nummer,
                Datum = d.Rechnung?.Datum.ToString("yyyy-MM-dd"),
                Empfaenger = d.Rechnung?.Empfaenger.Name,
                Betrag = d.Rechnung?.Gesamtbetrag,
                Gesperrt = d.Rechnung?.Gesperrt ?? false,
                ImPapierkorb = d.ImPapierkorb,
                Fehler = d.Fehler,
                Datei = d.DateiName,
            });
            Console.WriteLine(JsonSerializer.Serialize(daten, CliHilfe.Json));
            return 0;
        }

        var tabelle = new Table().Border(TableBorder.Rounded);
        tabelle.AddColumns("Nummer", "Datum", "Empfänger", "Betrag", "Status");
        foreach (var d in bestand.Alle.OrderByDescending(d => d.Rechnung?.Nummer))
        {
            var status = d switch
            {
                { HatFehler: true } => "[red]nicht lesbar[/]",
                { ImPapierkorb: true } => "[grey]Papierkorb[/]",
                { Rechnung.Gesperrt: true } => "[teal]gesperrt (PDF)[/]",
                _ => "offen",
            };
            tabelle.AddRow(
                Markup.Escape(d.Rechnung?.Nummer ?? d.DateiName),
                d.Rechnung?.Datum.ToString("dd.MM.yyyy") ?? "",
                Markup.Escape(d.Rechnung?.Empfaenger.Name ?? ""),
                d.Rechnung is null ? "" : CliHilfe.Euro(d.Rechnung.Gesamtbetrag),
                status);
        }
        AnsiConsole.Write(tabelle);
        return 0;
    }
}

public class ShowSettings : NummerSettings
{
    [CommandOption("--json")]
    [Description("Ausgabe als JSON (für Automatisierung)")]
    public bool AlsJson { get; set; }
}

public class ShowBefehl : Command<ShowSettings>
{
    protected override int Execute(CommandContext context, ShowSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        if (CliHilfe.RechnungOderFehler(bestand, settings.Nummer) is not { Rechnung: { } r } datei)
            return 1;

        if (settings.AlsJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(r, CliHilfe.Json));
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold]Rechnung {Markup.Escape(r.Nummer)}[/]  ({Markup.Escape(datei.DateiName)})");
        AnsiConsole.MarkupLine($"Datum: {r.Datum:dd.MM.yyyy}   Leistungszeitraum: {Markup.Escape(r.Leistungszeitraum)}");
        var status = datei.ImPapierkorb ? "Papierkorb" : r.Gesperrt ? "gesperrt (PDF erstellt)" : "offen";
        AnsiConsole.MarkupLine($"Status: {status}");
        AnsiConsole.MarkupLine($"Empfänger: {Markup.Escape(string.Join(", ", new[] { r.Empfaenger.Name, r.Empfaenger.Zusatz, r.Empfaenger.Strasse, $"{r.Empfaenger.Plz} {r.Empfaenger.Ort}" }.Where(z => !string.IsNullOrWhiteSpace(z))))}");

        var tabelle = new Table().Border(TableBorder.Rounded);
        tabelle.AddColumns("Pos.", "Leistung", "Menge", "Einheit", "Einzelpreis", "Betrag");
        for (var i = 0; i < r.Positionen.Count; i++)
        {
            var p = r.Positionen[i];
            tabelle.AddRow((i + 1).ToString(), Markup.Escape(p.Text),
                p.Menge.ToString("0.##", CliHilfe.DeDe), Markup.Escape(p.Einheit),
                CliHilfe.Euro(p.Einzelpreis), CliHilfe.Euro(p.Betrag));
        }
        AnsiConsole.Write(tabelle);
        AnsiConsole.MarkupLine($"[bold]Rechnungsbetrag: {CliHilfe.Euro(r.Gesamtbetrag)}[/]");
        if (!string.IsNullOrWhiteSpace(r.Hinweis))
            AnsiConsole.MarkupLine($"Hinweis: {Markup.Escape(r.Hinweis)}");
        return 0;
    }
}

public class RechnungsdatenSettings : BasisSettings
{
    [CommandOption("--datum <DATUM>")]
    [Description("Rechnungsdatum (yyyy-MM-dd oder dd.MM.yyyy)")]
    public string? Datum { get; set; }

    [CommandOption("--leistungszeitraum <TEXT>")]
    [Description("Leistungsdatum bzw. -zeitraum, z. B. \"Mai 2026\"")]
    public string? Leistungszeitraum { get; set; }

    [CommandOption("--empfaenger <NAME>")]
    [Description("Name/Firma des Empfängers")]
    public string? Empfaenger { get; set; }

    [CommandOption("--zusatz <TEXT>")]
    [Description("Empfänger-Zusatz, z. B. Ansprechpartner")]
    public string? Zusatz { get; set; }

    [CommandOption("--strasse <STRASSE>")]
    public string? Strasse { get; set; }

    [CommandOption("--plz <PLZ>")]
    public string? Plz { get; set; }

    [CommandOption("--ort <ORT>")]
    public string? Ort { get; set; }

    [CommandOption("--position <POSITION>")]
    [Description("Leistungsposition als \"Text|Menge|Einheit|Einzelpreis\" (mehrfach möglich)")]
    public string[] Positionen { get; set; } = [];

    [CommandOption("--hinweis <TEXT>")]
    [Description("Optionaler Hinweis auf der Rechnung")]
    public string? Hinweis { get; set; }

    /// <summary>Überträgt nur die angegebenen Optionen auf die Rechnung.</summary>
    public string? Anwenden(Rechnung r)
    {
        if (Datum is not null)
        {
            if (CliHilfe.DatumParsen(Datum) is not { } datum)
                return $"Ungültiges Datum: {Datum}";
            r.Datum = datum;
        }
        if (Leistungszeitraum is not null) r.Leistungszeitraum = Leistungszeitraum.Trim();
        if (Empfaenger is not null) r.Empfaenger.Name = Empfaenger.Trim();
        if (Zusatz is not null) r.Empfaenger.Zusatz = Zusatz.Trim();
        if (Strasse is not null) r.Empfaenger.Strasse = Strasse.Trim();
        if (Plz is not null) r.Empfaenger.Plz = Plz.Trim();
        if (Ort is not null) r.Empfaenger.Ort = Ort.Trim();
        if (Hinweis is not null) r.Hinweis = Hinweis.Trim();

        if (Positionen.Length > 0)
        {
            r.Positionen.Clear();
            foreach (var eingabe in Positionen)
            {
                if (CliHilfe.PositionParsen(eingabe) is not { } position)
                    return $"Ungültige Position „{eingabe}“ – erwartet: \"Text|Menge|Einheit|Einzelpreis\"";
                r.Positionen.Add(position);
            }
        }
        return null;
    }
}

public class NewSettings : RechnungsdatenSettings
{
    [CommandOption("--nummer <NUMMER>")]
    [Description("Rechnungsnummer; ohne Angabe wird die nächste fortlaufende vergeben")]
    public string? Nummer { get; set; }

    [CommandOption("--json")]
    [Description("Gibt die angelegte Rechnung als JSON aus")]
    public bool AlsJson { get; set; }
}

public class NewBefehl : Command<NewSettings>
{
    protected override int Execute(CommandContext context, NewSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);

        var rechnung = new Rechnung
        {
            Nummer = settings.Nummer?.Trim() is { Length: > 0 } n ? n : bestand.NaechsteNummer(),
            Datum = DateTime.Today,
        };
        if (settings.Anwenden(rechnung) is { } problem)
            return CliHilfe.Fehler(problem);

        if (bestand.NummernAusser(null).Any(x => string.Equals(x.Trim(), rechnung.Nummer, StringComparison.OrdinalIgnoreCase)))
            return CliHilfe.Fehler($"Rechnungsnummer „{rechnung.Nummer}“ wird bereits verwendet.");

        var pfad = XmlStore.RechnungSpeichern(bestand.Ordner, rechnung, null);

        if (settings.AlsJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(rechnung, CliHilfe.Json));
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Angelegt:[/] Rechnung {Markup.Escape(rechnung.Nummer)} → {Markup.Escape(Path.GetFileName(pfad))}");
            var fehler = RechnungsValidator.Pruefen(rechnung, bestand.Stammdaten,
                bestand.NummernAusser(pfad), rechnung.Nummer, rechnung.Nummer);
            if (fehler.Count > 0)
                CliHilfe.BefundeAusgeben(fehler);
        }
        return 0;
    }
}

public class EditSettings : RechnungsdatenSettings
{
    [CommandArgument(0, "<NUMMER>")]
    [Description("Rechnungsnummer, z. B. 2026-19")]
    public string Nummer { get; set; } = "";

    [CommandOption("--neue-nummer <NUMMER>")]
    [Description("Rechnungsnummer ändern (Datei wird umbenannt)")]
    public string? NeueNummer { get; set; }
}

public class EditBefehl : Command<EditSettings>
{
    protected override int Execute(CommandContext context, EditSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        if (CliHilfe.RechnungOderFehler(bestand, settings.Nummer) is not { Rechnung: { } r } datei)
            return 1;
        if (datei.ImPapierkorb)
            return CliHilfe.Fehler("Rechnung liegt im Papierkorb – zuerst mit »restore« wiederherstellen.");
        if (r.Gesperrt)
            return CliHilfe.Fehler("Rechnung ist gesperrt (PDF wurde erstellt) – zuerst mit »unlock« freigeben.");

        if (settings.Anwenden(r) is { } problem)
            return CliHilfe.Fehler(problem);
        if (settings.NeueNummer?.Trim() is { Length: > 0 } neueNummer)
        {
            if (bestand.NummernAusser(datei.Pfad).Any(x => string.Equals(x.Trim(), neueNummer, StringComparison.OrdinalIgnoreCase)))
                return CliHilfe.Fehler($"Rechnungsnummer „{neueNummer}“ wird bereits verwendet.");
            r.Nummer = neueNummer;
        }

        var pfad = XmlStore.RechnungSpeichern(bestand.Ordner, r, datei.Pfad);
        AnsiConsole.MarkupLine($"[green]Gespeichert:[/] {Markup.Escape(Path.GetFileName(pfad))}");

        var befunde = RechnungsValidator.Pruefen(r, bestand.Stammdaten,
            bestand.NummernAusser(datei.Pfad), r.Nummer, r.Nummer);
        if (befunde.Count > 0)
            CliHilfe.BefundeAusgeben(befunde);
        return 0;
    }
}

public class PdfSettings : NummerSettings
{
    [CommandOption("--out <PFAD>")]
    [Description("Ziel-PDF-Pfad (Standard: <Datenordner>/pdf/Rechnung_<Empfänger>_<Nummer>.pdf)")]
    public string? Ziel { get; set; }

    [CommandOption("--ueberschreiben")]
    [Description("Vorhandenes PDF überschreiben")]
    public bool Ueberschreiben { get; set; }

    [CommandOption("--korrektur")]
    [Description("Bei vorhandenem PDF einen Korrektur-Export (…_Korrektur.pdf) anlegen")]
    public bool Korrektur { get; set; }
}

public class PdfBefehl : AsyncCommand<PdfSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, PdfSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        if (CliHilfe.RechnungOderFehler(bestand, settings.Nummer) is not { Rechnung: { } r } datei)
            return 1;
        if (datei.ImPapierkorb)
            return CliHilfe.Fehler("Rechnung liegt im Papierkorb – zuerst mit »restore« wiederherstellen.");
        if (bestand.Stammdaten is null)
            return CliHilfe.Fehler("Stammdaten fehlen – zuerst mit »stammdaten set« pflegen.");

        var befunde = bestand.Validieren(datei);
        if (befunde.Any(b => b.IstFehler))
        {
            CliHilfe.BefundeAusgeben(befunde);
            return CliHilfe.Fehler("PDF kann nicht erstellt werden – Rechnung ist nicht valide.");
        }

        var pdfPfad = settings.Ziel ?? PdfAblage.StandardPfad(bestand.Ordner, r);
        if (File.Exists(pdfPfad))
        {
            if (settings.Korrektur)
                pdfPfad = PdfAblage.KorrekturPfad(bestand.Ordner, r);
            else if (!settings.Ueberschreiben)
                return CliHilfe.Fehler($"PDF existiert bereits: {pdfPfad} (--ueberschreiben oder --korrektur verwenden)");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pdfPfad))!);
        var pdf = new PdfDienst();
        try
        {
            AnsiConsole.MarkupLine("PDF wird erstellt…");
            await pdf.PdfSpeichernAsync(RechnungsHtml.Erzeugen(r, bestand.Stammdaten), pdfPfad);
        }
        finally
        {
            await pdf.BeendenAsync();
        }

        if (!r.Gesperrt)
        {
            r.Gesperrt = true;
            XmlStore.RechnungSpeichern(bestand.Ordner, r, datei.Pfad);
        }

        AnsiConsole.MarkupLine($"[green]PDF erstellt:[/] {Markup.Escape(pdfPfad)} (Rechnung ist jetzt gesperrt)");
        return 0;
    }
}

public class ValidateBefehl : Command<NummerSettings>
{
    protected override int Execute(CommandContext context, NummerSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        if (CliHilfe.RechnungOderFehler(bestand, settings.Nummer) is not { } datei)
            return 1;

        var befunde = bestand.Validieren(datei);
        if (befunde.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]✓ Rechnung ist valide.[/]");
            return 0;
        }
        CliHilfe.BefundeAusgeben(befunde);
        return befunde.Any(b => b.IstFehler) ? 1 : 0;
    }
}

public class TrashBefehl : Command<NummerSettings>
{
    protected override int Execute(CommandContext context, NummerSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        if (CliHilfe.RechnungOderFehler(bestand, settings.Nummer) is not { } datei)
            return 1;
        if (datei.ImPapierkorb)
            return CliHilfe.Fehler("Rechnung liegt bereits im Papierkorb.");

        var ziel = XmlStore.InPapierkorb(datei.Pfad);
        AnsiConsole.MarkupLine($"[green]In den Papierkorb verschoben:[/] {Markup.Escape(Path.GetFileName(ziel))}");
        return 0;
    }
}

public class RestoreBefehl : Command<NummerSettings>
{
    protected override int Execute(CommandContext context, NummerSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        if (CliHilfe.RechnungOderFehler(bestand, settings.Nummer) is not { } datei)
            return 1;
        if (!datei.ImPapierkorb)
            return CliHilfe.Fehler("Rechnung liegt nicht im Papierkorb.");

        try
        {
            var ziel = XmlStore.AusPapierkorb(datei.Pfad);
            AnsiConsole.MarkupLine($"[green]Wiederhergestellt:[/] {Markup.Escape(Path.GetFileName(ziel))}");
            return 0;
        }
        catch (IOException ex)
        {
            return CliHilfe.Fehler(ex.Message);
        }
    }
}

public class UnlockBefehl : Command<NummerSettings>
{
    protected override int Execute(CommandContext context, NummerSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        if (CliHilfe.RechnungOderFehler(bestand, settings.Nummer) is not { Rechnung: { } r } datei)
            return 1;
        if (!r.Gesperrt)
            return CliHilfe.Fehler("Rechnung ist nicht gesperrt.");

        r.Gesperrt = false;
        XmlStore.RechnungSpeichern(bestand.Ordner, r, datei.Pfad);
        AnsiConsole.MarkupLine($"[green]Freigegeben:[/] Rechnung {Markup.Escape(r.Nummer)} ist wieder editierbar. " +
                               "Empfehlung: verschickte Rechnungen nicht ändern, sondern kopieren.");
        return 0;
    }
}

public class StammdatenShowSettings : BasisSettings
{
    [CommandOption("--json")]
    [Description("Ausgabe als JSON (für Automatisierung)")]
    public bool AlsJson { get; set; }
}

public class StammdatenShowBefehl : Command<StammdatenShowSettings>
{
    protected override int Execute(CommandContext context, StammdatenShowSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        if (bestand.Stammdaten is not { } s)
            return CliHilfe.Fehler("Keine Stammdaten vorhanden – mit »stammdaten set« anlegen.");

        if (settings.AlsJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(s, CliHilfe.Json));
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(s.Firma.Name)}[/] ({Markup.Escape(s.Firma.Inhaber)})");
        AnsiConsole.MarkupLine(Markup.Escape($"{s.Firma.Strasse}, {s.Firma.Plz} {s.Firma.Ort}"));
        AnsiConsole.MarkupLine(Markup.Escape($"Tel. {s.Firma.Telefon} · {s.Firma.Email}"));
        AnsiConsole.MarkupLine(Markup.Escape($"Steuernummer: {s.Firma.Steuernummer}   USt-IdNr.: {s.Firma.UstIdNr}"));
        AnsiConsole.MarkupLine(Markup.Escape($"Bank: {s.Bank.Name} · IBAN {s.Bank.Iban} · BIC {s.Bank.Bic}"));
        AnsiConsole.MarkupLine(Markup.Escape($"Nummernkreis-Start: {s.Nummernkreis.StartNummer}   Zahlungsziel: {s.ZahlungszielTage} Tage"));
        return 0;
    }
}

public class StammdatenSetSettings : BasisSettings
{
    [CommandOption("--name <NAME>")] public string? Name { get; set; }
    [CommandOption("--inhaber <NAME>")] public string? Inhaber { get; set; }
    [CommandOption("--strasse <STRASSE>")] public string? Strasse { get; set; }
    [CommandOption("--plz <PLZ>")] public string? Plz { get; set; }
    [CommandOption("--ort <ORT>")] public string? Ort { get; set; }
    [CommandOption("--telefon <NR>")] public string? Telefon { get; set; }
    [CommandOption("--email <MAIL>")] public string? Email { get; set; }
    [CommandOption("--steuernummer <NR>")] public string? Steuernummer { get; set; }
    [CommandOption("--ust-idnr <NR>")] public string? UstIdNr { get; set; }
    [CommandOption("--kontoinhaber <NAME>")] public string? Kontoinhaber { get; set; }
    [CommandOption("--bank <NAME>")] public string? Bank { get; set; }
    [CommandOption("--iban <IBAN>")] public string? Iban { get; set; }
    [CommandOption("--bic <BIC>")] public string? Bic { get; set; }

    [CommandOption("--start-nummer <NUMMER>")]
    [Description("Letzte vor Einführung des Tools vergebene Rechnungsnummer (JJJJ-NN)")]
    public string? StartNummer { get; set; }

    [CommandOption("--zahlungsziel <TAGE>")]
    [Description("Zahlungsziel in Tagen")]
    public int? ZahlungszielTage { get; set; }
}

public class StammdatenSetBefehl : Command<StammdatenSetSettings>
{
    protected override int Execute(CommandContext context, StammdatenSetSettings settings, CancellationToken cancellationToken)
    {
        var bestand = RechnungsBestand.Laden(settings.DatenOrdner);
        var s = bestand.Stammdaten ?? new Stammdaten();

        if (settings.Name is not null) s.Firma.Name = settings.Name.Trim();
        if (settings.Inhaber is not null) s.Firma.Inhaber = settings.Inhaber.Trim();
        if (settings.Strasse is not null) s.Firma.Strasse = settings.Strasse.Trim();
        if (settings.Plz is not null) s.Firma.Plz = settings.Plz.Trim();
        if (settings.Ort is not null) s.Firma.Ort = settings.Ort.Trim();
        if (settings.Telefon is not null) s.Firma.Telefon = settings.Telefon.Trim();
        if (settings.Email is not null) s.Firma.Email = settings.Email.Trim();
        if (settings.Steuernummer is not null) s.Firma.Steuernummer = settings.Steuernummer.Trim();
        if (settings.UstIdNr is not null) s.Firma.UstIdNr = settings.UstIdNr.Trim();
        if (settings.Kontoinhaber is not null) s.Bank.Kontoinhaber = settings.Kontoinhaber.Trim();
        if (settings.Bank is not null) s.Bank.Name = settings.Bank.Trim();
        if (settings.Iban is not null) s.Bank.Iban = settings.Iban.Trim();
        if (settings.Bic is not null) s.Bank.Bic = settings.Bic.Trim();
        if (settings.ZahlungszielTage is { } tage)
        {
            if (tage < 0)
                return CliHilfe.Fehler("Zahlungsziel darf nicht negativ sein.");
            s.ZahlungszielTage = tage;
        }
        if (settings.StartNummer is not null)
        {
            if (settings.StartNummer.Trim().Length > 0 && !Rechnungsnummern.TryParse(settings.StartNummer, out _, out _))
                return CliHilfe.Fehler("Start-Nummer muss dem Format JJJJ-NN entsprechen.");
            s.Nummernkreis.StartNummer = settings.StartNummer.Trim();
        }

        XmlStore.StammdatenSpeichern(bestand.Ordner, s);
        AnsiConsole.MarkupLine("[green]Stammdaten gespeichert.[/]");
        return 0;
    }
}
