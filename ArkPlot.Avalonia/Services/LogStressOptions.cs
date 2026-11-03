using System;
using System.Globalization;
using System.IO;

namespace ArkPlot.Avalonia.Services;

/// <summary>Opt-in, offline stress of the real workflow log view.</summary>
internal sealed record LogStressOptions(
    int Count = 50000,
    int BatchSize = 100,
    int IntervalMs = 10,
    string ReportPath = "log-stress.jsonl",
    bool ExitWhenDone = false)
{
    internal static LogStressOptions? Current { get; set; }

    internal static LogStressOptions? Parse(string[] args)
    {
        var enabled = false;
        var hasModifiers = false;
        var options = new LogStressOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (name == "--log-stress") { enabled = true; continue; }
            if (name == "--log-stress-exit") { hasModifiers = true; options = options with { ExitWhenDone = true }; continue; }
            if (!name.StartsWith("--log-stress-", StringComparison.Ordinal)) continue;
            hasModifiers = true;
            if (name is not ("--log-stress-count" or "--log-stress-batch" or "--log-stress-interval" or "--log-stress-report"))
                throw new ArgumentException($"Unknown log stress option: {name}");
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {name}");
            var value = args[i];
            if (name == "--log-stress-report")
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Report path cannot be empty.");
                options = options with { ReportPath = Path.GetFullPath(value) };
                continue;
            }
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
                throw new ArgumentException($"{name} requires a positive integer.");
            options = name switch
            {
                "--log-stress-count" => options with { Count = number },
                "--log-stress-batch" when number <= 1000 => options with { BatchSize = number },
                "--log-stress-interval" => options with { IntervalMs = number },
                _ => throw new ArgumentException("Log stress batch size must be between 1 and 1000."),
            };
        }
        if (!enabled && hasModifiers)
            throw new ArgumentException("Log stress options require --log-stress.");
        return enabled ? options : null;
    }
}
