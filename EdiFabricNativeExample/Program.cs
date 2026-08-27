using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EdiFabric.Native.X12;

namespace EdiFabric.Native.Example;

/// <summary>
/// Walkthrough of every function in the ediFabric Native X12 C ABI.
/// Each section calls one group of entry points and prints the result.
/// </summary>
internal static class Program
{
    static string ediDir = "../../../edi/";
    const string mapDir = "../../../map/";

    private static readonly string SampleEdi = File.ReadAllText(Path.Combine(ediDir, "837p.txt"));
    private static readonly string SampleEdiInvalid = File.ReadAllText(Path.Combine(ediDir, "837p_error.txt"));

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        if (options is null)
            return 1;

        Section("Load: library resolution");
        var version = EdiFabricX12.Load(options.LibraryPath);
        Console.WriteLine($"  loaded {EdiFabricX12.ResolvedLibraryPath ?? NativeLibraryHint()}");
        Console.WriteLine($"  get_app_version -> {version}");

        DemoErrors();

        try
        {
            DemoLogging("edifabric.log");

            DemoLicensing(options.Serial);
            DemoSetOnlineMap(options.Serial);

            var transactions = DemoParse();
            DemoSetLocalMap();
            DemoParseValidate();
            DemoParseAck();
            DemoParseAckInvalid();
            DemoSplit();
            DemoBuild(transactions);
            DemoMerge(transactions);
            DemoEnumerators(transactions);

            DemoTeardown();
        }
        catch (EdiFabricException exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(
                "Check that the serial is valid and that the model map can resolve the " +
                "transaction set, either online through 'default' or from local model files.");
            return 1;
        }
        finally
        {
            DemoTeardown();
        }

