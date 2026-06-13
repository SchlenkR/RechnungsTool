using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace RechnungsTool.Services;

/// <summary>
/// Treibt Claude Code im Headless-Streaming-Modus (kein Terminal, kein PTY): ein Prozess
/// mit umgeleiteten Pipes. Eingaben gehen als stream-json an stdin, Antworten kommen als
/// NDJSON über stdout zurück. Mehrere Turns behalten den Kontext, solange der Prozess lebt.
///
/// Start über die Login-Shell (<c>$SHELL -l -c 'exec claude …'</c>), damit PATH/Umgebung
/// stimmen – eine .app erbt sonst nicht das ~/.local/bin des Users. <c>exec</c> macht den
/// Prozess zu Claude selbst (sauber beendbar) und umgeht den interaktiven Alias.
/// </summary>
public sealed class ClaudeChat : IDisposable
{
    // UTF-8 OHNE BOM: ein BOM vor der ersten stdin-Zeile bricht claudes NDJSON-Parser.
    static readonly Encoding Utf8OhneBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // Umlaute literal (UTF-8) statt \uXXXX schreiben.
    static readonly JsonSerializerOptions JsonOptionen = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    Process? _proc;

    /// <summary>Ein Text-Abschnitt einer Claude-Antwort (Hintergrund-Thread).</summary>
    public event Action<string>? AssistentText;

    /// <summary>Freundlicher Hinweis auf eine Werkzeug-Aktion, z. B. „Datei geschrieben“.</summary>
    public event Action<string>? Werkzeug;

    /// <summary>Der aktuelle Turn ist fertig (Eingabe wieder freigeben).</summary>
    public event Action<bool>? TurnFertig; // bool: war Fehler?

    /// <summary>System-/Statusmeldung (Start, Fehler, Prozessende).</summary>
    public event Action<string>? Systemmeldung;

    /// <summary>Der Claude-Prozess ist beendet.</summary>
    public event Action? Beendet;

    public bool Laeuft => _proc is { HasExited: false };

