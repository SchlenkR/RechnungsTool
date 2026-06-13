using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RechnungsTool.Views;

/// <summary>
/// Zoombare Rechnungsvorschau für den modalen Dialog. Standardmäßig wird das ganze
/// Dokument eingepasst. Scrollen scrollt (wenn hineingezoomt); gezoomt wird per
/// Pinch-Geste oder über die Buttons – nicht per Mausrad.
/// </summary>
public partial class VorschauView : UserControl
{
    Bitmap? _bitmap;
    ScaleTransform _skala = null!;

    double _zoom = 1;        // 1 = eingepasst
    double _einpassen = 1;   // Skalierung, bei der das Dokument genau passt
    double _letzterPinch = 1;

    public VorschauView() : this(null) { }

    public VorschauView(Bitmap? bild)
    {
        InitializeComponent();

        _bitmap = bild;
        Bild.Source = bild;
        _skala = (ScaleTransform)Ltc.LayoutTransform!;

        ZoomEin.Click += (_, _) => ZoomSetzen(_zoom * 1.25);
        ZoomAus.Click += (_, _) => ZoomSetzen(_zoom / 1.25);
        ZoomFit.Click += (_, _) => ZoomSetzen(1);

        Scroll.SizeChanged += (_, _) => Anwenden();

        Bild.AddHandler(Gestures.PinchEvent, (_, e) =>
        {
            var faktor = ((PinchEventArgs)e).Scale / _letzterPinch;
            _letzterPinch = ((PinchEventArgs)e).Scale;
            ZoomSetzen(_zoom * faktor);
        });
        Bild.AddHandler(Gestures.PinchEndedEvent, (_, _) => _letzterPinch = 1);
    }

    void ZoomSetzen(double zoom)
    {
        _zoom = Math.Clamp(zoom, 1, 8);
        Anwenden();
        ZoomText.Text = $"{Math.Round(_zoom * 100):0} %";
    }

    void Anwenden()
    {
        if (_bitmap is null)
            return;
        var vp = Scroll.Bounds.Size;
        var img = _bitmap.Size;
        if (vp.Width <= 0 || vp.Height <= 0 || img.Width <= 0 || img.Height <= 0)
            return;

        _einpassen = Math.Min(vp.Width / img.Width, vp.Height / img.Height) * 0.99;
        var s = _einpassen * _zoom;
        _skala.ScaleX = s;
        _skala.ScaleY = s;
    }
}
