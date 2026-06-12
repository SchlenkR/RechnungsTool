using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using FluentAvalonia.UI.Controls;
using RechnungsTool.ViewModels;

namespace RechnungsTool.Views;

public class Dialoge : IDialoge
{
    public async Task<bool> BestaetigenAsync(string titel, string text, string aktion)
    {
        var dialog = new ContentDialog
        {
            Title = titel,
            Content = text,
            PrimaryButtonText = aktion,
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task InfoAsync(string titel, string text)
    {
        var dialog = new ContentDialog
        {
            Title = titel,
            Content = text,
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }

    public async Task<SchliessenWahl> SchliessenAbfragenAsync(int anzahlUngespeichert)
    {
        var dialog = new ContentDialog
        {
            Title = "Ungespeicherte Änderungen",
            Content = anzahlUngespeichert == 1
                ? "Eine Rechnung hat ungespeicherte Änderungen."
                : $"{anzahlUngespeichert} Rechnungen haben ungespeicherte Änderungen.",
            PrimaryButtonText = "Alle speichern",
            SecondaryButtonText = "Nicht speichern",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => SchliessenWahl.AlleSpeichern,
            ContentDialogResult.Secondary => SchliessenWahl.NichtSpeichern,
            _ => SchliessenWahl.Abbrechen,
        };
    }

    public Task EinstellungenAnzeigenAsync(EinstellungenViewModel einstellungen) =>
        GrossenDialogZeigenAsync("Einstellungen", new EinstellungenView { DataContext = einstellungen });

    public Task StammdatenAnzeigenAsync(StammdatenViewModel stammdaten) =>
        GrossenDialogZeigenAsync("Stammdaten", new StammdatenView { DataContext = stammdaten });

    public Task AuswertungAnzeigenAsync(AuswertungViewModel auswertung) =>
        GrossenDialogZeigenAsync("Auswertung", new AuswertungView { DataContext = auswertung });

    /// <summary>Modaler Dialog, maximiert mit 120 px Abstand zu allen Fensterkanten.</summary>
    static async Task GrossenDialogZeigenAsync(string titel, Avalonia.Controls.Control inhalt)
    {
        var fenster = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var breite = Math.Max(420, (fenster?.ClientSize.Width ?? 1200) - 240);
        var hoehe = Math.Max(320, (fenster?.ClientSize.Height ?? 800) - 240);

        // Dialog-Innenabstände (Padding, Titel, Buttonzeile) ausgleichen
        inhalt.MinWidth = breite - 48;
        inhalt.MinHeight = hoehe - 150;
        inhalt.MaxHeight = hoehe - 150;

        var dialog = new ContentDialog
        {
            Title = titel,
            Content = inhalt,
            CloseButtonText = "Schließen",
        };
        dialog.Resources["ContentDialogMaxWidth"] = breite;
        dialog.Resources["ContentDialogMaxHeight"] = hoehe;
        await dialog.ShowAsync();
    }

    public async Task<FreigebenWahl> FreigebenAbfragenAsync(string nummer)
    {
        var dialog = new ContentDialog
        {
            Title = "Rechnung ist gesperrt",
            Content = $"Die Rechnung „{nummer}“ wurde bereits als PDF gespeichert und ist " +
                      "vermutlich schon verschickt.\n\n" +
                      "Empfehlung: Lege eine Rechnungskopie als neue Rechnung an, statt eine " +
                      "verschickte Rechnung nachträglich zu ändern. Alternativ kannst du die " +
                      "Rechnung zum Editieren entsperren.",
            PrimaryButtonText = "Rechnungskopie anlegen",
            SecondaryButtonText = "Entsperren",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => FreigebenWahl.KopieAnlegen,
            ContentDialogResult.Secondary => FreigebenWahl.Entsperren,
            _ => FreigebenWahl.Abbrechen,
        };
    }

    public async Task<PdfKonfliktWahl> PdfKonfliktAsync(string dateiName)
    {
        var dialog = new ContentDialog
        {
            Title = "PDF existiert bereits",
            Content = $"Für diese Rechnung gibt es bereits die Datei „{dateiName}“.\n\nWie soll verfahren werden?",
            PrimaryButtonText = "Überschreiben",
            SecondaryButtonText = "Als Korrektur exportieren",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Secondary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => PdfKonfliktWahl.Ueberschreiben,
            ContentDialogResult.Secondary => PdfKonfliktWahl.Korrektur,
            _ => PdfKonfliktWahl.Abbrechen,
        };
    }
}
