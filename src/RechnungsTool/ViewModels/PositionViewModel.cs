using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RechnungsTool.Models;

namespace RechnungsTool.ViewModels;

/// <summary>Eine editierbare Leistungsposition; Zahlen werden als Text gehalten (Komma oder Punkt).</summary>
public partial class PositionViewModel : ObservableObject
{
    static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");

    [ObservableProperty] private string text = "";
    [ObservableProperty] private string einheit = "Std.";
    [ObservableProperty] private string mengeText = "1";
    [ObservableProperty] private string einzelpreisText = "0";

    /// <summary>Validierungsfehler dieser Position (Geschäftslogik), leer = valide.</summary>
    [ObservableProperty] private string fehler = "";

    public event Action? Geaendert;

    public PositionViewModel()
    {
    }

    public PositionViewModel(Position p)
    {
        text = p.Text;
        einheit = p.Einheit;
        mengeText = p.Menge.ToString("0.##", DeDe);
        einzelpreisText = p.Einzelpreis.ToString("0.##", DeDe);
    }

    public decimal Menge => Parsen(MengeText) ?? 0;
    public decimal Einzelpreis => Parsen(EinzelpreisText) ?? 0;
    public bool MengeUngueltig => Parsen(MengeText) is null;
    public bool EinzelpreisUngueltig => Parsen(EinzelpreisText) is null;

    public string BetragText =>
        Math.Round(Menge * Einzelpreis, 2, MidpointRounding.AwayFromZero).ToString("N2", DeDe) + " €";

    public Position ZuModel() => new()
    {
        Text = Text.Trim(),
        Einheit = Einheit.Trim(),
        Menge = Menge,
        Einzelpreis = Einzelpreis,
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Text) or nameof(Einheit) or nameof(MengeText) or nameof(EinzelpreisText))
        {
            OnPropertyChanged(nameof(BetragText));
            Geaendert?.Invoke();
        }
    }

    static decimal? Parsen(string eingabe)
    {
        var s = eingabe.Trim();
        if (s.Length == 0)
            return null;

        // Deutsche Eingabe (Komma als Dezimaltrenner, Punkt als Tausender) und Punkt-Notation akzeptieren
        if (s.Contains(','))
            s = s.Replace(".", "").Replace(',', '.');

        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var wert)
            ? wert
            : null;
    }
}
