using System;
using System.IO;

namespace PeakHoursWidget;

internal static class Diag
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "PeakHoursWidget.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Never let diagnostics break the provider.
        }
    }
}
