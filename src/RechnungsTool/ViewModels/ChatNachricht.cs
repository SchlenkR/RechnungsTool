using CommunityToolkit.Mvvm.ComponentModel;

namespace RechnungsTool.ViewModels;

public enum NachrichtRolle
{
    Ich,
    Claude,
    System,
}

/// <summary>Eine Zeile im Chat: eigene Nachricht, Claude-Antwort oder ein System-/Aktionshinweis.</summary>
public partial class ChatNachricht : ObservableObject
{
    public NachrichtRolle Rolle { get; init; }

    /// <summary>Veränderbar, damit gestreamter Antworttext an dieselbe Blase angehängt werden kann.</summary>
    [ObservableProperty] private string text = "";

    public bool IstIch => Rolle == NachrichtRolle.Ich;
    public bool IstClaude => Rolle == NachrichtRolle.Claude;
    public bool IstSystem => Rolle == NachrichtRolle.System;

    public static ChatNachricht Ich(string text) => new() { Rolle = NachrichtRolle.Ich, Text = text };
    public static ChatNachricht Claude(string text = "") => new() { Rolle = NachrichtRolle.Claude, Text = text };
    public static ChatNachricht System(string text) => new() { Rolle = NachrichtRolle.System, Text = text };
}
