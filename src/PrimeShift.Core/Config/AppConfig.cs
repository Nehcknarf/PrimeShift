using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrimeShift.Core.Display;

namespace PrimeShift.Core.Config;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool NotifyOnSwitch { get; set; } = true;
    public List<LayoutProfile> Profiles { get; set; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

public sealed class ConfigException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Reads and writes %APPDATA%\PrimeShift\config.json (PRIMESHIFT_HOME overrides the folder).</summary>
public static class ConfigStore
{
    // Keep non-ASCII profile names readable in the file instead of \uXXXX escapes.
    private static readonly ConfigJsonContext Context = new(new JsonSerializerOptions(ConfigJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static string Directory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("PRIMESHIFT_HOME");
            return string.IsNullOrEmpty(overridden)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeShift")
                : overridden;
        }
    }

    public static string ConfigPath => Path.Combine(Directory, "config.json");

    public static AppConfig Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            return new AppConfig();
        }
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path, Encoding.UTF8), Context.AppConfig) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"配置文件 {path} 格式错误：{ex.Message}", ex);
        }
    }

    public static void Save(AppConfig config)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = ConfigPath;
        var temp = path + ".tmp";
        // Write then rename so a crash mid-write never leaves a truncated config behind.
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Context.AppConfig), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
