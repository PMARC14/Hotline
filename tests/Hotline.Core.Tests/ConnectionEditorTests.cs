using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ConnectionEditorTests
{
    private static ChatSettings Chat() => new() { Backends = [new BackendProfile { Id = "agy", Type = BackendType.Antigravity, Name = "Gemini (Antigravity)", Agent = "hotline" }] };

    [Fact]
    public void Add_uses_type_defaults_and_unique_ids_and_names()
    {
        var chat = Chat();
        var a = ConnectionEditor.Add(chat, BackendType.Local);
        var b = ConnectionEditor.Add(chat, BackendType.Local);
        Assert.Equal(("local-model", "Local model", "http://127.0.0.1:8080/v1"), (a.Id, a.Name, a.Endpoint));
        Assert.Equal(("local-model-2", "Local model 2"), (b.Id, b.Name));
        Assert.Equal(3, chat.Backends.Count);
    }

    [Fact]
    public void Add_agy_sets_agent_and_avoids_existing_name()
    {
        var p = ConnectionEditor.Add(Chat(), BackendType.Antigravity);
        Assert.Equal("hotline", p.Agent);
        Assert.Equal("Gemini (Antigravity) 2", p.Name);
    }

    [Fact]
    public void Duplicate_copies_settings_after_original()
    {
        var chat = Chat();
        chat.Backends[0].Model = "gemini-3.8-flash-low";
        chat.Backends[0].Tools = ToolMode.Inherit;
        chat.Backends[0].ApproveAllTools = true;
        var copy = ConnectionEditor.Duplicate(chat, "agy");
        Assert.Equal(1, chat.Backends.IndexOf(copy));
        Assert.Equal(("Gemini (Antigravity) (copy)", "gemini-3.8-flash-low", ToolMode.Inherit), (copy.Name, copy.Model, copy.Tools));
        Assert.NotEqual("agy", copy.Id);
        Assert.False(copy.ApproveAllTools);
    }

    [Theory]
    [InlineData(false)] // the last connection can be removed; the default becomes empty
    [InlineData(true)]  // removing the default hands it to a remaining connection
    public void Removing_the_default_reassigns_or_empties_it(bool another)
    {
        var chat = Chat();
        var local = another ? ConnectionEditor.Add(chat, BackendType.Local) : null;
        ConnectionEditor.SetDefault(chat, "agy");
        Assert.True(ConnectionEditor.Remove(chat, "agy"));
        Assert.Equal(local?.Id ?? "", chat.DefaultBackend);
        Assert.Equal(another ? 1 : 0, chat.Backends.Count);
    }

    [Fact]
    public void Set_default_requires_existing_id()
    {
        var chat = Chat();
        var local = ConnectionEditor.Add(chat, BackendType.Local);
        ConnectionEditor.SetDefault(chat, local.Id);
        Assert.Equal(local.Id, chat.DefaultBackend);
        Assert.Throws<KeyNotFoundException>(() => ConnectionEditor.SetDefault(chat, "nope"));
    }

    [Fact]
    public void Move_reorders_the_dropdown_and_records_the_order()
    {
        var chat = Chat();
        var b = ConnectionEditor.Add(chat, BackendType.Gemini);
        var c = ConnectionEditor.Add(chat, BackendType.Anthropic);
        Assert.True(ConnectionEditor.Move(chat, c.Id, -1));
        Assert.Equal(["agy", c.Id, b.Id], chat.Backends.Select(x => x.Id));
        Assert.Equal(["agy", c.Id, b.Id], chat.Order);
        Assert.False(ConnectionEditor.Move(chat, "agy", -1)); // already first
        Assert.False(ConnectionEditor.Move(chat, b.Id, +1));   // already last
    }
}
