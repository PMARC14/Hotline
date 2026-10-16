namespace Hotline.Core.Chat;

/// <summary>
/// Whether the transcript follows the newest text. Only the reader stops it: scrolling (wheel, scrollbar, touch, keys)
/// away from the bottom. Hotline's own scrolls never do — one that ends short of the bottom (the answer grew while it
/// was moving) just scrolls again. Reaching the bottom, or sending a message, follows again.
/// </summary>
public sealed class ScrollFollow
{
    public bool Following { get; private set; } = true;

    /// <summary>The reader is scrolling: the view is theirs until it settles back at the bottom.</summary>
    public void UserInput() => Following = false;

    /// <summary>Follow the conversation again (a message was sent, "jump to latest").</summary>
    public void Follow() => Following = true;

    /// <summary>A scroll ended. Returns true when the view should scroll to the bottom again.</summary>
    public bool Settled(bool atBottom)
    {
        if (atBottom) Following = true;
        return Following && !atBottom;
    }
}
