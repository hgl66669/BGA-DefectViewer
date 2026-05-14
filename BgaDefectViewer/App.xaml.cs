using System.Windows;
using System.Windows.Threading;

namespace BgaDefectViewer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // Catch UI-thread exceptions (e.g. async void handlers in ViewModels
        // that throw before the main window has rendered). Without this, a
        // single throw silently terminates the process and the user sees
        // "no window appears, exe just exits."
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Catch exceptions from non-UI threads (background Task.Run that
        // bubbles out via async void).
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // Catch unobserved Task exceptions that are GC'd without ever being
        // awaited. Marking them observed keeps the process alive.
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
    }

    private static void OnDispatcherUnhandledException(
        object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError("UI 執行緒發生未處理例外", e.Exception);
        // Mark handled so the app keeps running. The user can reproduce
        // and we have a chance to log/screenshot the error.
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(
        object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ShowError("背景執行緒發生未處理例外", ex);
    }

    private static void ShowError(string title, Exception ex)
    {
        MessageBox.Show(
            $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

