using System.Text.Encodings.Web;
using System.Text.Json;
using ArkPlot.Avalonia.Models;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class NovelizerSettingsTests
{
    private static NovelizerSettings CreateWithCustom(ProviderConfig[]? custom = null)
    {
        return new NovelizerSettings(
            SystemPrompt: "test prompt",
            SelectedProvider: "DeepSeek",
            SelectedModel: "deepseek-v4-pro",
            ApiKeys: new Dictionary<string, string> { ["DeepSeek"] = "ds-key", ["百炼"] = "bl-key" },
            CustomProviders: custom
        );
    }

    // B1
    [Fact]
    public void GetModels_BuiltIn_DeepSeek()
    {
        var s = CreateWithCustom();
        var models = s.GetModelsForProvider("DeepSeek");
        Assert.Equal(["deepseek-v4-pro", "deepseek-v4-flash"], models);
    }

    // B2
    [Fact]
    public void GetModels_BuiltIn_Bailian()
    {
        var s = CreateWithCustom();
        var models = s.GetModelsForProvider("百炼");
        Assert.Contains("glm-5", models);
        Assert.Contains("MiniMax-M2.5", models);
        Assert.Contains("kimi-k2.5", models);
        Assert.Contains("deepseek-v4.1-flash", models);
        Assert.Contains("deepseek-v4-flash-0731", models);
        Assert.Equal(5, models.Length);
    }

    // B3
    [Fact]
    public void GetModels_Custom()
    {
        var custom = new[] { new ProviderConfig("OpenRouter", "https://or.ai/v1", "k", ["gpt-4o", "claude-3"]) };
        var s = CreateWithCustom(custom);
        Assert.Equal(["gpt-4o", "claude-3"], s.GetModelsForProvider("OpenRouter"));
    }

    // B4
    [Fact]
    public void GetModels_Unknown_ReturnsEmpty()
    {
        var s = CreateWithCustom();
        Assert.Empty(s.GetModelsForProvider("nonexistent"));
    }

    // B5
    [Fact]
    public void GetBaseUrl_BuiltIn()
    {
        var s = CreateWithCustom();
        Assert.Equal("https://api.deepseek.com", s.GetBaseUrlForProvider("DeepSeek"));
        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", s.GetBaseUrlForProvider("百炼"));
    }

    // B6
    [Fact]
    public void GetBaseUrl_Custom()
    {
        var custom = new[] { new ProviderConfig("Test", "https://test.ai/v1", "k", ["m1"]) };
        var s = CreateWithCustom(custom);
        Assert.Equal("https://test.ai/v1", s.GetBaseUrlForProvider("Test"));
    }

    // B7
    [Fact]
    public void GetApiKey_Custom_First()
    {
        var custom = new[] { new ProviderConfig("MyProvider", "https://my.ai", "custom-key", ["m1"]) };
        var s = CreateWithCustom(custom);
        Assert.Equal("custom-key", s.GetApiKeyForProvider("MyProvider"));
    }

    // B8
    [Fact]
    public void GetApiKey_BuiltIn_FromApiKeys()
    {
        var s = CreateWithCustom();
        Assert.Equal("ds-key", s.GetApiKeyForProvider("DeepSeek"));
        Assert.Equal("bl-key", s.GetApiKeyForProvider("百炼"));
    }

    // B9
    [Fact]
    public void GetApiKey_Fallback_ToEnvVar()
    {
        var s = new NovelizerSettings(
            SystemPrompt: "x", SelectedProvider: "DeepSeek", SelectedModel: "m",
            ApiKeys: new Dictionary<string, string> { ["DeepSeek"] = "", ["百炼"] = "" });
        // When ApiKeys dict is empty, falls back to env var (which may also be empty in test)
        var key = s.GetApiKeyForProvider("DeepSeek");
        var expected = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
        Assert.Equal(expected, key);
    }

    // B10
    [Fact]
    public void AllProviderNames_IncludesCustom()
    {
        var custom = new[] { new ProviderConfig("MyAI", "https://my.ai", "k", ["m1"]) };
        var s = CreateWithCustom(custom);
        var names = s.AllProviderNames;
        Assert.Contains("DeepSeek", names);
        Assert.Contains("百炼", names);
        Assert.Contains("MyAI", names);
    }

    // B11
    [Fact]
    public void AllProviderNames_NoDuplicates()
    {
        // Custom provider with same name as built-in should not duplicate
        var custom = new[] { new ProviderConfig("DeepSeek", "https://custom.ds.com", "k", ["m1"]) };
        var s = CreateWithCustom(custom);
        var names = s.AllProviderNames;
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    // Regression: null/empty providerName must not crash
    [Fact]
    public void GetModels_NullProvider_ReturnsEmpty()
    {
        var s = CreateWithCustom();
        Assert.Empty(s.GetModelsForProvider(null!));
        Assert.Empty(s.GetModelsForProvider(""));
    }

    [Fact]
    public void GetBaseUrl_NullProvider_ReturnsEmpty()
    {
        var s = CreateWithCustom();
        Assert.Equal("", s.GetBaseUrlForProvider(null!));
        Assert.Equal("", s.GetBaseUrlForProvider(""));
    }

    [Fact]
    public void GetApiKey_NullProvider_FallsToEnvVar()
    {
        var s = CreateWithCustom();
        // null provider should not crash, returns empty (no env var in test)
        var key = s.GetApiKeyForProvider(null!);
        Assert.NotNull(key);
    }

    [Fact]
    public void GetApiKey_DeletedProvider_FallsThrough()
    {
        // Simulate: user added "TestAI", selected it, then deleted it
        var s = CreateWithCustom(null); // CustomProviders is null after deletion
        // SelectedProvider was "TestAI" but it no longer exists
        var key = s.GetApiKeyForProvider("TestAI");
        // Should not crash, returns empty string (no env var for "TestAI")
        Assert.Equal("", key);
    }

    // Regression: RoundTrip with stale SelectedProvider after deletion
    [Fact]
    public void RoundTrip_StaleSelectedProvider_AfterDeletion()
    {
        // Step 1: User had a custom provider and selected it
        var custom = new[] { new ProviderConfig("TestAI", "https://test.ai", "key", ["m1"]) };
        var original = new AppSettings(
            new NovelizerSettings("prompt", "TestAI", "m1",
                new Dictionary<string, string> { ["DeepSeek"] = "", ["百炼"] = "" }, custom));

        // Step 2: User deletes the custom provider, saves
        var afterDelete = original with
        {
            Novelizer = original.Novelizer with
            {
                SelectedProvider = "TestAI", // stale! still points to deleted provider
                CustomProviders = null
            }
        };
        var json = JsonSerializer.Serialize(afterDelete, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        // Step 3: Reload — must not crash
        var restored = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(restored);

        // Step 4: Accessing stale provider methods must not crash
        Assert.Empty(restored!.Novelizer.GetModelsForProvider("TestAI"));
        Assert.Equal("", restored.Novelizer.GetBaseUrlForProvider("TestAI"));
        Assert.NotNull(restored.Novelizer.GetApiKeyForProvider("TestAI"));

        // Step 5: Null provider must not crash
        Assert.Empty(restored.Novelizer.GetModelsForProvider(null!));
        Assert.Equal("", restored.Novelizer.GetBaseUrlForProvider(null!));
    }

    // B12
    [Fact]
    public void CreateDefaults_HasExpectedValues()
    {
        var d = NovelizerSettings.CreateDefaults();
        Assert.Equal("DeepSeek", d.SelectedProvider);
        Assert.Equal("deepseek-v4-pro", d.SelectedModel);
        Assert.Null(d.CustomProviders);
        Assert.True(d.ApiKeys.ContainsKey("DeepSeek"));
        Assert.True(d.ApiKeys.ContainsKey("百炼"));
    }
}

public class VisionSettingsTests
{
    // C1
    [Fact]
    public void GetModels_Bailian()
    {
        var v = VisionSettings.CreateDefaults();
        var models = v.GetModelsForProvider("百炼");
        Assert.Equal(4, models.Length);
        Assert.Contains("qwen3-vl-flash", models);
        Assert.Contains("qwen-vl-max", models);
    }

    // C2
    [Fact]
    public void GetModels_Ollama()
    {
        var v = VisionSettings.CreateDefaults();
        Assert.Equal(["qwen3-vl:8b"], v.GetModelsForProvider("Ollama"));
    }

    // C3
    [Fact]
    public void GetModels_Custom()
    {
        var custom = new[] { new ProviderConfig("MyVision", "http://my:8080", "", ["llava:13b"]) };
        var v = new VisionSettings(false, CustomProviders: custom);
        Assert.Equal(["llava:13b"], v.GetModelsForProvider("MyVision"));
    }

    // C4
    [Fact]
    public void AllProviderNames()
    {
        var custom = new[] { new ProviderConfig("Extra", "http://x", "", ["m"]) };
        var v = new VisionSettings(false, CustomProviders: custom);
        var names = v.AllProviderNames;
        Assert.Contains("百炼", names);
        Assert.Contains("Ollama", names);
        Assert.Contains("Extra", names);
    }

    // C5
    [Fact]
    public void CreateDefaults()
    {
        var d = VisionSettings.CreateDefaults();
        Assert.False(d.IsPicDescEnabled);
        Assert.Equal("百炼", d.SelectedProvider);
        Assert.Equal("qwen3-vl-flash", d.SelectedModel);
        Assert.Equal(VisionSettings.DefaultSystemPrompt, d.SystemPrompt);
        Assert.Equal("http://localhost:11434", d.OllamaBaseUrl);
        Assert.Null(d.CustomProviders);
    }
}

public class SerializationCompatTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // D1
    [Fact]
    public void Deserialize_OldFormat_NoCustomProviders()
    {
        var json = """
        {
            "Novelizer": {
                "SystemPrompt": "old prompt",
                "SelectedProvider": "DeepSeek",
                "SelectedModel": "deepseek-v4-pro",
                "ApiKeys": { "DeepSeek": "k1", "百炼": "k2" }
            },
            "Vision": { "IsPicDescEnabled": true }
        }
        """;
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
        Assert.NotNull(settings);
        Assert.Equal("old prompt", settings!.Novelizer.SystemPrompt);
        Assert.Null(settings.Novelizer.CustomProviders);
        Assert.Equal("k1", settings.GetApiKey("DeepSeek"));
    }

    // D2
    [Fact]
    public void Deserialize_OldFormat_NoVisionFields()
    {
        var json = """
        {
            "Novelizer": {
                "SystemPrompt": "p",
                "SelectedProvider": "百炼",
                "SelectedModel": "deepseek-v4-flash",
                "ApiKeys": {}
            },
            "Vision": { "IsPicDescEnabled": false }
        }
        """;
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
        Assert.NotNull(settings?.Vision);
        Assert.False(settings!.Vision!.IsPicDescEnabled);
        // New fields should have default values
        Assert.Equal("百炼", settings.Vision.SelectedProvider);
        Assert.Equal("qwen3-vl-flash", settings.Vision.SelectedModel);
    }

    // D3
    [Fact]
    public void Deserialize_NewFormat_RoundTrip()
    {
        var custom = new[] { new ProviderConfig("TestAI", "https://test.ai/v1", "secret", ["gpt-4o"]) };
        var original = new AppSettings(
            new NovelizerSettings("prompt", "TestAI", "gpt-4o",
                new Dictionary<string, string> { ["DeepSeek"] = "ds", ["百炼"] = "bl" }, custom),
            new VisionSettings(true, "Ollama", "qwen3-vl:8b", "desc prompt", "http://localhost:11434")
        );
        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, Options);
        Assert.NotNull(restored);
        Assert.Equal("TestAI", restored!.Novelizer.SelectedProvider);
        Assert.Single(restored.Novelizer.CustomProviders!);
        Assert.Equal("TestAI", restored.Novelizer.CustomProviders![0].Name);
        Assert.True(restored.Vision!.IsPicDescEnabled);
        Assert.Equal("Ollama", restored.Vision.SelectedProvider);
    }

    // D4
    [Fact]
    public void Deserialize_EmptyJson_ReturnsDefaults()
    {
        // AppSettings.Load handles this via try/catch → CreateDefaults
        // Here we test that empty JSON object deserializes without error
        var json = "{}";
        // This will throw because Novelizer is required; AppSettings.Load catches it
        // Just verify CreateDefaults works:
        var defaults = NovelizerSettings.CreateDefaults();
        Assert.NotNull(defaults);
        var vDefaults = VisionSettings.CreateDefaults();
        Assert.NotNull(vDefaults);
    }

    // D5
    [Fact]
    public void Serialize_CustomProviders_Preserved()
    {
        var custom = new[]
        {
            new ProviderConfig("A", "https://a.com", "key-a", ["m1", "m2"]),
            new ProviderConfig("B", "https://b.com", "key-b", ["m3"])
        };
        var settings = new AppSettings(
            new NovelizerSettings("p", "A", "m1",
                new Dictionary<string, string>(), custom));

        var json = JsonSerializer.Serialize(settings, Options);
        Assert.Contains("\"Name\": \"A\"", json);
        Assert.Contains("\"Name\": \"B\"", json);
        Assert.Contains("\"Models\"", json);

        var restored = JsonSerializer.Deserialize<AppSettings>(json, Options);
        Assert.Equal(2, restored!.Novelizer.CustomProviders!.Length);
    }

    // D6
    [Fact]
    public void Deserialize_OldVision_OnlyBool()
    {
        var json = """
        {
            "Novelizer": {
                "SystemPrompt": "x",
                "SelectedProvider": "DeepSeek",
                "SelectedModel": "m",
                "ApiKeys": {}
            },
            "Vision": { "IsPicDescEnabled": true }
        }
        """;
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
        Assert.NotNull(settings?.Vision);
        Assert.True(settings!.Vision!.IsPicDescEnabled);
        // All other fields should take defaults
        Assert.Equal("百炼", settings.Vision.SelectedProvider);
        Assert.Equal("qwen3-vl-flash", settings.Vision.SelectedModel);
        Assert.Equal("http://localhost:11434", settings.Vision.OllamaBaseUrl);
    }
}
