namespace Hotline.Core.Windowing;

public enum ClipboardCopyAction { Wait, ReadAndRestore, Restore, Stop }

public static class ClipboardCopyPolicy
{
    public const int LateCopyWatchMilliseconds = 2000;

    public static bool MaySend(long now, long deadline, bool cancelled, bool foregroundMatches,
        uint savedSequence, uint currentSequence) =>
        now < deadline && !cancelled && foregroundMatches && savedSequence == currentSequence;

    public static ClipboardCopyAction Observe(long now, long deadline, uint before, uint current,
        uint targetProcess, uint ownerProcess)
    {
        if (now >= deadline + LateCopyWatchMilliseconds) return ClipboardCopyAction.Stop;
        if (before == current) return ClipboardCopyAction.Wait;
        if (targetProcess == 0 || ownerProcess != targetProcess) return ClipboardCopyAction.Stop;
        return now < deadline ? ClipboardCopyAction.ReadAndRestore : ClipboardCopyAction.Restore;
    }

    public static bool MayRestore(uint expectedSequence, uint currentSequence, uint targetProcess, uint ownerProcess) =>
        expectedSequence == currentSequence && targetProcess != 0 && ownerProcess == targetProcess;

    public static bool MayRead(long now, long deadline, uint expectedSequence, uint currentSequence,
        uint targetProcess, uint ownerProcess) =>
        now < deadline && MayRestore(expectedSequence, currentSequence, targetProcess, ownerProcess);

    public static bool CanSnapshotFormat(uint format, string? registeredName) =>
        format is 1 or 7 or 8 or 13 or 15 or 16 or 17 ||
        (format >= 0xC000 && registeredName is "HTML Format" or "Rich Text Format" or
            "Rich Text Format Without Objects" or "ExcludeClipboardContentFromMonitorProcessing" or
            "CanIncludeInClipboardHistory" or "CanUploadToCloudClipboard");
}
