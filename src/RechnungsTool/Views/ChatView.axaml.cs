using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RechnungsTool.ViewModels;

namespace RechnungsTool.Views;

public partial class ChatView : UserControl
{
    // Hält die Ansicht während des Streamings am unteren Rand (Text wächst sonst aus dem Bild).
    readonly DispatcherTimer _scrollTimer;

    public ChatView()
    {
        InitializeComponent();

        var feld = this.FindControl<TextBox>("EingabeFeld")!;
        feld.AddHandler(KeyDownEvent, EingabeKeyDown, RoutingStrategies.Tunnel);

        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _scrollTimer.Tick += (_, _) => this.FindControl<ScrollViewer>("Verlauf")?.ScrollToEnd();

        AttachedToVisualTree += BeimAnhaengen;
        DetachedFromVisualTree += BeimAbloesen;
    }

    void BeimAnhaengen(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (Design.IsDesignMode || DataContext is not ChatViewModel vm)
            return;

        vm.Nachrichten.CollectionChanged += NachrichtHinzu;
        vm.PropertyChanged += VmGeaendert;
        vm.Starten();
    }

    void BeimAbloesen(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _scrollTimer.Stop();
        if (DataContext is ChatViewModel vm)
        {
            vm.Nachrichten.CollectionChanged -= NachrichtHinzu;
            vm.PropertyChanged -= VmGeaendert;
            vm.Stoppen();
        }
    }

    void VmGeaendert(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatViewModel.Beschaeftigt) || sender is not ChatViewModel vm)
            return;

        // Während Claude arbeitet/streamt am unteren Rand bleiben.
        if (vm.Beschaeftigt)
            _scrollTimer.Start();
        else
        {
            _scrollTimer.Stop();
            NachrichtHinzu(null, null!);
        }
    }

    void NachrichtHinzu(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Nach dem Layout-Durchlauf ans Ende scrollen.
        Dispatcher.UIThread.Post(
            () => this.FindControl<ScrollViewer>("Verlauf")?.ScrollToEnd(),
            DispatcherPriority.Background);
    }

    void EingabeKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter sendet, Shift+Enter fügt eine neue Zeile ein.
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        if (DataContext is ChatViewModel vm && vm.SendenCommand.CanExecute(null))
            vm.SendenCommand.Execute(null);
        e.Handled = true;
    }
}
