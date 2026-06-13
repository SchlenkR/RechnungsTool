using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RechnungsTool.Views;

/// <summary>
/// Modale Vorschau der Rechnung (DIN A4) mit Zoom und Pan: Mausrad/Pinch zum Zoomen,
/// Ziehen zum Verschieben, Doppelklick wechselt zwischen Einpassen und Hineinzoomen.
/// </summary>
public partial class VorschauFenster : Window
{
    Bitmap? _bitmap;
    MatrixTransform _transform = null!;

    double _scale = 1;
    double _fitScale = 1;
    double _offsetX;
    double _offsetY;

    bool _eingepasst;
    bool _ziehen;
    Point _letzte;
    double _letzterPinch = 1;

    public VorschauFenster() : this(null) { }

    public VorschauFenster(Bitmap? bild)
    {
        InitializeComponent();
        _bitmap = bild;
        _transform = (MatrixTransform)Bild.RenderTransform!;
        Bild.Source = _bitmap;

        ZoomEin.Click += (_, _) => ZoomBei(1.25, Mitte());
        ZoomAus.Click += (_, _) => ZoomBei(1 / 1.25, Mitte());
        ZoomEinpassen.Click += (_, _) => Einpassen();
        SchliessenBtn.Click += (_, _) => Close();

        Viewport.SizeChanged += (_, _) => { if (!_eingepasst) Einpassen(); };
        Viewport.PointerWheelChanged += AufRad;
        Viewport.PointerPressed += AufDruck;
        Viewport.PointerMoved += AufBewegung;
        Viewport.PointerReleased += AufLoslassen;
        Viewport.DoubleTapped += AufDoppelklick;
        Bild.AddHandler(Gestures.PinchEvent, AufPinch);
        Bild.AddHandler(Gestures.PinchEndedEvent, (_, _) => _letzterPinch = 1);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    Point Mitte() => new(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2);

    void Einpassen()
    {
        var vp = Viewport.Bounds.Size;
        var img = _bitmap?.Size ?? default;
        if (vp.Width <= 0 || vp.Height <= 0 || img.Width <= 0 || img.Height <= 0)
            return;

        _fitScale = Math.Min(vp.Width / img.Width, vp.Height / img.Height) * 0.98;
        _scale = _fitScale;
        _offsetX = (vp.Width - img.Width * _scale) / 2;
        _offsetY = (vp.Height - img.Height * _scale) / 2;
        _eingepasst = true;
        Anwenden();
    }

    void ZoomBei(double faktor, Point zentrum)
    {
        if (_fitScale <= 0)
            return;
        var neu = Math.Clamp(_scale * faktor, _fitScale, _fitScale * 12);
        faktor = neu / _scale;
        _offsetX = zentrum.X - (zentrum.X - _offsetX) * faktor;
        _offsetY = zentrum.Y - (zentrum.Y - _offsetY) * faktor;
        _scale = neu;
        Anwenden();
    }

    void Anwenden()
    {
        _transform.Matrix = Matrix.CreateScale(_scale, _scale)
                            * Matrix.CreateTranslation(_offsetX, _offsetY);
        var prozent = _fitScale > 0 ? Math.Round(_scale / _fitScale * 100) : 100;
        ZoomText.Text = $"{prozent:0} %";
    }

    void AufRad(object? sender, PointerWheelEventArgs e)
    {
        ZoomBei(e.Delta.Y > 0 ? 1.15 : 1 / 1.15, e.GetPosition(Viewport));
        e.Handled = true;
    }

    void AufDruck(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed)
            return;
        _ziehen = true;
        _letzte = e.GetPosition(Viewport);
        e.Pointer.Capture(Viewport);
    }

    void AufBewegung(object? sender, PointerEventArgs e)
    {
        if (!_ziehen)
            return;
        var pos = e.GetPosition(Viewport);
        _offsetX += pos.X - _letzte.X;
        _offsetY += pos.Y - _letzte.Y;
        _letzte = pos;
        Anwenden();
    }

    void AufLoslassen(object? sender, PointerReleasedEventArgs e)
    {
        _ziehen = false;
        e.Pointer.Capture(null);
    }

    void AufDoppelklick(object? sender, TappedEventArgs e)
    {
        // Nah am Einpassen → hineinzoomen, sonst zurück aufs Einpassen
        if (_scale <= _fitScale * 1.05)
            ZoomBei(2.5, e.GetPosition(Viewport));
        else
            Einpassen();
    }

    void AufPinch(object? sender, PinchEventArgs e)
    {
        var faktor = e.Scale / _letzterPinch;
        _letzterPinch = e.Scale;
        ZoomBei(faktor, Mitte());
    }
}
