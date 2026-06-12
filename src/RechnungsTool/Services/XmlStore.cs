using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using RechnungsTool.Models;

namespace RechnungsTool.Services;

/// <summary>
/// Dateikonvention im Datenordner:
///   - genau eine  stammdaten.xml
///   - je Rechnung eine  rechnung-&lt;Nummer&gt;.xml
///   - Papierkorb: _rechnung-&lt;Nummer&gt;.xml (Unterstrich-Präfix)
/// Andere XML-Dateien sind nicht erlaubt und werden als Verstoß gemeldet.
/// </summary>
public static class XmlStore
{
    public const string StammdatenDateiName = "stammdaten.xml";
    public const string RechnungPrefix = "rechnung-";
    public const string PapierkorbPrefix = "_";

    static readonly XmlSerializer RechnungSerializer = new(typeof(Rechnung));
    static readonly XmlSerializer StammdatenSerializer = new(typeof(Stammdaten));

    public record OrdnerInhalt(
        Stammdaten? Stammdaten,
        string? StammdatenFehler,
        List<RechnungsDatei> Rechnungen,
        List<RechnungsDatei> Papierkorb,
        List<string> FremdeDateien);

    public static OrdnerInhalt OrdnerLaden(string ordner)
    {
        Directory.CreateDirectory(ordner);

        Stammdaten? stammdaten = null;
        string? stammdatenFehler = null;
        var rechnungen = new List<RechnungsDatei>();
        var papierkorb = new List<RechnungsDatei>();
        var fremde = new List<string>();

        foreach (var pfad in Directory.EnumerateFiles(ordner, "*.xml", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(pfad);

            if (string.Equals(name, StammdatenDateiName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    stammdaten = Deserialisieren<Stammdaten>(StammdatenSerializer, pfad);
                }
                catch (Exception ex)
                {
                    stammdatenFehler = Fehlertext(ex);
                }
            }
            else if (name.StartsWith(RechnungPrefix, StringComparison.OrdinalIgnoreCase))
            {
                rechnungen.Add(RechnungsDateiLaden(pfad, imPapierkorb: false));
            }
            else if (name.StartsWith(PapierkorbPrefix + RechnungPrefix, StringComparison.OrdinalIgnoreCase))
            {
                papierkorb.Add(RechnungsDateiLaden(pfad, imPapierkorb: true));
            }
            else
            {
                fremde.Add(name);
            }
        }

        return new OrdnerInhalt(stammdaten, stammdatenFehler, rechnungen, papierkorb, fremde);
    }

    static RechnungsDatei RechnungsDateiLaden(string pfad, bool imPapierkorb)
    {
        var datei = new RechnungsDatei { Pfad = pfad, ImPapierkorb = imPapierkorb };
        try
        {
            datei.Rechnung = Deserialisieren<Rechnung>(RechnungSerializer, pfad);
        }
        catch (Exception ex)
        {
            datei.Fehler = Fehlertext(ex);
        }
        return datei;
    }

    /// <summary>Verschiebt eine Rechnung in den Papierkorb (Unterstrich-Präfix im Dateinamen).</summary>
    public static string InPapierkorb(string pfad)
    {
        var ziel = Path.Combine(Path.GetDirectoryName(pfad)!, PapierkorbPrefix + Path.GetFileName(pfad));
        File.Move(pfad, ziel);
        return ziel;
    }

    /// <summary>Stellt eine Rechnung aus dem Papierkorb wieder her; wirft IOException bei Namenskonflikt.</summary>
    public static string AusPapierkorb(string pfad)
    {
        var name = Path.GetFileName(pfad);
        if (!name.StartsWith(PapierkorbPrefix, StringComparison.Ordinal))
            return pfad;

        var ziel = Path.Combine(Path.GetDirectoryName(pfad)!, name[PapierkorbPrefix.Length..]);
        if (File.Exists(ziel))
            throw new IOException($"Es existiert bereits eine Datei „{Path.GetFileName(ziel)}“.");

        File.Move(pfad, ziel);
        return ziel;
    }

    public static void Loeschen(string pfad) => File.Delete(pfad);

    /// <summary>
    /// Serialisiert eine Rechnung in-memory – Referenzdarstellung für den
    /// Dirty-Vergleich (aktueller Editorzustand vs. zuletzt gespeicherter Zustand).
    /// </summary>
    public static string AlsXml(Rechnung rechnung) => AlsXml(RechnungSerializer, rechnung);

    /// <summary>Gegenstück zu <see cref="AlsXml(Rechnung)"/>, z. B. zum Verwerfen von Änderungen.</summary>
    public static Rechnung RechnungAusXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        return (Rechnung)RechnungSerializer.Deserialize(reader)!;
    }

    public static string AlsXml(Stammdaten stammdaten) => AlsXml(StammdatenSerializer, stammdaten);

    static string AlsXml(XmlSerializer serializer, object wert)
    {
        using var writer = new StringWriter();
        using var xml = XmlWriter.Create(writer, new XmlWriterSettings { Indent = true });
        serializer.Serialize(xml, wert);
        return writer.ToString();
    }

    public static string RechnungsDateiName(string nummer) =>
        RechnungPrefix + DateinamenSicher(nummer) + ".xml";

    /// <summary>Speichert die Rechnung; bei geänderter Nummer wird die alte Datei entfernt.</summary>
    public static string RechnungSpeichern(string ordner, Rechnung rechnung, string? bisherigerPfad)
    {
        Directory.CreateDirectory(ordner);
        var pfad = Path.Combine(ordner, RechnungsDateiName(rechnung.Nummer));
        Serialisieren(RechnungSerializer, pfad, rechnung);

        if (bisherigerPfad is not null
            && !string.Equals(bisherigerPfad, pfad, StringComparison.Ordinal)
            && File.Exists(bisherigerPfad))
        {
            File.Delete(bisherigerPfad);
        }

        return pfad;
    }

    public static void StammdatenSpeichern(string ordner, Stammdaten stammdaten)
    {
        Directory.CreateDirectory(ordner);
        Serialisieren(StammdatenSerializer, Path.Combine(ordner, StammdatenDateiName), stammdaten);
    }

    static T Deserialisieren<T>(XmlSerializer serializer, string pfad)
    {
        using var stream = File.OpenRead(pfad);
        return (T)serializer.Deserialize(stream)!;
    }

    static void Serialisieren(XmlSerializer serializer, string pfad, object wert)
    {
        var einstellungen = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
        };
        using var writer = XmlWriter.Create(pfad, einstellungen);
        serializer.Serialize(writer, wert);
    }

    static string Fehlertext(Exception ex)
    {
        // XmlSerializer verpackt den eigentlichen Fehler meist in der InnerException
        var kern = ex.InnerException ?? ex;
        return kern.Message;
    }

    public static string DateinamenSicher(string nummer)
    {
        var sb = new StringBuilder(nummer.Length);
        foreach (var c in nummer)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return sb.ToString();
    }
}
