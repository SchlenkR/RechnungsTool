using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace RechnungsTool.Services;

public record UpdateInfo(string Tag, string AssetName, string DownloadUrl);

/// <summary>Over-the-air-Updates über die GitHub-Releases des (öffentlichen) Repos.</summary>
public class UpdateDienst
{
    public const string Repo = "SchlenkR/RechnungsTool";

    /// <summary>Versionsanteil der InformationalVersion (ohne Commit-Metadaten).</summary>
    public static string AktuelleVersion
    {
        get
        {
            var info = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0";
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
    }

    /// <summary>Pfad des .app-Bundles oder null (z. B. bei dotnet run).</summary>
    public static string? BundlePfad()
    {
        var bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        return bundle.EndsWith(".app", StringComparison.Ordinal) ? bundle : null;
    }

    static string AssetSuffix =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "macos-arm64.zip"
            : "macos-x64.zip";

    /// <summary>Liefert das neueste Release, falls es neuer als die laufende Version ist.</summary>
    public async Task<UpdateInfo?> PruefenAsync()
    {
        using var http = HttpClientErzeugen();
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");

        using var dokument = JsonDocument.Parse(json);
        var wurzel = dokument.RootElement;
        var tag = wurzel.GetProperty("tag_name").GetString() ?? "";

        if (!IstNeuer(tag, AktuelleVersion))
            return null;

        foreach (var asset in wurzel.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                return new UpdateInfo(tag, name,
                    asset.GetProperty("browser_download_url").GetString() ?? "");
        }
        return null;
    }

    public static bool IstNeuer(string tag, string aktuelleVersion) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var neu)
        && Version.TryParse(aktuelleVersion, out var aktuell)
        && neu > aktuell;

    /// <summary>Lädt das Update-Zip in einen Temp-Ordner und liefert den Pfad.</summary>
    public async Task<string> HerunterladenAsync(UpdateInfo update)
    {
        var ordner = Directory.CreateTempSubdirectory("rechnungstool-update").FullName;
        var zip = Path.Combine(ordner, update.AssetName);

        using var http = HttpClientErzeugen();
        await using var quelle = await http.GetStreamAsync(update.DownloadUrl);
        await using var ziel = File.Create(zip);
        await quelle.CopyToAsync(ziel);

        return zip;
    }

    /// <summary>
    /// Startet ein abgekoppeltes Swap-Skript, das nach App-Ende das Bundle ersetzt
    /// und die neue Version startet. Danach muss die App sofort beendet werden.
    /// </summary>
    public void InstallierenNachBeenden(string zipPfad)
    {
        var bundle = BundlePfad()
            ?? throw new InvalidOperationException("Update nur aus dem installierten App-Bundle möglich.");

        var skript = Path.Combine(Path.GetTempPath(), "rechnungstool-update.sh");
        File.WriteAllText(skript, $"""
            #!/bin/bash
            PID=$1
            while kill -0 "$PID" 2>/dev/null; do sleep 0.3; done
            T="$(mktemp -d)"
            ditto -x -k "{zipPfad}" "$T"
            rm -rf "{bundle}"
            ditto "$T/RechnungsTool.app" "{bundle}"
            rm -rf "$T" "{Path.GetDirectoryName(zipPfad)}"
            open "{bundle}"
            """);

        Process.Start(new ProcessStartInfo("/bin/bash", $"\"{skript}\" {Environment.ProcessId}")
        {
            UseShellExecute = false,
        });
    }

    static HttpClient HttpClientErzeugen()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RechnungsTool", AktuelleVersion));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }
}
