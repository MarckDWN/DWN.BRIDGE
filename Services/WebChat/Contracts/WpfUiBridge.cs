using System.Windows;

namespace AIBridge.Services.WebChat;

/// <summary>Implementazione WPF di <see cref="IUiBridge"/> basata su Application.Current.</summary>
public sealed class WpfUiBridge : IUiBridge
{
    public void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.Invoke(action);
    }

    public void BringMainWindowToFront()
    {
        // Stessa sequenza usata oggi in GeminiBrowserService.InitializeAsync (InvokeAsync, non bloccante).
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null) return;

            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;

            mainWindow.Activate();
            mainWindow.Topmost = true;
            mainWindow.Topmost = false;
            mainWindow.Focus();
        });
    }
}
