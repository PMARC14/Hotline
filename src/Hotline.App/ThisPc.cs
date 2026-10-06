using Hotline.App.Chat;
using Hotline.Core.Platform;
using Microsoft.Win32;
using Windows.Media.Ocr;

namespace Hotline.App;

/// <summary>One line of the "This PC" report: a feature, whether it works here, and why not.</summary>
internal sealed record PcFeature(string Name, bool Available, string Detail);

/// <summary>
/// What this PC supports, checked live (Windows build, OCR languages, speech, the agent registry, App Actions), for
/// Settings › About and the self-test log. Each feature also checks for itself before it's used; this only explains.
/// </summary>
internal static class ThisPc
{
    public static WindowsBuild Build
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                var build = int.TryParse(key?.GetValue("CurrentBuild") as string, out var b) ? b : Environment.OSVersion.Version.Build;
                return new WindowsBuild(build, key?.GetValue("UBR") is int ubr ? ubr : 0);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return new WindowsBuild(Environment.OSVersion.Version.Build, 0);
            }
        }
    }

    public static async Task<IReadOnlyList<PcFeature>> CheckAsync(string? odrPath)
    {
        var build = Build;
        var features = new List<PcFeature> { new("Windows", true, $"build {build}") };

        var ocrLanguages = SafeCount(() => OcrEngine.AvailableRecognizerLanguages.Count);
        features.Add(ocrLanguages > 0
            ? new PcFeature("Screenshot text (OCR)", true, $"{ocrLanguages} OCR language(s)")
            : new PcFeature("Screenshot text (OCR)", false, "No OCR language: Settings › Time & language › Language & region › add a language with Optical character recognition"));

        var speech = await WindowsSpeech.CheckAsync();
        features.Add(speech == "Success"
            ? new PcFeature("Voice input", true, "Windows dictation is available")
            : new PcFeature("Voice input", false, $"{speech} — Settings › Privacy & security › Speech › Online speech recognition, and a microphone"));

        features.Add(odrPath is not null
            ? new PcFeature("Windows agent connectors", true, "found odr.exe; its connectors appear as windows-… tool servers")
            : new PcFeature("Windows agent connectors", false, PlatformSupport.AgentConnectors(build)
                ? "odr.exe not found (not turned on for this PC yet)" : "needs Windows build 26220.7262 or later (e.g. 26H2)"));

        features.Add(PlatformSupport.AppActions(build)
            ? new PcFeature("App Actions (Click to Do)", true, "registered; Click to Do itself needs a Copilot+ PC")
            : new PcFeature("App Actions (Click to Do)", false, "needs Windows 11 24H2 (build 26100) or later"));
        return features;
    }

    private static int SafeCount(Func<int> count)
    {
        try { return count(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { return 0; }
    }
}
