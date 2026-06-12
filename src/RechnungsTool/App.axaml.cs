using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using RechnungsTool.ViewModels;
using RechnungsTool.Views;

namespace RechnungsTool;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var viewModel = new MainWindowViewModel(new Dialoge());
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            // Menüleisten-Eintrag (macOS): Anwendung neu laden mit Cmd+R
            NativeMenu.SetMenu(this, new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem("Neu laden")
                    {
                        Command = viewModel.VollNeuLadenCommand,
                        Gesture = new Avalonia.Input.KeyGesture(
                            Avalonia.Input.Key.R, Avalonia.Input.KeyModifiers.Meta),
                    },
                },
            });
            desktop.ShutdownRequested += (_, e) =>
            {
                // Cmd+Q bei ungespeicherten Änderungen abfangen und nachfragen
                if (!viewModel.BeendenBestaetigt && viewModel.HatUngespeicherte)
                {
                    e.Cancel = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        if (await viewModel.DarfBeendenAsync())
                            desktop.TryShutdown();
                    });
                    return;
                }

                // Headless-Chromium beenden, sonst überlebt der Prozess das App-Ende
                System.Threading.Tasks.Task.Run(viewModel.Pdf.BeendenAsync)
                    .Wait(System.TimeSpan.FromSeconds(5));
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}