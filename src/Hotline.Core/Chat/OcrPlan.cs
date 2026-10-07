using System.Text;
using Hotline.Core.Settings;

namespace Hotline.Core.Chat;

/// <summary>Screenshot text (OCR, done by the app with Windows' built-in engine) for models that can't read images.</summary>
public static class OcrPlan
{
    /// <summary>
    /// Auto: read the text only when the model can't take images, and send it instead of them. Always: also for image
    /// models, alongside the image. Off: never (an image for a text-only model is refused as before).
    /// </summary>
    public static (bool Run, bool KeepImages) Decide(OcrMode mode, bool backendTakesImages) => mode switch
    {
        OcrMode.Off => (false, true),
        OcrMode.Always => (true, backendTakesImages),
        _ => (!backendTakesImages, backendTakesImages),
    };

    public static Attachment TextAttachment(string imageName, string? text)
    {
        var body = string.IsNullOrWhiteSpace(text) ? "(no text found)" : text.Trim();
        return new Attachment(Ids.New(), $"Text in {imageName}", AttachmentKind.Text, "text/plain",
            Encoding.UTF8.GetBytes("Text in screenshot:\n" + body));
    }
}
