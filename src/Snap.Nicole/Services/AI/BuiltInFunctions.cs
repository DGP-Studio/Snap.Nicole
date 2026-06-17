using System.ComponentModel;
using Snap.Nicole.Core.Diagnostics;

namespace Snap.Nicole.Services.AI;

internal static class BuiltInFunctions
{
    [Description("Get the current local time.")]
    public static string GetCurrentTime()
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIToolCurrentTime, "Get current local time");

        try
        {
            return DateTimeOffset.Now.ToString("O");
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.AIToolCurrentTime);
            throw;
        }
    }
}
