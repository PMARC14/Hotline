using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ConnectionEditorTests
{
    private static ChatSettings Chat() => new(); // contains the default agy connection

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
        var copy = ConnectionEditor.Duplicate(chat, "agy");
        Assert.Equal(1, chat.Backends.IndexOf(copy));
        Assert.Equal(("Gemini (Antigravity) (copy)", "gemini-3.8-flash-low", ToolMode.Inherit), (copy.Name, copy.Model, copy.Tools));
        Assert.NotEqual("agy", copy.Id);
    }

    [Fact]
    public void Cannot_remove_last()
    {
        var chat = Chat();
        Assert.False(ConnectionEditor.Remove(chat, "agy"));
        Assert.Single(chat.Backends);
    }

    [Fact]
    public void Remove_default_reassigns()
    {
        var chat = Chat();
        var local = ConnectionEditor.Add(chat, BackendType.Local);
        Assert.True(ConnectionEditor.Remove(chat, "agy"));
        Assert.Equal(local.Id, chat.DefaultBackend);
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
}
