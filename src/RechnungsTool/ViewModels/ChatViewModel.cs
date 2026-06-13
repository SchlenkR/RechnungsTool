using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RechnungsTool.Services;

namespace RechnungsTool.ViewModels;

/// <summary>
/// Chat-Panel für den eingebetteten Claude-Assistenten (headless, kein Terminal).
/// Hält die Nachrichtenliste und vermittelt zwischen UI und <see cref="ClaudeChat"/>.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    readonly ClaudeChat _claude = new();
    readonly string _datenOrdner;

    // Aktuelle, noch wachsende Claude-Antwortblase (gestreamter Text wird angehängt)
    ChatNachricht? _aktuelleAntwort;
    bool _gestartet;

    // Anzahl gesendeter, noch nicht abgeschlossener Turns (Steering: man darf nachschieben,
    // während Claude noch arbeitet – die Nachrichten werden der Reihe nach abgearbeitet).
    int _ausstehend;

    public ObservableCollection<ChatNachricht> Nachrichten { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendenCommand))]
    private string eingabe = "";

    /// <summary>Claude arbeitet gerade an einem Turn – Eingabe ist gesperrt.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendenCommand))]
    private bool beschaeftigt;

    /// <summary>Footer eingeklappt: nur Kopf- und Eingabezeile sichtbar, Verlauf ausgeblendet.</summary>
    [ObservableProperty] private bool minimiert;

    /// <summary>Gewünschte Footer-Höhe (eingeklappt vs. ausgeklappt) – vom MainWindow gebunden.</summary>
    public double FooterHoehe => Minimiert ? 52 : 360;

    partial void OnMinimiertChanged(bool value) => OnPropertyChanged(nameof(FooterHoehe));

    [RelayCommand]
    void MinimierenUmschalten() => Minimiert = !Minimiert;

    /// <summary>
    /// Sichtbarer „arbeitet…“-Indikator: an, solange Claude beschäftigt ist und gerade
    /// kein Text live in eine Blase einläuft (z. B. direkt nach dem Senden oder während
    /// ein Werkzeug ausgeführt wird).
    /// </summary>
    [ObservableProperty] private bool arbeitet;

    public ChatViewModel(string datenOrdner)
    {
        _datenOrdner = datenOrdner;

        _claude.AssistentText += t => Auf(() => AntwortAnhaengen(t));
        _claude.Werkzeug += t => Auf(() => HinweisAnhaengen(t));
        _claude.TurnFertig += fehler => Auf(() => TurnAbschliessen(fehler));
        _claude.Systemmeldung += t => Auf(() => Nachrichten.Add(ChatNachricht.System(t)));
        _claude.Beendet += () => Auf(ProzessBeendet);
    }

    /// <summary>Startet den Claude-Prozess (idempotent; aus der View bei Sichtbarkeit aufgerufen).</summary>
    public void Starten()
    {
        if (_gestartet)
            return;
        _gestartet = true;

        Nachrichten.Add(ChatNachricht.System(
            "Hallo! Ich helfe dir mit deinen Rechnungen. Schreib einfach, was du brauchst – " +
            "z. B. „Lege eine neue Rechnung für die Acme GmbH über 8 Stunden Beratung à 95 € an.“"));
        _claude.Starten(_datenOrdner);
    }

    public void Stoppen() => _claude.Dispose();

    // Steering: auch während Claude arbeitet darf gesendet werden (wird angehängt).
    bool KannSenden() => Eingabe.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(KannSenden))]
    async Task SendenAsync()
    {
        var text = Eingabe.Trim();
        if (text.Length == 0)
            return;

        Nachrichten.Add(ChatNachricht.Ich(text));
        Eingabe = "";
        _ausstehend++;
        Beschaeftigt = true;
        AktualisiereArbeitet();

        await _claude.SendenAsync(text);
    }

    [RelayCommand]
    void Neustarten()
    {
        _claude.Dispose();
        _gestartet = false;
        Beschaeftigt = false;
        _ausstehend = 0;
        _aktuelleAntwort = null;
        AktualisiereArbeitet();
        Nachrichten.Clear();
        Starten();
    }

    // --- Eingehende Ereignisse (bereits auf den UI-Thread gebracht) ----------

    void AntwortAnhaengen(string text)
    {
        if (_aktuelleAntwort is null)
        {
            _aktuelleAntwort = ChatNachricht.Claude(text);
            Nachrichten.Add(_aktuelleAntwort);
        }
        else
        {
            _aktuelleAntwort.Text += text;
        }
        AktualisiereArbeitet();
    }

    void HinweisAnhaengen(string text)
    {
        // Nach einem Aktionshinweis beginnt folgender Text eine neue Antwortblase.
        _aktuelleAntwort = null;
        Nachrichten.Add(ChatNachricht.System(text));
        AktualisiereArbeitet();
    }

    void TurnAbschliessen(bool fehler)
    {
        if (fehler && _aktuelleAntwort is null)
            Nachrichten.Add(ChatNachricht.System(
                "Da ist etwas schiefgelaufen. Versuch es bitte noch einmal oder klick „Neu starten“."));
        _ausstehend = Math.Max(0, _ausstehend - 1);
        Beschaeftigt = _ausstehend > 0;
        _aktuelleAntwort = null;
        AktualisiereArbeitet();
    }

    void ProzessBeendet()
    {
        if (!_gestartet)
            return;
        _ausstehend = 0;
        Beschaeftigt = false;
        AktualisiereArbeitet();
        Nachrichten.Add(ChatNachricht.System("Die Sitzung wurde beendet. Klick „Neu starten“, um weiterzumachen."));
    }

    void AktualisiereArbeitet() => Arbeitet = Beschaeftigt && _aktuelleAntwort is null;

    static void Auf(System.Action aktion) => Dispatcher.UIThread.Post(aktion);
}
