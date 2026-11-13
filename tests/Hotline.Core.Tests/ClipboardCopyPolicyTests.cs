using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class ClipboardCopyPolicyTests
{
    [Theory]
    [InlineData(449, 8, 8, 42, 42, true)]
    [InlineData(450, 8, 8, 42, 42, false)]
    [InlineData(650, 8, 8, 42, 42, false)]
    [InlineData(449, 8, 9, 42, 42, false)]
    [InlineData(449, 8, 8, 42, 99, false)]
    [InlineData(449, 8, 8, 0, 0, false)]
    public void Read_rechecks_deadline_sequence_and_owner_after_acquiring_lock(long now, uint expected, uint current,
        uint target, uint owner, bool read)
        => Assert.Equal(read, ClipboardCopyPolicy.MayRead(now, 450, expected, current, target, owner));

    [Theory]
    [InlineData(13, null, true)]
    [InlineData(15, null, true)]
    [InlineData(8, null, true)]
    [InlineData(17, null, true)]
    [InlineData(2, null, false)]
    [InlineData(14, null, false)]
    [InlineData(0x200, null, false)]
    [InlineData(0xC000, "HTML Format", true)]
    [InlineData(0xC001, "Rich Text Format", true)]
    [InlineData(0xC010, "ExcludeClipboardContentFromMonitorProcessing", true)]
    [InlineData(0xC011, "CanIncludeInClipboardHistory", true)]
    [InlineData(0xC012, "CanUploadToCloudClipboard", true)]
    [InlineData(0xC002, "FileContents", false)]
    [InlineData(0xC003, "FileGroupDescriptorW", false)]
    [InlineData(0xC004, "Shell IDList Array", false)]
    [InlineData(0xC005, "Unknown OLE format", false)]
    [InlineData(0xC006, null, false)]
    public void Unsupported_clipboards_skip_copy_instead_of_losing_formats(uint format, string? name, bool expected)
        => Assert.Equal(expected, ClipboardCopyPolicy.CanSnapshotFormat(format, name));

    [Theory]
    [InlineData(449, false, true, 7, 7, true)]
    [InlineData(450, false, true, 7, 7, false)]
    [InlineData(451, false, true, 7, 7, false)]
    [InlineData(449, true, true, 7, 7, false)]
    [InlineData(449, false, false, 7, 7, false)]
    [InlineData(449, false, true, 7, 8, false)]
    public void Send_requires_deadline_focus_no_cancellation_and_unchanged_snapshot(long now, bool cancelled,
        bool focused, uint saved, uint current, bool expected)
        => Assert.Equal(expected, ClipboardCopyPolicy.MaySend(now, 450, cancelled, focused, saved, current));

    [Theory]
    [InlineData(449, 7, 8, 42, 42, ClipboardCopyAction.ReadAndRestore)]
    [InlineData(450, 7, 8, 42, 42, ClipboardCopyAction.Restore)]
    [InlineData(1200, 7, 8, 42, 42, ClipboardCopyAction.Restore)]
    [InlineData(2449, 7, 8, 42, 42, ClipboardCopyAction.Restore)]
    [InlineData(2450, 7, 8, 42, 42, ClipboardCopyAction.Stop)]
    [InlineData(1200, 7, 7, 42, 42, ClipboardCopyAction.Wait)]
    [InlineData(2450, 7, 7, 42, 42, ClipboardCopyAction.Stop)]
    [InlineData(300, 7, 8, 42, 99, ClipboardCopyAction.Stop)]
    [InlineData(1200, 7, 8, 42, 99, ClipboardCopyAction.Stop)]
    [InlineData(1200, 7, 8, 42, 0, ClipboardCopyAction.Stop)]
    [InlineData(300, 7, 8, 0, 0, ClipboardCopyAction.Stop)]
    [InlineData(300, uint.MaxValue, 0, 42, 42, ClipboardCopyAction.ReadAndRestore)]
    public void Watch_restores_late_target_copies_but_preserves_other_owners(long now, uint before, uint current,
        uint target, uint owner, ClipboardCopyAction expected)
        => Assert.Equal(expected, ClipboardCopyPolicy.Observe(now, 450, before, current, target, owner));

    [Theory]
    [InlineData(8, 8, 42, 42, true)]
    [InlineData(8, 9, 42, 42, false)]
    [InlineData(8, 8, 42, 99, false)]
    [InlineData(8, 8, 42, 0, false)]
    [InlineData(8, 8, 0, 0, false)]
    public void Restore_rechecks_sequence_and_target_owner_under_clipboard_lock(uint expected, uint current,
        uint target, uint owner, bool restore)
        => Assert.Equal(restore, ClipboardCopyPolicy.MayRestore(expected, current, target, owner));
}
