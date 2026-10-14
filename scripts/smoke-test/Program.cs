// Checks that the EdiFabric.Native.X12 package loads the native library it bundles.
using EdiFabric.Native.X12;

var version = EdiFabricX12.Load();
var loaded = EdiFabricX12.ResolvedLibraryPath ?? "(default .NET probing)";
Console.WriteLine($"loaded {loaded}");
if (!loaded.Contains($"{Path.DirectorySeparatorChar}runtimes{Path.DirectorySeparatorChar}"))
    throw new InvalidOperationException($"Expected the library from the package's runtimes folder, got {loaded}");
Console.WriteLine($"GetAppVersion -> {version}");
