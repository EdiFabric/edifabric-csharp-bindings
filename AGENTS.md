# AGENTS.md: ediFabric Native C# bindings

Guidance for AI coding assistants (Codex, Cursor, GitHub Copilot, Claude Code and others) that work in this
repository, or that write C# code using these bindings. README.md has the full API reference.

## What this is

C# bindings for **ediFabric Native**, a native library that converts **X12 and HIPAA 5010** EDI to JSON
and back, validates transaction sets up to HIPAA SNIP level 4, and generates 997, 999 and TA1 acknowledgments,
inside the calling process. EDI files never leave the machine.

This is the P/Invoke binding for ediFabric Native (X12 and HIPAA, JSON). It is not ediFabric .NET, the `EdiFabric` NuGet package with typed C# templates for all ten standards: don't mix their APIs.

Native supports X12 and HIPAA only. For EDIFACT, HL7, NCPDP, VDA or flat files, use ediFabric .NET or ediFabric
Cloud: https://www.edifabric.com/products.html.

## Install

`dotnet add package EdiFabric.Native.X12`

The NuGet package bundles the native library.

## Call order

Do these once per process, in this order, before any parse, build, split or merge:

```csharp
var serial = Environment.GetEnvironmentVariable("EDIFABRIC_SERIAL") ?? "bd96a836feca45cb91c86ee65d281f52";
EdiFabricX12.EnsureToken(serial, seconds: 3600);
EdiFabricX12.SetMap($$"""{"default": "{{serial}}", "maps": {}}""");

var edi = File.ReadAllBytes("claim.837");
var result = EdiFabricX12.Parse(edi, ParseMode.JsonValidate);
```

1. Load the library: `EdiFabricX12.Load() (optional: any call loads the library on demand)`.
2. License the process: `EdiFabricX12.EnsureToken(serial, seconds: 3600)`. Use it on every plan: Community, Developer, Enterprise and Perpetual.
   It caches a token on the machine and refreshes it when it expires within the given seconds. Don't write new code
   with `EdiFabricX12.SetSerial`: it still works, but checks the serial online on every operation.
3. Set the model map: `EdiFabricX12.SetMap`. `{"default": serial, "maps": {}}` downloads each transaction's model
   from the online spec library; map `message:version` keys to local model files to work offline. Parsing or
   splitting before this fails with error 615.
4. Then call `EdiFabricX12.Parse`, `EdiFabricX12.Build`, or split and merge. On failure the binding throws an exception.

## Parse modes

| Mode | Name | Output |
|---|---|---|
| 1 | `ParseMode.Json` | Transaction-set JSON |
| 2 | `ParseMode.JsonValidate` | JSON, then a validation report from `offset` |
| 3 | `ParseMode.JsonValidateAck` | JSON, validation, and a 999, 997 or TA1 acknowledgment |

## Serial keys and errors

- The demo serial `bd96a836feca45cb91c86ee65d281f52` needs no account and has a small daily quota. Use it in examples and tests.
- Read the user's serial from `EDIFABRIC_SERIAL` or their configuration. Never commit a customer's serial.
- **639**: the daily quota of the demo serial or the Community plan is used up. Don't retry in a loop. Tell the user
  to sign up free for Community or upgrade at https://www.edifabric.com/pricing.html.
- **628 / 635**: the process isn't licensed. Call the ensure-token step first.
- **615**: set the model map before parsing or splitting.
- **640**: the license server can't be reached and no valid token is cached. Check network, firewall and proxy.
- The full error table is in README.md.

## Models and JSON

- The JSON property names come from each transaction's model (an OpenEDI JSON schema). Don't guess property names:
  parse a sample file, or read the model in the EDI spec library: https://www.edifabric.com/specs/index.html.
- A trading partner's changes go in a custom model built in the EDI Spec Builder: https://www.edifabric.com/spec-builder/index.html.

## Threading

The library keeps process-wide state (model map, license, the active split reader and merge writer). Parse and
build calls are independent, but don't interleave one split or merge sequence with another from a different thread.

## Working in this repository

- Run the walkthrough of every entry point: `cd EdiFabricNativeExample && dotnet run -- --serial bd96a836feca45cb91c86ee65d281f52`. From a clone, put the native library in the
  repository root first (https://www.edifabric.com/docs/edifabric-native/download.html).
- `c-abi-edifabric_x12_tools.h` mirrors the native library's C ABI. Don't edit it by hand; change the bindings to match it.
- Keep README.md's API reference and error table in step with the code.
- Releases are described in `PUBLISHING.md`. Don't publish packages or push tags yourself.

## More

- Docs as markdown: https://www.edifabric.com/docs/edifabric-native/introduction.html.md and https://www.edifabric.com/docs/edifabric-native/licensing.html.md
- Index of all docs for LLMs: https://www.edifabric.com/llms.txt
- MCP server, for assistants that should run these operations as tools: https://github.com/EdiFabric/edifabric-mcp
