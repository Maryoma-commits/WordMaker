using Microsoft.UI.Dispatching;

namespace WordMaker;

/// <summary>
/// Custom entry point. Required for single-file publish: the Windows App SDK
/// must be told where its runtime files were extracted BEFORE it initializes.
/// </summary>
public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