        Section("Finished");
        Console.WriteLine("Every entry point in c-abi-edifabric_x12_tools.h was called.");
        return 0;
    }

    /// <summary>get_error, free_error</summary>
    private static void DemoErrors()
    {
        Section("Error messages: get_error, free_error");

        foreach (var code in new[]
                 {
                     EdiFabricErrorCode.InsufficientCapacity,
                     EdiFabricErrorCode.MapNotSet,
                     EdiFabricErrorCode.LicenseNotSet,
                 })
        {
            Console.WriteLine($"  get_error({(int)code,3}) -> {EdiFabricX12.GetError(code)}");
        }

        // The raw export hands back a heap pointer that the caller owns.
        var pointer = EdiFabricX12.Raw.GetError((int)EdiFabricErrorCode.TokenExpired);
        var message = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(pointer);
        EdiFabricX12.FreeError(pointer);
        Console.WriteLine($"  raw get_error(631) -> {message}");
        Console.WriteLine("  free_error(pointer) released the string");
    }

    /// <summary>init_logger</summary>
    private static void DemoLogging(string logPath)
    {
        Section("Lifecycle: init_logger");

        EdiFabricX12.InitLogger(logPath, LogLevel.Information);
        Console.WriteLine($"  init_logger -> logging to {logPath}");
    }

    /// <summary>ensure_token, get_token, validate_token, set_token, get_token_expiration, set_serial</summary>
    private static void DemoLicensing(string serial)
    {
        Section("Licensing: ensure_token, get_token, validate_token, set_token, set_serial");
        //  For the free and developer plans only set_serial must be used. Tokens are only available for the Enterprise plan.

        EdiFabricX12.SetSerial(serial);
        Console.WriteLine("  set_serial -> ok");
    }

    /// <summary>set_map online</summary>
    private static void DemoSetOnlineMap(string serial)
    {
        Section("Model map: set_map online");

        // "default" falls back to the online spec service for unmapped transaction sets.
        // For offline use, add local models instead:
        //   ["850:005010"] = new { type = 1, name = "850.json", location = "C:/models" }
        var map = new JsonObject
        {
            ["default"] = serial,
            ["maps"] = new JsonObject(),
        };

        EdiFabricX12.SetMap(map.ToJsonString(JsonOptions));
        Console.WriteLine($"  set_map -> default={serial}, local maps=0");
    }

    /// <summary>set_map local</summary>
    private static void DemoSetLocalMap()
    {
        Section("Model map: set_map local");

        var mapLocation = Path.GetFullPath(mapDir);
        var localMap = JsonNode.Parse(File.ReadAllText(Path.Combine(mapDir, "map.json")))!.AsObject();
        var maps = localMap["maps"]!.AsObject();
        foreach (var entry in maps)
            entry.Value!["location"] = mapLocation;

        EdiFabricX12.SetMap(localMap.ToJsonString(JsonOptions));
        Console.WriteLine($"  set_map -> default='', local maps={maps.Count}, location={mapLocation}");
    }

    /// <summary>parse in mode 1</summary>
    private static string DemoParse()
    {
        Section("Parse: parse (mode 1, JSON only)");

        var result = EdiFabricX12.Parse(SampleEdi);
        Console.WriteLine($"  {result.Output.Length} bytes of JSON, offset={result.Offset}");
        Console.WriteLine($"  {Preview(result.Transactions)}");
        return result.Transactions;
    }

    /// <summary>parse in mode 2</summary>
    private static void DemoParseValidate()
    {
        Section("Parse: parse (mode 2, JSON + validation report)");

        var result = EdiFabricX12.Parse(SampleEdi, ParseMode.JsonValidate, BuildParseConfig());
        Console.WriteLine($"  {result.Output.Length} bytes total, validation starts at offset {result.Offset}");
        Console.WriteLine($"  validation -> {Preview(result.Report)}");
    }

    /// <summary>parse in mode 3</summary>
    private static void DemoParseAck()
    {
        Section("Parse: parse (mode 3, JSON + validation + acknowledgment)");

        var result = EdiFabricX12.Parse(SampleEdi, ParseMode.JsonValidateAck, BuildParseConfig());
        Console.WriteLine($"  {result.Output.Length} bytes total, report starts at offset {result.Offset}");
        Console.WriteLine($"  report -> {Preview(result.Report, 600)}");
    }

    /// <summary>parse in mode 3</summary>
    private static void DemoParseAckInvalid()
    {
        Section("Parse: parse (mode 3, JSON + validation + acknowledgment) invalid");

        var result = EdiFabricX12.Parse(SampleEdiInvalid, ParseMode.JsonValidateAck, BuildParseConfig());
        Console.WriteLine($"  {result.Output.Length} bytes total, report starts at offset {result.Offset}");
        Console.WriteLine($"  report -> {Preview(result.Report, 600)}");
    }

    /// <summary>start_split, split, get_result</summary>
    private static void DemoSplit()
    {
        Section("Split: start_split, split, get_result");

        EdiFabricX12.StartSplit(SampleEdi, ParseMode.Json, BuildSplitConfig());

        var step = 0;
        while (true)
        {
            var current = EdiFabricX12.Split();
            step++;

            Console.WriteLine($"  step {step}: size={current.Size} offset={current.Offset} last={current.IsLast}");
            if (current.Size > 0)
            {
                var payload = Encoding.UTF8.GetString(EdiFabricX12.GetResult(current.Size));
                Console.WriteLine($"    {Preview(payload, 160)}");
            }

            if (current.IsLast)
                break;
        }
    }

    /// <summary>build</summary>
    private static void DemoBuild(string transactions)
    {
        Section("Build: build");

        var edi = EdiFabricX12.Build(transactions, postfix: "\r\n");
        Console.WriteLine($"  {edi.Length} bytes of X12");
        foreach (var line in edi.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            Console.WriteLine($"  {line}");
    }

    /// <summary>start_merge, merge, get_result</summary>
    private static void DemoMerge(string transactions)
    {
        Section("Merge: start_merge, merge, get_result");

        EdiFabricX12.StartMerge(transactions);

        var count = 0;
        while (true)
        {
            var size = EdiFabricX12.Merge();
            if (size == 0)
                break;

            var segment = Encoding.UTF8.GetString(EdiFabricX12.GetResult(size));
            count++;
            Console.WriteLine($"  segment {count,2}: {segment}");
        }

        Console.WriteLine($"  merge produced {count} segments");
    }

    /// <summary>EnumerateSplit, EnumerateMerge</summary>
    private static void DemoEnumerators(string transactions)
    {
        Section("Convenience wrappers: EnumerateSplit, EnumerateMerge");

        var parts = EdiFabricX12.EnumerateSplit(SampleEdi, ParseMode.Json, BuildSplitConfig()).ToList();
        var sizes = string.Join(", ", parts.Select(part => part.Payload.Length));
        Console.WriteLine($"  EnumerateSplit -> {parts.Count} payloads, sizes [{sizes}]");

        var segments = EdiFabricX12.EnumerateMerge(transactions).ToList();
        Console.WriteLine($"  EnumerateMerge -> {segments.Count} segments");
    }

    /// <summary>clear_cache, shutdown_logger</summary>
    private static void DemoTeardown()
    {
        Section("Teardown: clear_cache, shutdown_logger");

        EdiFabricX12.ClearCache();
        Console.WriteLine("  clear_cache -> map, license, stream state and results reset");

        EdiFabricX12.ShutdownLogger();
        Console.WriteLine("  shutdown_logger -> logger stopped");
    }

    private static string BuildParseConfig()
    {
        var config = new JsonObject
        {
            ["validate"] = new JsonObject
            {
                ["regex"] = null,
                ["date_format"] = null,
                ["time_format"] = null,
                ["skip_seq_count"] = false,
                ["skip_hl_seq"] = false,
                ["snip_level"] = 4,
                ["max_errors"] = 100,
            },
            ["ack"] = new JsonObject
            {
                ["supress_ta1"] = false,
                ["ak901p"] = false,
                ["gen_for_valid"] = true,
                ["gen997"] = false,
            },
        };

        return config.ToJsonString(JsonOptions);
    }

    private static string BuildSplitConfig()
    {
        var config = JsonNode.Parse(BuildParseConfig())!.AsObject();
        config["split"] = new JsonObject
        {
            ["segment_id"] = "LX",
            ["segment_depth"] = 6,
            ["loop_id"] = "2400",
        };

        return config.ToJsonString(JsonOptions);
    }

    private static string Preview(string text, int limit = 400)
        => text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit), " ...");

    private static string NativeLibraryHint()
        => $"{EdiFabricX12.Raw.PlatformFileName} (resolved by the default probing logic)";

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 70));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 70));
    }
}

/// <summary>Command line options for the example.</summary>
internal sealed record CommandLineOptions(string Serial, string? LibraryPath)
{
    private const string DefaultSerial = "bd96a836feca45cb91c86ee65d281f52";

    public static CommandLineOptions? Parse(string[] args)
    {
        string? serial = null;
        string? libraryPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--serial" when i + 1 < args.Length:
                    serial = args[++i];
                    break;
                case "--lib" when i + 1 < args.Length:
                    libraryPath = args[++i];
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    return null;
                default:
                    Console.Error.WriteLine($"Unrecognized argument: {args[i]}");
                    PrintUsage();
                    return null;
            }
        }

        serial ??= Environment.GetEnvironmentVariable("EDIFABRIC_SERIAL") ?? DefaultSerial;
        return new CommandLineOptions(serial, libraryPath);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run -- [--serial SERIAL] [--lib PATH] [--skip-network]");
        Console.WriteLine();
        Console.WriteLine("  --serial SERIAL   license serial (default: EDIFABRIC_SERIAL or the free plan serial)");
        Console.WriteLine("  --lib PATH        edifabric-x12-tools library file or its folder");
    }
}
