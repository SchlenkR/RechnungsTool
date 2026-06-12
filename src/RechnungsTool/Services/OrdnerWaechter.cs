using System;
using System.IO;

namespace RechnungsTool.Services;

/// <summary>
/// Überwacht das Root des Datenordners auf externe XML-Änderungen.
/// Eigene Schreibvorgänge (über <see cref="XmlStore"/>) und das pdf-Unterverzeichnis
/// lösen keine Meldung aus.
/// </summary>
public sealed class OrdnerWaechter : IDisposable
{
    FileSystemWatcher? _watcher;

    /// <summary>Wird auf einem Hintergrund-Thread ausgelöst.</summary>
    public event Action? ExterneAenderung;

    public void Ueberwachen(string ordner)
    {
        _watcher?.Dispose();
        _watcher = null;

        if (!Directory.Exists(ordner))
            return;

        _watcher = new FileSystemWatcher(ordner, "*.xml")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += Ausgeloest;
        _watcher.Created += Ausgeloest;
        _watcher.Deleted += Ausgeloest;
        _watcher.Renamed += Ausgeloest;
        _watcher.EnableRaisingEvents = true;
    }

    void Ausgeloest(object sender, FileSystemEventArgs e)
    {
        if (!XmlStore.EigenerSchreibvorgangAktiv)
            ExterneAenderung?.Invoke();
    }

    public void Dispose() => _watcher?.Dispose();
}
