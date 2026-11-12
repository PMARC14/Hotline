using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class NewerVersionGuardTests : TempDirTest
{
    [Fact]
    public void Settings_with_newer_version_is_not_overwritten_and_reports_write_problem()
    {
        var filePath = Path.Combine(_dir, SettingsStore.FileName);
        Directory.CreateDirectory(_dir);
        var json = """
            {
              "schemaVersion": 99,
              "unrecognizedFutureKey": "preserve-this-payload",
              "window": {
                "fontSize": 16
              }
            }
            """;
        File.WriteAllText(filePath, json);
        var beforeBytes = File.ReadAllBytes(filePath);

        var store = new SettingsStore(_dir);
        var settings = store.Load();

        Assert.Equal(99, store.NewerVersionFile);
        Assert.Equal(99, settings.SchemaVersion);

        // Modify an in-memory property and attempt save
        settings.Window.FontSize = 20;
        store.Save(settings);

        var afterBytes = File.ReadAllBytes(filePath);
        Assert.Equal(beforeBytes, afterBytes);

        var expectedProblem = L.F(Strings.Settings_NewerVersion, 99, HotlineSettings.CurrentSchemaVersion);
        Assert.Contains(store.WriteProblems, p => p == expectedProblem);
    }

    [Fact]
    public void Settings_with_newer_version_still_allows_separate_connection_files_to_save()
    {
        var filePath = Path.Combine(_dir, SettingsStore.FileName);
        Directory.CreateDirectory(_dir);
        var json = """
            {
              "schemaVersion": 99,
              "newField": 42
            }
            """;
        File.WriteAllText(filePath, json);
        var beforeBytes = File.ReadAllBytes(filePath);

        var store = new SettingsStore(_dir);
        var settings = store.Load();

        // Add a connection
        settings.Chat.Backends.Add(new BackendProfile { Id = "test-backend", Name = "Test Connection", Type = BackendType.OpenAiCompatible });
        store.Save(settings);

        // settings.json is still protected
        Assert.Equal(beforeBytes, File.ReadAllBytes(filePath));
        var expectedProblem = L.F(Strings.Settings_NewerVersion, 99, HotlineSettings.CurrentSchemaVersion);
        Assert.Contains(store.WriteProblems, p => p == expectedProblem);

        // Connection file was saved
        var connectionPath = store.ConnectionPath("test-backend");
        Assert.True(File.Exists(connectionPath));
    }

    [Fact]
    public void Settings_with_current_version_saves_normally()
    {
        var filePath = Path.Combine(_dir, SettingsStore.FileName);
        Directory.CreateDirectory(_dir);
        var json = $$"""
            {
              "schemaVersion": {{HotlineSettings.CurrentSchemaVersion}},
              "window": {
                "fontSize": 14
              }
            }
            """;
        File.WriteAllText(filePath, json);

        var store = new SettingsStore(_dir);
        var settings = store.Load();

        Assert.Null(store.NewerVersionFile);
        Assert.Equal(HotlineSettings.CurrentSchemaVersion, settings.SchemaVersion);

        settings.Window.FontSize = 18;
        store.Save(settings);

        Assert.Empty(store.WriteProblems);
        var textAfter = File.ReadAllText(filePath);
        Assert.Contains("18", textAfter);
    }

    [Fact]
    public void Toolbar_with_newer_version_is_not_overwritten_and_returns_problem()
    {
        var path = Path.Combine(_dir, "toolbar.json");
        Directory.CreateDirectory(_dir);
        var json = """
            {
              "version": 3,
              "futureToolbarOption": { "enabled": true },
              "items": { "left": ["pin"], "center": [], "right": [] }
            }
            """;
        File.WriteAllText(path, json);
        var beforeBytes = File.ReadAllBytes(path);

        var load = ToolbarFile.Load(path);
        Assert.NotNull(load.Model);

        var problem = ToolbarFile.Save(path, ToolbarModel.Defaults);

        Assert.NotNull(problem);
        var expectedProblem = L.F(Strings.Toolbar_NewerVersion, 3);
        Assert.Equal(expectedProblem, problem);

        var afterBytes = File.ReadAllBytes(path);
        Assert.Equal(beforeBytes, afterBytes);
    }

    [Fact]
    public void Toolbar_with_current_version_saves_normally()
    {
        var path = Path.Combine(_dir, "toolbar.json");
        Directory.CreateDirectory(_dir);
        var json = """
            {
              "version": 2,
              "items": { "left": ["pin"], "center": [], "right": [] }
            }
            """;
        File.WriteAllText(path, json);

        var problem = ToolbarFile.Save(path, ToolbarModel.Defaults);

        Assert.Null(problem);
        var textAfter = File.ReadAllText(path);
        Assert.Contains("\"version\": 2", textAfter);
    }
}
