namespace Hotline.Core.Windowing;

public record EmbeddedBox(int Indent, bool ContentSized);

public static class EmbeddedWidths
{
    public static double ForViewport(double availableWidth, int indent) =>
        Math.Max(0, availableWidth - indent * 18 - 8);
}
