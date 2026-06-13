using System;
using System.IO;
using System.Threading;

namespace RechnungsTool.Services;

/// <summary>
/// Überwacht das Root des Datenordners auf externe XML-Änderungen.
/// Eigene Schreibvorgänge (über <see cref="XmlStore"/>) und das pdf-Unterverzeichnis
/// lösen keine Meldung aus. Event-Bursts (mehrere FileSystemWatcher-Events für einen
/// logischen Vorgang, halbfertige Schreibvorgänge einer KI) werden über eine kurze
/// Ruhefrist zu einer einzigen Meldung zusammengefasst.
/// </summary>
public sealed class OrdnerWaechter : IDisposable
{
    static readonly TimeSpan Ruhefrist = TimeSpan.FromMilliseconds(300);

    FileSystemWatcher? _watcher;
    Timer? _entprellung;

    /// <summary>Wird (entprellt) auf einem Hintergrund-Thread ausgelöst.</summary>
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
        // Eigene Schreibvorgänge ignorieren (inkl. Nachlauffrist im XmlStore).
        if (XmlStore.EigenerSchreibvorgangAktiv)
            return;

        // Jede Aktivität schiebt die Frist nach hinten; erst nach Ruhe wird gemeldet.
        _entprellung ??= new Timer(_ =>
        {
            if (!XmlStore.EigenerSchreibvorgangAktiv)
                ExterneAenderung?.Invoke();
        });
        _entprellung.Change(Ruhefrist, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _entprellung?.Dispose();
    }
}