    public void Starten(string datenOrdner)
    {
        if (Laeuft)
            return;

        Directory.CreateDirectory(AppConfig.AppDatenOrdner);
        var promptDatei = Path.Combine(AppConfig.AppDatenOrdner, "claude-systemprompt.txt");
        File.WriteAllText(promptDatei, Kontextprompt(datenOrdner));

        // Fester Befehl; der einzige variable Teil ist der (single-gequotete) Dateipfad.
        // --include-partial-messages: Antworttext kommt Token für Token (Live-Feedback).
        var befehl =
            "exec claude -p --input-format stream-json --output-format stream-json " +
            "--include-partial-messages --verbose " +
            "--dangerously-skip-permissions " +
            "--append-system-prompt \"$(cat " + EinfachGequotet(promptDatei) + ")\"";

        var psi = new ProcessStartInfo
        {
            FileName = Shell(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8OhneBom,
            StandardInputEncoding = Utf8OhneBom,
            WorkingDirectory = Directory.Exists(datenOrdner) ? datenOrdner : Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(befehl);

        try
        {
            _proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Systemmeldung?.Invoke($"Claude konnte nicht gestartet werden: {ex.Message}");
            return;
        }
        if (_proc is null)
        {
            Systemmeldung?.Invoke("Claude konnte nicht gestartet werden.");
            return;
        }

        _proc.EnableRaisingEvents = true;
        _proc.Exited += (_, _) => Beendet?.Invoke();

        _ = LeseAusgabeAsync(_proc.StandardOutput);
        _ = LeseFehlerAsync(_proc.StandardError);
    }

    /// <summary>Sendet eine Nutzernachricht (ein Chat-Turn).</summary>
    public async Task SendenAsync(string text)
    {
        if (_proc is null || _proc.HasExited)
            return;

        var zeile = JsonSerializer.Serialize(new
        {
            type = "user",
            message = new
            {
                role = "user",
                content = new[] { new { type = "text", text } },
            },
        }, JsonOptionen);

        try
        {
            await _proc.StandardInput.WriteLineAsync(zeile);
            await _proc.StandardInput.FlushAsync();
        }
        catch (Exception ex)
        {
            Systemmeldung?.Invoke($"Senden fehlgeschlagen: {ex.Message}");
        }
    }

    async Task LeseAusgabeAsync(StreamReader reader)
    {
        try
        {
            string? zeile;
            while ((zeile = await reader.ReadLineAsync()) is not null)
                Verarbeiten(zeile);
        }
        catch
        {
            // Stream geschlossen – Prozessende wird über Exited gemeldet.
        }
    }

    async Task LeseFehlerAsync(StreamReader reader)
    {
        try
        {
            string? zeile;
            while ((zeile = await reader.ReadLineAsync()) is not null)
                if (zeile.Trim().Length > 0)
                    Systemmeldung?.Invoke(zeile.Trim());
        }
        catch { /* ignore */ }
    }

    void Verarbeiten(string zeile)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(zeile);
        }
        catch
        {
            return; // keine JSON-Zeile
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("type", out var typEl))
                return;

            switch (typEl.GetString())
            {
                case "stream_event":
                    StreamEventVerarbeiten(doc.RootElement);
                    break;
                case "assistant":
                    // Text kommt bereits live über stream_event; hier nur Werkzeug-Hinweise.
                    WerkzeugeVerarbeiten(doc.RootElement);
                    break;
                case "result":
                    var fehler = doc.RootElement.TryGetProperty("is_error", out var e) && e.GetBoolean();
                    TurnFertig?.Invoke(fehler);
                    break;
            }
        }
    }

    /// <summary>Live-Tokens und Aktivität aus den Streaming-Events.</summary>
    void StreamEventVerarbeiten(JsonElement wurzel)
    {
        if (!wurzel.TryGetProperty("event", out var ev)
            || !ev.TryGetProperty("type", out var et))
            return;

        if (et.GetString() != "content_block_delta"
            || !ev.TryGetProperty("delta", out var delta)
            || !delta.TryGetProperty("type", out var dt))
            return;

        if (dt.GetString() == "text_delta" && delta.TryGetProperty("text", out var txt))
        {
            var s = txt.GetString();
            if (!string.IsNullOrEmpty(s))
                AssistentText?.Invoke(s);
        }
    }

    /// <summary>Werkzeug-Hinweise aus dem vollständigen Assistant-Event (Text läuft separat live).</summary>
    void WerkzeugeVerarbeiten(JsonElement wurzel)
    {
        if (!wurzel.TryGetProperty("message", out var msg)
            || !msg.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
            return;

        foreach (var block in content.EnumerateArray())
        {
            var art = block.TryGetProperty("type", out var bt) ? bt.GetString() : null;
            if (art == "tool_use")
            {
                var hinweis = WerkzeugHinweis(block);
                if (hinweis is not null)
                    Werkzeug?.Invoke(hinweis);
            }
        }
    }

    /// <summary>Übersetzt eine Werkzeug-Nutzung in einen knappen, laienverständlichen Hinweis.</summary>
    static string? WerkzeugHinweis(JsonElement toolUse)
    {
        var name = toolUse.TryGetProperty("name", out var n) ? n.GetString() : null;
        string? datei = null;
        if (toolUse.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object
            && input.TryGetProperty("file_path", out var fp))
            datei = Path.GetFileName(fp.GetString() ?? "");

        return name switch
        {
            "Write" => $"📝 Datei angelegt/geschrieben: {datei}",
            "Edit" or "MultiEdit" => $"✏️ Datei geändert: {datei}",
            "Bash" => "⚙️ führt einen Befehl aus …",
            // Lese-/Suchwerkzeuge sind für Laien irrelevantes Rauschen
            "Read" or "Glob" or "Grep" or "TodoWrite" or "LS" => null,
            _ => null,
        };
    }

    public void Dispose()
    {
        try
        {
            if (_proc is { HasExited: false })
            {
                try { _proc.StandardInput.Close(); } catch { /* ignore */ }
                _proc.Kill(entireProcessTree: true);
            }
        }
        catch { /* ignore */ }
        _proc?.Dispose();
        _proc = null;
    }

    // --- Umgebung & Prompt ---------------------------------------------------

    static string Shell() =>
        Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s ? s : "/bin/zsh";

    static string EinfachGequotet(string s) => "'" + s.Replace("'", "'\\''") + "'";

    static string Kontextprompt(string datenOrdner) =>
        $$"""
        Du bist der Assistent im RechnungsTool – einer App für Kleinunternehmer nach § 19 UStG
        (keine Umsatzsteuer). Alle Daten liegen als XML-Dateien im Dateisystem. Dein
        Arbeitsverzeichnis ist der Datenordner der App:
        {{datenOrdner}}

        Du bearbeitest, erstellst und löschst die XML-Dateien dort DIREKT. Die grafische App läuft
        parallel und übernimmt deine Änderungen automatisch und live – der Nutzer sieht sie sofort,
        ein Neuladen ist nicht nötig. Schreibe immer gültiges, wohlgeformtes XML in UTF-8; ungültige
        Dateien erscheinen in der App rot. Orientiere dich am Format vorhandener rechnung-*.xml.

        WICHTIG: Der Nutzer ist KEIN Programmierer. Antworte kurz, freundlich und in einfacher
        Sprache auf Deutsch. Erkläre keine technischen Details (XML, Dateipfade, Befehle) von dir
        aus. Bestätige knapp, was du getan hast (z. B. „Rechnung 2026-21 für Acme GmbH angelegt“).
        Wenn dir Angaben fehlen, frag in einem Satz nach.

        DATEIKONVENTION (andere XML-Dateien sind im Ordner nicht erlaubt):
        - stammdaten.xml          – genau eine; Firmen-, Bank- und Nummernkreis-Daten
        - rechnung-<Nummer>.xml   – eine Datei pro Rechnung, z. B. rechnung-2026-20.xml
        - _rechnung-<Nummer>.xml  – Unterstrich-Präfix = Papierkorb
        - pdf/                    – exportierte PDFs (nicht anfassen)
        Rechnungsnummern: Format JJJJ-NN, fortlaufend (z. B. 2026-19, 2026-20). Der Dateiname
        leitet sich aus der Nummer ab.

        STRUKTUR EINER RECHNUNG (rechnung-*.xml):
        <?xml version="1.0" encoding="utf-8"?>
        <Rechnung>
          <Nummer>2026-20</Nummer>
          <Datum>2026-06-13</Datum>
          <Leistungszeitraum>Mai 2026</Leistungszeitraum>
          <Empfaenger>
            <Name>Acme GmbH</Name>
            <Zusatz>z. Hd. Frau Muster</Zusatz>
            <Strasse>Hafenweg 2</Strasse>
            <Plz>20457</Plz>
            <Ort>Hamburg</Ort>
          </Empfaenger>
          <Positionen>
            <Position>
              <Text>Beratung</Text>
              <Einheit>Std.</Einheit>
              <Menge>8</Menge>
              <Einzelpreis>95</Einzelpreis>
            </Position>
          </Positionen>
          <Hinweis>Vielen Dank!</Hinweis>
          <Gesperrt>false</Gesperrt>
        </Rechnung>

        Felder: Nummer (muss zum Dateinamen passen); Datum (Format JJJJ-MM-TT); Leistungszeitraum
        (Pflichtangabe § 14); Empfaenger (Name, Zusatz optional, Strasse, Plz, Ort); Positionen mit
        je Text, Einheit (z. B. "Std.", "pauschal", "Stk."), Menge, Einzelpreis; Hinweis optional;
        Gesperrt (true/false, optional – fehlt = false).

        STRUKTUR DER STAMMDATEN (stammdaten.xml): <Stammdaten> mit <Firma> (Name, Inhaber, Strasse,
        Plz, Ort, Telefon, Email, Steuernummer, UstIdNr), <Bank> (Kontoinhaber, Name, Iban, Bic),
        <Nummernkreis><StartNummer>…</StartNummer></Nummernkreis> und <ZahlungszielTage> (z. B. 14).

        REGELN:
        - Zahlen (Menge, Einzelpreis): Dezimaltrennzeichen ist der PUNKT (.), z. B. 6.5. Kein
          Währungssymbol.
        - KEINE Umsatzsteuer (§ 19) – nie MwSt. dazurechnen. Den Gesamtbetrag berechnet die App
          selbst; schreibe KEINEN Summenbetrag ins XML.
        - Gesperrt=true heißt: bereits als PDF exportiert/verschickt. Solche Rechnungen nicht
          ungefragt ändern – empfiehl stattdessen eine Kopie als neue Rechnung.
        - Lege keine anderen XML-Dateien im Ordner an. Die xmlns-Attribute am Wurzelelement sind
          optional.
        """;
}
