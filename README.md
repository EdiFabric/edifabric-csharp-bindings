# ediFabric Native X12 - C# bindings

**ediFabric Native** is a self-contained, high-performance X12 EDI native shared library. It converts X12 EDI to JSON (and back),
validates transaction sets, and generates acknowledgments — callable from **any
language with a C foreign-function interface** (C, C++, Rust, Go, Python, Node.js,
Java/JNA, .NET, …). No .NET runtime, JVM, or other dependency is required on the
target machine.

C# P/Invoke bindings for [ediFabric Native](https://www.edifabric.com/edifabric-native.html).

| File | Purpose |
| --- | --- |
| `EdiFabricNativeExample/NativeMethods.cs` | `DllImport` declarations mirroring the C header |
| `EdiFabricNativeExample/EdiFabricX12.cs` | managed API over those declarations |
| `EdiFabricNativeExample/Program.cs` | runnable walkthrough of every entry point |
| `../c-abi-edifabric_x12_tools.h` | the C header these bindings mirror |

## Requirements

- .NET 10.0 SDK or later
- 64-bit Windows, Linux, or macOS
- The native library for your platform:

| Platform | File |
| --- | --- |
| Windows | `edifabric-x12-tools.dll` |
| Linux | `edifabric-x12-tools.so` |
| macOS | `edifabric-x12-tools.dylib` |

