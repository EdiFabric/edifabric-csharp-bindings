# ediFabric Native X12 — C# bindings

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

[Download **ediFabric Native** Library](https://support.edifabric.com/hc/en-us/articles/37289848931869-Download)

Plus your **model files** (per transaction set) and a **map file** that tells the
engine where to find them. See [Model map](#model-map) for details.

## Getting started

**Download the library** from [here](https://support.edifabric.com/hc/en-us/articles/37289848931869-Download).
Put the native library in the repository root, then run the walkthrough:

```bash
cd EdiFabricNativeExample
dotnet run
```

The project copies the library next to the executable on build. It authorizes with
the free plan serial, loads the model map, and calls every function in the ABI,
printing what each one returns.

```
======================================================================
Parse: parse (mode 2, JSON + validation report)
======================================================================
  1754 bytes total, validation starts at offset 1708
  validation -> {"errors":[],"errors_count":0,"data_count":10}
```

Options:

```bash
dotnet run -- --serial YOUR_SERIAL     # use your own license
dotnet run -- --lib /opt/edifabric     # library file or the folder holding it
dotnet run -- --skip-network           # authorize with set_serial only
```

The library is resolved through a `DllImportResolver` that tries
`EdiFabricX12.LibraryPath`, then `EDIFABRIC_X12_LIB`, then the application
directory and a few levels above it, before falling back to the default .NET
probing logic. The serial comes from `--serial`, then `EDIFABRIC_SERIAL`, then
the built-in free plan serial.

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

const string serial = "your-serial";

EdiFabricX12.Load();                    // optional, any call loads on demand
EdiFabricX12.SetSerial(serial);         // or SetToken(token) for offline use (only available for the Enterprise license)
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
> The examples are available with a free plan which can be used only with Serial model validation.
> Tokens are only available for the Enterprise plan. For the free and developer plans, the only
> licensing call must be `set_serial`.

The serial key for the free plan is:
```
bd96a836feca45cb91c86ee65d281f52
```

Two models are supported. Tokens are recommended for containers, air-gapped
machines, and high volume; serials are simplest when always online.

```csharp
// Token (Enterprise): keep a cached token fresh; refreshes if it expires within N seconds
EdiFabricX12.EnsureToken(serial, seconds: 3600);
Console.WriteLine(EdiFabricX12.GetTokenExpiration());   // DateTime, or null when unset

// Or fetch / validate / set a token yourself
var token = EdiFabricX12.GetToken(serial);
EdiFabricX12.ValidateToken(token);
EdiFabricX12.SetToken(token);

// Serial (free / developer): authorize per process against the license server
EdiFabricX12.SetSerial(serial);
```

## Model map

`SetMap` tells the engine where to find transaction-set models. Keys are
`message:version`. Set `default` to your serial to resolve unmapped transaction
sets through the online spec service, or leave it `null` and map everything locally.

```json
{
  "default": null,
  "maps": {
    "837:005010X222A1": { "type": 1, "name": "837P.json", "location": "/opt/models" },
    "850:005010":       { "type": 1, "name": "850.json",  "location": "/opt/models" }
  }
}
```

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

| Code | Meaning |
| --- | --- |
| 501 | Unknown |
| 502 | No internet access to the authentication API |
| 503 | Local map file has invalid paths or file names |
| 611 | Incorrect or empty input |
| 612 | Logger initialization failed |
| 613 | Map JSON could not be deserialized |
| 614 | Negative output capacity |
| 615 | Model map not set, call `SetMap` first |
| 616 | Mode must be 1, 2, or 3 |
| 617 | No JSON produced |
| 618 | Validation result unavailable |
| 619 | Validation report serialization failed |
| 620 | Incorrect token |
| 621 | Config JSON could not be deserialized |
| 622 | Split `segment_id` missing or empty |
| 623 | `Split` called before `StartSplit` |
| 624 | No result available for `GetResult` |
| 625 | `GetResult` buffer size mismatch |
| 626 | `Merge` called before `StartMerge` |
| 627 | Incorrect or null output pointer |
| 628 | Incorrect serial |
| 629 | License not installed; call `EnsureToken` or `SetToken` |
| 630 | Application maximum version exceeded |
| 631 | Token expired |
| 632 | Token missing |
| 633 | Maximum licenses exceeded |
| 634 | License snapshot not found |
| 635 | License not set, call `SetToken` or `SetSerial` |

## Troubleshooting

**`DllNotFoundException`** — the library is not on any searched path. Set
`EdiFabricX12.LibraryPath` before the first call, or set `EDIFABRIC_X12_LIB`.

**Error 615 on parse** — call `SetMap` before parsing or splitting. `ClearCache`
resets the map, so reload it afterwards.

**Error 635 on parse** — authorize first with `SetToken` or `SetSerial`.

**Error 633 on ensure_token / get_token** — the plan's machine quota is used up.
Contact support.

## Links

- [Documentation](https://support.edifabric.com/hc/en-us/articles/37276016388125-Introduction)
- [Product page](https://www.edifabric.com/edifabric-native.html)
- Support: support@edifabric.com
