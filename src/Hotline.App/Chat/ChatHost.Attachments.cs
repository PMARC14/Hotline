namespace Hotline.App.Chat;

internal sealed partial class ChatHost
{
    private partial Task PickFilesAsync() { Toast("Attaching files arrives in the next step."); return Task.CompletedTask; }
    private partial Task CaptureAsync(bool window) { Toast("Capture arrives in the next step."); return Task.CompletedTask; }
    private partial Task AddBytesAsync(string name, string? mime, byte[] data) { Toast("Attachments arrive in the next step."); return Task.CompletedTask; }
}
