using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using PeakHoursWidget.WidgetHelper;

namespace PeakHoursWidget;

public static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [MTAThread]
    private static void Main(string[] args)
    {
        Diag.Log($"Main started, args=[{string.Join(", ", args)}]");

        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            Diag.Log("Not launched by Widgets host; exiting.");
            return;
        }

        // The Widgets host starts this process via COM. Detach from any console
        // Windows may have allocated for us so no black window flashes on screen.
        FreeConsole();

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            using var manager = RegistrationManager<WidgetProvider>.RegisterProvider();
            Diag.Log("COM provider registered.");

            var existingWidgets = WidgetManager.GetDefault().GetWidgetIds();
            Diag.Log($"Existing widgets: {(existingWidgets == null ? 0 : existingWidgets.Length)}");

            // No Console.ReadLine(): stay alive until the COM class is revoked.
            using var disposedEvent = manager.GetDisposedEvent();
            disposedEvent.WaitOne();
            Diag.Log("Provider disposed.");
        }
        catch (Exception ex)
        {
            Diag.Log("FATAL: " + ex);
        }
    }
}
