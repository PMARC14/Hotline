using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Chat;
using Hotline.Core.Settings;
using Hotline.Core.Tools;
using Hotline.Core.Windowing;
using Json.Schema;

namespace Hotline.Core.Tests.Fixtures;

public sealed class FixtureTests
{
    private static readonly JsonDocumentOptions LenientJson = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly EvaluationOptions StrictSchema = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    private static string OutputPath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, .. parts]);

    private static JsonSchema LoadSchema(string family, int version) =>
        JsonSchema.FromText(
            File.ReadAllText(OutputPath("schemas", $"{family}.v{version}.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    private static JsonElement ParseLenient(string json)
    {
        using var doc = JsonDocument.Parse(json, LenientJson);
        return doc.RootElement.Clone();
    }

    private static string ReadDefaultResource(string family)
    {
        var name = $"Hotline.Core.Defaults.{family}.json";
        using var stream = typeof(HotlineSettings).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AssertValid(JsonSchema schema, JsonElement instance, string label)
    {
        var result = schema.Evaluate(instance, StrictSchema);
        if (result.IsValid) return;

        var errors = (result.Details ?? [])
            .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} = {e.Value}"))
            .ToList();
        Assert.Fail($"Schema validation failed for '{label}':\n{string.Join('\n', errors)}");
    }

    [Theory]
    [InlineData("settings", 1, "default")]
    [InlineData("settings", 1, "sparse")]
    [InlineData("settings", 1, "unknown-keys")]
    [InlineData("connection", 1, "default")]
    [InlineData("connection", 1, "sparse")]
    [InlineData("connection", 1, "unknown-keys")]
    [InlineData("toolbar", 3, "default")]
    [InlineData("toolbar", 3, "sparse")]
    [InlineData("toolbar", 3, "unknown-keys")]
    [InlineData("mcp", 1, "default")]
    [InlineData("mcp", 1, "sparse")]
    [InlineData("mcp", 1, "unknown-keys")]
    [InlineData("state", 1, "default")]
    [InlineData("state", 1, "sparse")]
    [InlineData("state", 1, "unknown-keys")]
    public void Valid_fixture_validates_against_schema(string family, int version, string variant)
    {
        var schema = LoadSchema(family, version);
        var instance = ParseLenient(File.ReadAllText(OutputPath("fixtures", family, $"v{version}", $"{variant}.json")));
        AssertValid(schema, instance, $"{family}/v{version}/{variant}.json");
    }

    [Theory]
    [InlineData("settings", 1)]
    [InlineData("connection", 1)]
    [InlineData("toolbar", 3)]
    [InlineData("mcp", 1)]
    [InlineData("state", 1)]
    public void Newer_version_fixture_parses_with_higher_version_and_fails_current_schema(string family, int version)
    {
        var schema = LoadSchema(family, version);
        var raw = File.ReadAllText(OutputPath("fixtures", family, $"v{version}", "newer-version.json"));
        var instance = ParseLenient(raw);
        Assert.True(instance.GetProperty("version").GetInt32() > version);
        var result = schema.Evaluate(instance, StrictSchema);
        Assert.False(result.IsValid);

        var downgraded = JsonNode.Parse(raw, documentOptions: LenientJson)!.AsObject();
        downgraded["version"] = version;
        AssertValid(schema, ParseLenient(downgraded.ToJsonString()), $"{family}/v{version}/newer-version.json (version={version})");
    }

    [Theory]
    [InlineData("settings", 1)]
    [InlineData("connection", 1)]
    [InlineData("toolbar", 3)]
    [InlineData("mcp", 1)]
    [InlineData("state", 1)]
    public void Broken_fixture_does_not_parse(string family, int version)
    {
        var text = File.ReadAllText(OutputPath("fixtures", family, $"v{version}", "broken.json"));
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(text, LenientJson));
    }

    [Theory]
    [InlineData("settings", 1, false)]
    [InlineData("toolbar", 3, false)]
    [InlineData("mcp", 1, true)]
    public void Embedded_defaults_file_validates_against_schema(string family, int version, bool expectsComments)
    {
        var text = ReadDefaultResource(family);
        if (expectsComments)
            Assert.Contains("//", text);
        else
            using (JsonDocument.Parse(text)) { } // must be strict JSON without comments

        var schema = LoadSchema(family, version);
        var instance = ParseLenient(text);
        AssertValid(schema, instance, $"Defaults/{family}.json");
    }

    [Theory]
    [InlineData("settings", 1)]
    [InlineData("toolbar", 3)]
    [InlineData("mcp", 1)]
    public void Schema_defaults_equal_defaults_files(string family, int version)
    {
        var schemaNode = JsonNode.Parse(File.ReadAllText(OutputPath("schemas", $"{family}.v{version}.schema.json")))!.AsObject();
        var defaultsNode = JsonNode.Parse(ReadDefaultResource(family), documentOptions: LenientJson)!.AsObject();

        AssertDefaultsMatch(schemaNode, defaultsNode, family);
    }

    private static void AssertDefaultsMatch(JsonObject schemaObj, JsonObject defaultsObj, string path)
    {
        if (schemaObj["properties"] is not JsonObject props)
            return;

        // Every key in the defaults file must match the schema's default at that path.
        foreach (var (key, value) in defaultsObj)
        {
            Assert.True(props.ContainsKey(key), $"Defaults key '{path}.{key}' is missing from schema properties.");
            var propSchema = props[key]!.AsObject();
            if (propSchema["properties"] is JsonObject && value is JsonObject nestedDefaults)
            {
                if (propSchema.TryGetPropertyValue("default", out var objDefault))
                    Assert.True(JsonNode.DeepEquals(objDefault, value),
                        $"Default mismatch at '{path}.{key}': schema has {objDefault?.ToJsonString() ?? "null"}, file has {value.ToJsonString()}.");
                AssertDefaultsMatch(propSchema, nestedDefaults, $"{path}.{key}");
                continue;
            }

            if (propSchema.TryGetPropertyValue("default", out var schemaDefault))
            {
                Assert.True(JsonNode.DeepEquals(schemaDefault, value),
                    $"Default mismatch at '{path}.{key}': schema has {schemaDefault?.ToJsonString() ?? "null"}, file has {value?.ToJsonString() ?? "null"}.");
            }
            else if (propSchema["$ref"]?.GetValue<string>() is { } refPath && refPath.StartsWith("#/$defs/", StringComparison.Ordinal))
            {
                Assert.Fail($"Property '{path}.{key}' uses $ref '{refPath}' without a top-level default.");
            }
        }

        // Every schema property with a non-null default (and every null default except omitted optional style.outlineColor) must appear in the defaults file.
        foreach (var (key, rawProp) in props)
        {
            var propSchema = rawProp!.AsObject();
            if (propSchema["properties"] is JsonObject)
            {
                Assert.True(defaultsObj.ContainsKey(key) && defaultsObj[key] is JsonObject,
                    $"Section '{path}.{key}' is missing from defaults file.");
                AssertDefaultsMatch(propSchema, defaultsObj[key]!.AsObject(), $"{path}.{key}");
                continue;
            }

            if (!propSchema.TryGetPropertyValue("default", out var schemaDefault))
                continue;

            if (defaultsObj.TryGetPropertyValue(key, out var actual))
            {
                Assert.True(JsonNode.DeepEquals(schemaDefault, actual),
                    $"Default mismatch at '{path}.{key}': schema has {schemaDefault?.ToJsonString() ?? "null"}, file has {actual?.ToJsonString() ?? "null"}.");
            }
            else
            {
                Assert.True(schemaDefault is null,
                    $"Schema property '{path}.{key}' has non-null default {schemaDefault?.ToJsonString()} but is missing from defaults file.");
            }
        }
    }

    [Fact]
    public void All_schemas_allow_unknown_keys_everywhere()
    {
        foreach (var file in Directory.GetFiles(OutputPath("schemas"), "*.schema.json"))
        {
            var root = JsonNode.Parse(File.ReadAllText(file))!;
            AssertNoClosedObjects(root, Path.GetFileName(file));
        }
    }

    private static void AssertNoClosedObjects(JsonNode node, string path)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("additionalProperties", out var ap))
                Assert.False(ap is JsonValue v && v.TryGetValue<bool>(out var b) && !b,
                    $"Schema '{path}' forbids unknown keys with additionalProperties: false.");
            foreach (var (k, v) in obj)
                if (v is not null) AssertNoClosedObjects(v, $"{path}.{k}");
        }
        else if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
                if (arr[i] is not null) AssertNoClosedObjects(arr[i]!, $"{path}[{i}]");
        }
    }

    [Fact]
    public void Pre_freeze_fixture_folder_matches_0_6_3_formats()
    {
        var dir = OutputPath("fixtures", "pre-freeze");
        Assert.True(File.Exists(Path.Combine(dir, ".first-run-shown")));
        Assert.True(File.Exists(Path.Combine(dir, ".support-note-shown")));

        var settingsJson = File.ReadAllText(Path.Combine(dir, "settings.json"));
        var settingsDoc = ParseLenient(settingsJson);
        Assert.Equal(15, settingsDoc.GetProperty("schemaVersion").GetInt32());
        var windowEl = settingsDoc.GetProperty("window");
        Assert.True(windowEl.TryGetProperty("widthPercent", out _));
        Assert.True(windowEl.TryGetProperty("height", out _));
        var chatEl = settingsDoc.GetProperty("chat");
        Assert.Equal("claude-work", chatEl.GetProperty("defaultBackend").GetString());
        Assert.Equal("Ctrl+.", chatEl.GetProperty("stopShortcut").GetString());
        Assert.Equal("grow", chatEl.GetProperty("growMode").GetString());

        var problems = new ReadProblems();
        var (parsedSettings, _, failure) = SettingsStore.ParseSettings(settingsJson, problems);
        Assert.Null(failure);
        Assert.NotNull(parsedSettings);
        Assert.Empty(problems.All);
        Assert.Equal("claude-work", parsedSettings.Chat.DefaultBackend);

        var toolbarJson = File.ReadAllText(Path.Combine(dir, "toolbar.json"));
        var toolbarDoc = ParseLenient(toolbarJson);
        Assert.Equal(2, toolbarDoc.GetProperty("version").GetInt32());
        Assert.True(toolbarDoc.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Object);
        var toolbar = ToolbarModel.Resolve(toolbarJson, parsedSettings.Chat);
        Assert.Empty(toolbar.Warnings);
        Assert.Single(toolbar.Buttons);
        Assert.Equal(toolbar.ToJson().ReplaceLineEndings("\n"), toolbarJson.ReplaceLineEndings("\n"));

        var mineJson = File.ReadAllText(Path.Combine(dir, "connections", "mine.json"));
        var mineDoc = ParseLenient(mineJson);
        var mineId = mineDoc.GetProperty("id").GetString();
        Assert.Equal("claude-work", mineId);
        Assert.NotEqual("mine", mineId);
        Assert.Equal(JsonValueKind.String, mineDoc.GetProperty("extraArgs").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(mineDoc.GetProperty("extraArgs").GetString()));
        Assert.True(File.Exists(Path.Combine(dir, "connections", "local.json")));

        var mcpJson = File.ReadAllText(Path.Combine(dir, "mcp.json"));
        var mcpDoc = ParseLenient(mcpJson);
        Assert.True(mcpDoc.TryGetProperty("approvals", out var approvalsEl) && approvalsEl.ValueKind == JsonValueKind.Object);
        Assert.Equal("allow", approvalsEl.GetProperty("files/read_file").GetString());
        Assert.Equal("ask", approvalsEl.GetProperty("files/write_file").GetString());
        var mcp = McpConfig.Parse(mcpJson);
        Assert.Empty(mcp.Warnings);
        Assert.Single(mcp.Servers);
        Assert.Equal(2, mcp.Approvals.Count);
    }

    [Theory]
    [InlineData("connection", 1, """{"version":1}""")]
    [InlineData("connection", 1, """{"version":1,"effort":" HIGH "}""")]
    [InlineData("connection", 1, """{"version":1,"type":"claudeCode","effort":"xhigh"}""")]
    [InlineData("settings", 1, """{"version":1,"window":{"accent":"Windows"}}""")]
    [InlineData("settings", 1, """{"version":1,"window":{"accent":" #6E56CF "}}""")]
    public void Schema_accepts_valid_values(string family, int version, string json)
    {
        var schema = LoadSchema(family, version);
        AssertValid(schema, ParseLenient(json), $"{family}.v{version} valid case");
    }

    [Theory]
    [InlineData("résumé-1_2")]
    [InlineData("要約_1")]
    [InlineData("cafe\u0301")]
    public void QuickActions_accepts_unicode_action_names_matched_by_schema(string actionName)
    {
        Assert.True(QuickActions.IsName(actionName));
        var schema = LoadSchema("toolbar", 3);
        var json = JsonSerializer.Serialize(new
        {
            version = 3,
            buttons = new Dictionary<string, object>
            {
                ["custom"] = new { label = "Custom", quickAction = actionName },
            },
        });
        AssertValid(schema, ParseLenient(json), $"quickAction:{actionName}");
    }

    [Fact]
    public void Toolbar_defaults_file_matches_code_definitions()
    {
        var defaults = JsonNode.Parse(ReadDefaultResource("toolbar"), documentOptions: LenientJson)!.AsObject();
        var shortcuts = defaults["shortcuts"]!.AsObject();
        Assert.Equal("Esc", shortcuts["stop"]!.GetValue<string>());

        var fileShortcuts = shortcuts
            .Where(p => p.Key != "stop")
            .ToDictionary(p => p.Key, p => p.Value!.GetValue<string>());
        Assert.Equal(ChatShortcuts.Defaults.Count, fileShortcuts.Count);
        foreach (var (id, combo) in ChatShortcuts.Defaults)
        {
            Assert.True(fileShortcuts.TryGetValue(id, out var actual), $"Defaults/toolbar.json shortcuts missing '{id}'.");
            Assert.Equal(combo, actual);
        }

        foreach (var rowKey in new[] { "top", "bottom" })
        {
            var row = defaults[rowKey]!.AsObject();
            foreach (var groupKey in new[] { "left", "center", "right" })
            {
                foreach (var entry in row[groupKey]!.AsArray())
                {
                    var id = entry!.GetValue<string>();
                    Assert.Contains(id, ToolbarModel.BuiltIn);
                }
            }
        }
    }

    [Theory]
    [InlineData("settings", 1, """{"version":1,"window":{"width":199}}""")]
    [InlineData("settings", 1, """{"version":1,"window":{"width":4001}}""")]
    [InlineData("settings", 1, """{"version":1,"window":{"width":"19%"}}""")]
    [InlineData("settings", 1, """{"version":1,"window":{"width":"91%"}}""")]
    [InlineData("settings", 1, """{"version":1,"window":{"accent":"red"}}""")]
    [InlineData("settings", 1, """{"version":1,"tools":{"approvals":{"a/b":"maybe"}}}""")]
    [InlineData("connection", 1, """{"version":1,"type":"unknown"}""")]
    [InlineData("connection", 1, """{"version":1,"type":"codex","extraArgs":"--search"}""")]
    [InlineData("connection", 1, """{"version":1,"type":"local","temperature":2.5}""")]
    [InlineData("connection", 1, """{"version":1,"type":"anthropic","maxTokens":0}""")]
    [InlineData("toolbar", 3, """{"version":3,"style":{"width":"normal"}}""")]
    [InlineData("toolbar", 3, """{"version":3,"style":{"width":5}}""")]
    [InlineData("toolbar", 3, """{"version":3,"overrides":{"effort":{"span":9}}}""")]
    [InlineData("toolbar", 3, """{"version":3,"buttons":{"stop":{"label":"Stop","text":"x"}}}""")]
    [InlineData("toolbar", 3, """{"version":3,"buttons":{"spacer":{"label":"Space","text":"x"}}}""")]
    [InlineData("toolbar", 3, """{"version":3,"buttons":{"mine":{"label":"Both","quickAction":"fix","text":"x"}}}""")]
    [InlineData("toolbar", 3, """{"version":3,"buttons":{"mine":{"label":"Neither"}}}""")]
    [InlineData("mcp", 1, """{"mcpServers":{"files":{}}}""")]
    [InlineData("mcp", 1, """{"mcpServers":{"files":{"command":"npx","env":{"PORT":8080}}}}""")]
    [InlineData("state", 1, """{"version":1,"lastWritten":{"settings.json":"not-sha256"}}""")]
    [InlineData("state", 1, """{"version":1,"shown":{"firstRun":"yesterday"}}""")]
    public void Schema_rejects_invalid_values(string family, int version, string json)
    {
        var schema = LoadSchema(family, version);
        var result = schema.Evaluate(ParseLenient(json), StrictSchema);
        Assert.False(result.IsValid, $"Expected '{family}.v{version}' schema to reject: {json}");
    }

    [Fact]
    public void All_schema_properties_have_descriptions()
    {
        foreach (var file in Directory.GetFiles(OutputPath("schemas"), "*.schema.json"))
        {
            var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            AssertDescriptions(root, Path.GetFileName(file));
        }
    }

    private static void AssertDescriptions(JsonObject schemaObj, string path)
    {
        foreach (var key in new[] { "properties", "$defs" })
        {
            if (schemaObj[key] is not JsonObject map) continue;
            foreach (var (name, raw) in map)
            {
                if (raw is not JsonObject child) continue;
                if (key == "properties")
                    Assert.True(child["description"] is JsonValue v && v.TryGetValue<string>(out var desc) && desc.Length > 0,
                        $"Schema property '{path}.{name}' is missing a description.");
                AssertDescriptions(child, $"{path}.{name}");
            }
        }
    }
}
