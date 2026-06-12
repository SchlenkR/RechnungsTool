using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace RechnungsTool.Services;

/// <summary>
/// HTML → PDF/Vorschau über Headless-Chromium (PuppeteerSharp, MIT-Lizenz).
/// Chromium wird beim ersten Aufruf einmalig in den App-Datenordner geladen.
/// </summary>
public class PdfDienst
{
    readonly SemaphoreSlim _schleuse = new(1, 1);
    IBrowser? _browser;

    public bool BrowserBereit =>
        _browser is { IsClosed: false }
        || new BrowserFetcher(new BrowserFetcherOptions { Path = ChromiumOrdner })
            .GetInstalledBrowsers().Any();

    static string ChromiumOrdner => Path.Combine(AppConfig.AppDatenOrdner, "chromium");

    async Task<IBrowser> BrowserHolenAsync()
    {
        if (_browser is { IsClosed: false })
            return _browser;

        var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = ChromiumOrdner });
        var installiert = await fetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = installiert.GetExecutablePath(),
        });
        return _browser;
    }

    /// <summary>Rendert die Rechnung als PNG (DIN-A4-Proportionen) für die Live-Vorschau.</summary>
    public async Task<byte[]> VorschauPngAsync(string html)
    {
        await _schleuse.WaitAsync();
        try
        {
            var browser = await BrowserHolenAsync();
            var page = await browser.NewPageAsync();
            try
            {
                // 794 x 1123 px entsprechen 210 x 297 mm bei 96 dpi
                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = 794,
                    Height = 1123,
                    DeviceScaleFactor = 2,
                });
                await page.SetContentAsync(html);
                return await page.ScreenshotDataAsync(new ScreenshotOptions { FullPage = true });
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        finally
        {
            _schleuse.Release();
        }
    }

    /// <summary>Schließt den Headless-Browser; ohne diesen Aufruf überlebt der Chromium-Prozess das App-Ende.</summary>
    public async Task BeendenAsync()
    {
        if (_browser is { IsClosed: false } browser)
        {
            _browser = null;
            await browser.CloseAsync();
        }
    }

    /// <param name="fusszeile">Auf jeder Seite wiederholte Fußzeile (Chromium-Template).</param>
    public async Task PdfSpeichernAsync(string html, string pdfPfad, string fusszeile)
    {
        await _schleuse.WaitAsync();
        try
        {
            var browser = await BrowserHolenAsync();
            var page = await browser.NewPageAsync();
            try
            {
                await page.SetContentAsync(html);
                // Print-CSS aktivieren (blendet u. a. die Bildschirm-Fußzeile aus)
                await page.EmulateMediaTypeAsync(PuppeteerSharp.Media.MediaType.Print);
                await page.PdfAsync(pdfPfad, new PdfOptions
                {
                    Format = PuppeteerSharp.Media.PaperFormat.A4,
                    PrintBackground = true,
                    DisplayHeaderFooter = true,
                    HeaderTemplate = "<span></span>",
                    FooterTemplate = fusszeile,
                    // DIN-5008-Ränder; unten Platz für die Fußzeile
                    MarginOptions = new PuppeteerSharp.Media.MarginOptions
                    {
                        Top = "16mm",
                        Bottom = "34mm",
                        Left = "25mm",
                        Right = "20mm",
                    },
                });
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        finally
        {
            _schleuse.Release();
        }
    }
}