1. [Sign up free for **Community**](https://www.edifabric.com/pricing.html) to get an evaluation serial key. Community never expires, requires no credit card, and is limited to 250 operations per day for non-production use. After signup, retrieve your serial from [Your Account](https://support.edifabric.com/hc/en-us/articles/360007159031-Your-Account-API-key).
2. [Download the **ediFabric Native** library](https://support.edifabric.com/hc/en-us/articles/37289848931869-Download).

Plus your **model files** (per transaction set) and a **map file** that tells the
engine where to find them. See [Model map](#model-map) for details.

## Getting started

**Sign up free for Community** at [edifabric.com/pricing](https://www.edifabric.com/pricing.html)
to get an evaluation serial key, then **download the library** from
[here](https://support.edifabric.com/hc/en-us/articles/37289848931869-Download).
Put the native library in the repository root, then run the walkthrough with your serial:

```bash
cd EdiFabricNativeExample
dotnet run -- --serial YOUR_SERIAL
```

The project copies the library next to the executable on build. It authorizes with
your Community (or paid) serial, loads the model map, and calls every function in
the ABI, printing what each one returns.

```
======================================================================
Parse: parse (mode 2, JSON + validation report)
======================================================================
  1754 bytes total, validation starts at offset 1708
  validation -> {"errors":[],"errors_count":0,"data_count":10}
```

Options:

```bash
dotnet run -- --serial YOUR_SERIAL     # Community or paid serial (required)
dotnet run -- --lib /opt/edifabric     # library file or the folder holding it
```

You can also set `EDIFABRIC_SERIAL` instead of passing `--serial`.

The library is resolved through a `DllImportResolver` that tries
`EdiFabricX12.LibraryPath`, then `EDIFABRIC_X12_LIB`, then the application
directory and a few levels above it, before falling back to the default .NET
probing logic. The serial comes from `--serial`, then `EDIFABRIC_SERIAL`.

All strings and payloads cross the boundary as **UTF‑8 byte buffers**
(`pointer + length`). Every function returns `0` on success or a non-zero
[error code](#error-codes).

## Usage

Copy `NativeMethods.cs` and `EdiFabricX12.cs` into your project and enable unsafe
blocks:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

```csharp
using EdiFabric.Native.X12;

const string serial = "your-serial";   // from your Community or paid plan

EdiFabricX12.Load();                    // optional, any call loads on demand
EdiFabricX12.SetSerial(serial);         // Community: SetSerial. Developer: prefer EnsureToken. Enterprise: prefer SetToken.
EdiFabricX12.SetMap($$"""{"default": "{{serial}}", "maps": {}}""");

var edi = File.ReadAllBytes("837p.txt");
var result = EdiFabricX12.Parse(edi);
Console.WriteLine(result.Transactions);
```

`Parse` accepts a `string` or a `ReadOnlySpan<byte>`, so EDI read straight from
disk needs no intermediate decoding.

### Validation and acknowledgments

`ParseResult` splits the native output for you: `Transactions` is the JSON, and
`Report` is the validation and acknowledgment section that follows it.

```csharp
var config = """
{
  "validate": { "snip_level": 2, "max_errors": 0 },
  "ack": { "gen997": false, "supress_ta1": false }
}
""";

var result = EdiFabricX12.Parse(edi, ParseMode.JsonValidateAck, config);
using var report = JsonDocument.Parse(result.Report);
Console.WriteLine(report.RootElement.GetProperty("errors_count").GetInt32());
```

| Mode | Constant | Output |
| --- | --- | --- |
| 1 | `ParseMode.Json` | transaction-set JSON |
| 2 | `ParseMode.JsonValidate` | JSON plus a validation report |
| 3 | `ParseMode.JsonValidateAck` | JSON plus validation and a 999/997/TA1 acknowledgment |

### Streaming large interchanges

`EnumerateSplit` streams one transaction set (or repeating loop) at a time with
flat memory use. `segment_id` must be `ST` or the first segment of a repeating loop.

```csharp
var config = """{ "split": { "segment_id": "LX", "segment_depth": 6, "loop_id": "2400" } }""";

foreach (var part in EdiFabricX12.EnumerateSplit(edi, ParseMode.Json, config))
    Handle(Encoding.UTF8.GetString(part.Payload));
```

`EnumerateMerge` streams a full interchange JSON document back out one segment at a time:

```csharp
using var output = File.Create("out.edi");
foreach (var segment in EdiFabricX12.EnumerateMerge(result.Transactions))
{
    output.Write(segment);
    output.Write("\r\n"u8);
}
```

Both wrap the underlying `StartSplit`/`Split`/`GetResult` and
`StartMerge`/`Merge`/`GetResult` sequences, which you can also drive directly.

### Building EDI

```csharp
var edi = EdiFabricX12.Build(result.Transactions, postfix: "\r\n");   // null for compact output
```

## API reference

Conventions used by every function:

- Returns `int`: `0` = success, non-zero = [error code](#error-codes).
- Inputs are UTF‑8 `byte*` + `int length`.
- Output functions use **grow-and-retry**: if the buffer is too small the call
  returns `1` (`InsufficientCapacity`) and writes the required size into the
  length out-parameter; reallocate and call again.
- Exceptions never cross the boundary.

Every wrapper throws `EdiFabricException` when the native call returns a
non-zero status. Buffer growth (`InsufficientCapacity`) is retried automatically.

| Group | Members |
| --- | --- |
| Loading | `Load`, `LibraryPath`, `ResolvedLibraryPath` |
| Lifecycle | `InitLogger`, `ShutdownLogger`, `ClearCache` |
| Licensing | `EnsureToken`, `GetAppVersion`, `GetToken`, `ValidateToken`, `SetToken`, `GetTokenExpiration`, `GetTokenExpirationTicks`, `SetSerial` |
| Model map | `SetMap` |
| Processing | `Parse`, `StartSplit`, `Split`, `Build`, `StartMerge`, `Merge`, `GetResult` |
| Errors | `GetError`, `FreeError`, `Check` |
| Wrappers | `EnumerateSplit`, `EnumerateMerge` |
| Types | `ParseMode`, `LogLevel`, `EdiFabricErrorCode`, `EdiFabricException`, `ParseResult`, `SplitStep`, `SplitPart`, `EdiFabricX12.Raw` |

`GetError` frees the native string for you. Use `EdiFabricX12.Raw.GetError` with
`FreeError` only when you want to own the unmanaged memory yourself. Builds that
do not export `free_error` fall back to `Marshal.FreeHGlobal`.

## Licensing

> [!NOTE]
> Sign up free for the [Community plan](https://www.edifabric.com/pricing.html)
> to get an evaluation serial key. Community never expires, requires no credit
> card, and is for non-production evaluation, learning, and prototyping
> (250 operations per day). After signup, copy your serial from
> [Your Account](https://support.edifabric.com/hc/en-us/articles/360007159031-Your-Account-API-key).
>
> If you hit the Community daily quota, native calls return [error 639](#error-codes);
> upgrade at [edifabric.com/pricing](https://www.edifabric.com/pricing.html) if you
> want to continue.

| Plan | What works | Recommended |
| --- | --- | --- |
| Community | `SetSerial` only | `SetSerial` |
| Developer | `SetSerial` and `EnsureToken` (`EnsureToken` caches the result for 1 day) | `EnsureToken` |
| Enterprise | `SetSerial`, `EnsureToken`, `GetToken` / `SetToken` | `SetToken` (offline tokens) |

```csharp
// Community: authorize per process against the license server
EdiFabricX12.SetSerial(serial);

// Developer (recommended): 1-day built-in cache; refreshes if the token expires within N seconds
EdiFabricX12.EnsureToken(serial, seconds: 3600);
Console.WriteLine(EdiFabricX12.GetTokenExpiration());   // DateTime, or null when unset

// Developer (also works): same as Community, online check per process
EdiFabricX12.SetSerial(serial);

// Enterprise (recommended): fetch / validate / set an offline token yourself
var token = EdiFabricX12.GetToken(serial);
EdiFabricX12.ValidateToken(token);
EdiFabricX12.SetToken(token);
```

## Model map

`SetMap` tells the engine where to find transaction-set models. Keys are
`message:version`. Set `default` to your serial to resolve unmapped transaction
sets through the online spec service, or leave it `null` (or `""`) and map
everything locally.

The example builds that JSON at runtime instead of hard-coding paths. Online
fallback is a `JsonObject` with `default` set to your serial:

```csharp
var map = new JsonObject
{
    ["default"] = serial,
    ["maps"] = new JsonObject(),
};

EdiFabricX12.SetMap(map.ToJsonString());
```

For local models, load a map file and rewrite each entry's `location` to the
folder that actually holds the JSON files (see `DemoSetLocalMap` in
`EdiFabricNativeExample/Program.cs`):

```csharp
var mapLocation = Path.GetFullPath("map");
var localMap = JsonNode.Parse(File.ReadAllText(Path.Combine(mapLocation, "map.json")))!.AsObject();
var maps = localMap["maps"]!.AsObject();
foreach (var entry in maps)
    entry.Value!["location"] = mapLocation;

EdiFabricX12.SetMap(localMap.ToJsonString());
```

`map.json` lists each transaction set; `location` is filled in at runtime so the
same file works from any working directory:

```json
{
  "default": "",
  "maps": {
    "837:005010X222A1": { "type": 1, "name": "model837P.json", "location": "" },
    "834:005010X220A1": { "type": 1, "name": "model834.json",  "location": "" }
  }
}
```

You can also mix both: keep `default` as your serial and add local entries under
`maps` for the transaction sets you ship on disk.

All X12 transactions, such as 837P, 834, 850, etc. are represented as proprietary JSON.
Download a standard model from [EdiNation Spec Library](https://edination.edifabric.com/edi-spec-library.html),
or a custom model from [EdiNation Spec Builder](https://edination.edifabric.com/edi-spec-builder.html).
Create/modify models in OpenEDI format, upload them in EdiNation Spec Builder and download them as JSON for use in ediFabric Native.

To download a model in either EdiNation Spec Library or EdiNation Spec Builder,
select the model first, then in the JSON view
select the Download button in the top right corner.

![Model Img](https://github.com/EdiFabric/native-csharp-examples/blob/main/model.png)

Choose to download as **ediFabric Native**.

## Configuration JSON

All JSON uses **`snake_case`** keys and is case-insensitive.

### ParseConfig (`parse`)

```json
{
  "validate": { "regex": null, "date_format": null, "time_format": null,
                "skip_seq_count": false, "skip_hl_seq": false,
                "snip_level": 0, "max_errors": 0 },
  "ack":      { "supress_ta1": false, "ak901p": false,
                "gen_for_valid": false, "gen997": false }
}
```

- `validate` — applied when `mode ≥ 2`; `snip_level` is `1`–`4`.
- `ack` — applied when `mode == 3`.

All sections are optional for `parse`.

### SplitConfig (`start_split`)

```json
{
  "split":    { "segment_id": "ST", "segment_depth": 0, "loop_id": null }
}
```

- `split` — required for `start_split`.

Splitting is possible for the following boundaries:

- Transaction - for files that contain batches of transactions.
- Repeating loop - for files that contain batches of loops, such as order lines, claims or benefit enrollments.

The splitter must be configured as follows:

- `segment_id`  — the name of the segment to split by. It must be either ST or the first segment in the repeatable loop (Mandatory).
- `segment_depth` — the depth of the segment in the model hierarchy (Mandatory).
- `loop_id` — the name of the loop for the segment specified in segment_id (Optional).

The values for the splitter can be found in EdiNation by loading a sample file. For example, if you want to split by loop 2000A in 837P, load an 837P file in EdiNation (or use the example one), click on the first segment in that loop, e.g., HL. `segment_id` is **CODE**,  `loop_id` is the last item in **PATH**, and `segment_depth` is **DEPTH**.

> [!NOTE]
> If a segment does not show a SPLITTER copy button, than splitting is not possible by that segment.

The easiest way to get the splitter configuration is to click on the copy button under SPLITTER that has the full splitter JSON pre-configured.

![Model Img](https://github.com/EdiFabric/native-csharp-examples/blob/main/splitter.png)

## Threading

The library holds process-global state (model map, active split reader, active
merge writer, last result, license) behind an internal lock. `Parse` and `Build`
are independent per call, but each split or merge sequence must run to completion
without another split or merge interleaving from a different thread.

## Error codes

`0` is success and `1` means the output buffer was too small. Library-level codes
are exposed as `EdiFabricErrorCode`, and `GetError(code)` returns the message.
Validation codes (elements, segments, transaction sets, groups, and interchanges)
appear in the parse report when `mode ≥ 2`.

**Error 639** means the Community (evaluation) daily quota was exceeded.
Upgrade your plan at [edifabric.com/pricing](https://www.edifabric.com/pricing.html)
if you wish to continue.

### Parser and library

| Code | Meaning |
| --- | --- |
| 1 | The suggested output buffer size is too small |
| 501 | Unexpected error. Contact support@edifabric.com and include a sample project/file to reproduce the issue |
| 502 | No connection to EdiNation API |
| 503 | The model map configuration is invalid. Check the paths and the model file names are correct |
| 611 | The input buffer is either null or its size is nill |
| 612 | The logger failed to log |
| 613 | The map configuration file is invalid |
| 614 | The output capacity must be positive |
| 615 | Models map must be set before parsing or splitting |
| 616 | Mode must be any of: 1 - Parse, 2 - Parse and Validate, 3 - Parse and Validate and Acknowledge |
| 617 | Parser failed. Contact support@edifabric.com and include a sample project/file to reproduce the issue |
| 618 | Validation failed. Contact support@edifabric.com and include a sample project/file to reproduce the issue |
| 619 | Validation serializer failed. Contact support@edifabric.com and include a sample project/file to reproduce the issue |
| 620 | The token is invalid. Contact support@edifabric.com for assistance |
| 621 | The configuration file is invalid |
| 622 | The split segment ID must not be blank |
| 623 | Call `StartSplit` before splitting |
| 624 | The result can't be retrieved. Contact support@edifabric.com and include a sample project/file to reproduce the issue |
| 625 | Result buffer size mismatched |
| 626 | Call `StartMerge` before merging |
| 627 | The output buffer is either null or its size is nill |
| 628 | The serial number is missing or incorrect. `GetToken` doesn't work with Developer license |
| 629 | License was not installed. Contact support@edifabric.com for assistance |
| 630 | No license to use this version. Contact support@edifabric.com for assistance |
| 631 | The token has expired. Get and set a new token to continue |
| 632 | The token is missing. Set token to continue |
| 633 | Reached the maximum number of licenses. Set token to continue |
| 634 | Environment not recognized for licensing or reached the maximum number of licenses |
| 635 | Serial or token not found. Either set token or serial to continue |
| 636 | The rate to get serials was exceeded for your license. Wait for 60 seconds and try again or upgrade your license |
| 637 | Invalid JSON. Enable logging for additional details |
| 638 | The operation is not supported by your license |
| 639 | Community daily quota exceeded. Your license has reached its daily call limit. Upgrade your plan at edifabric.com to continue |

## Troubleshooting

**`DllNotFoundException`** — the library is not on any searched path. Set
`EdiFabricX12.LibraryPath` before the first call, or set `EDIFABRIC_X12_LIB`.

**Error 615 on parse** — call `SetMap` before parsing or splitting. `ClearCache`
resets the map, so reload it afterwards.

**Error 628 / 635 on parse** — authorize first: `SetSerial` on Community,
`EnsureToken` (or `SetSerial`) on Developer, or `SetToken` on Enterprise.

**Error 633 on ensure_token / get_token** — the plan's machine quota is used up.
Contact support.

**Error 639** — the Community (evaluation) daily quota was exceeded. Wait until
the next day, or [upgrade your plan](https://www.edifabric.com/pricing.html) if
you wish to continue.

## Links

- [Documentation](https://support.edifabric.com/hc/en-us/articles/37276016388125-Introduction)
- [Product page](https://www.edifabric.com/edifabric-native.html)
- [Community plan (free signup)](https://www.edifabric.com/pricing.html)
- [Your Account](https://support.edifabric.com/hc/en-us/articles/360007159031-Your-Account-API-key)
- Support: support@edifabric.com
