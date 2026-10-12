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
        var features = new List<PcFeature> { new("Windows", true, L.F(Strings.Pc_Build, build)) };

        var ocrLanguages = SafeCount(() => OcrEngine.AvailableRecognizerLanguages.Count);
        features.Add(ocrLanguages > 0
            ? new PcFeature(Strings.Set_Ocr, true, L.F(Strings.Pc_OcrLanguages, ocrLanguages))
            : new PcFeature(Strings.Set_Ocr, false, Strings.Pc_OcrMissing));

        var speech = await WindowsSpeech.CheckAsync();
        features.Add(speech == "Success"
            ? new PcFeature(Strings.Set_Voice, true, Strings.Pc_VoiceOk)
            : new PcFeature(Strings.Set_Voice, false, L.F(Strings.Pc_VoiceMissing, speech)));

        features.Add(odrPath is not null
            ? new PcFeature(Strings.Pc_Agents, true, Strings.Pc_AgentsOk)
            : new PcFeature(Strings.Pc_Agents, false, PlatformSupport.AgentConnectors(build) ? Strings.Pc_AgentsNotOn : Strings.Pc_AgentsBuild));

        features.Add(PlatformSupport.AppActions(build)
            ? new PcFeature(Strings.Pc_Actions, true, Strings.Pc_ActionsOk)
            : new PcFeature(Strings.Pc_Actions, false, Strings.Pc_ActionsBuild));
        return features;
    }

    private static int SafeCount(Func<int> count)
    {
        try { return count(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { return 0; }
    }
}
