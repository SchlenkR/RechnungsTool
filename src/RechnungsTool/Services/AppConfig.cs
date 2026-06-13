using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RechnungsTool.Services;

/// <summary>
/// Konfiguration in ~/Library/Application Support/RechnungsTool/config.json.
/// Dort lässt sich der Datenordner (lokal oder Share) anpassen.
/// </summary>
public class AppConfig
{
    public string DatenOrdner { get; set; } = "~/Documents/Rechnungen";

    /// <summary>Anzeige-Skalierung der gesamten Oberfläche (1.0 = 100 %).</summary>
    public double UiSkalierung { get; set; } = 0.7;

    [JsonIgnore]
    public string DatenOrdnerAbsolut => PfadAufloesen(DatenOrdner);

    public static string AppDatenOrdner =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "RechnungsTool");

    public static string ConfigPfad => Path.Combine(AppDatenOrdner, "config.json");

    static readonly JsonSerializerOptions JsonOptionen = new() { WriteIndented = true };

    public static AppConfig Laden()
    {
        if (File.Exists(ConfigPfad))
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPfad));
            if (config is not null)
                return config;
        }

        var neu = new AppConfig();
        neu.Speichern();
        return neu;
    }

    public void Speichern()
    {
        Directory.CreateDirectory(AppDatenOrdner);
        File.WriteAllText(ConfigPfad, JsonSerializer.Serialize(this, JsonOptionen));
    }

    static string PfadAufloesen(string pfad)
    {
        if (pfad.StartsWith("~"))
            pfad = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                   + pfad[1..];
        return Path.GetFullPath(pfad);
    }
}
