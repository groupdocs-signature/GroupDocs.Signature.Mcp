using System.Reflection;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var version = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion
    ?.Split('+')[0]
    ?? "0.0.0";

WarnIfOutputIsOutsideStorage();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services
    .AddGroupDocsMcp()
    .AddLocalStorage("./Files");
builder.Services.AddSingleton<ILicenseManager, SignatureLicenseManager>();
builder.Services
    .AddMcpServer(options => { options.ServerInfo = new() { Name = "GroupDocs.Signature.Mcp", Version = version }; })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();

// Files are written to the output folder but read back only from the storage folder. When output sits
// outside storage the server can write documents it can never read, so sign-then-verify silently fails.
// Written to stderr: stdout carries the JSON-RPC traffic.
static void WarnIfOutputIsOutsideStorage()
{
    var storage = Environment.GetEnvironmentVariable("GROUPDOCS_MCP_STORAGE_PATH");
    var output = Environment.GetEnvironmentVariable("GROUPDOCS_MCP_OUTPUT_PATH");
    if (string.IsNullOrWhiteSpace(output)) return;

    var storageRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(storage) ? "./Files" : storage);
    var outputRoot = Path.GetFullPath(output);
    var prefix = storageRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    if (outputRoot.Equals(storageRoot, StringComparison.OrdinalIgnoreCase) ||
        outputRoot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return;

    Console.Error.WriteLine(
        $"WARNING: GROUPDOCS_MCP_OUTPUT_PATH ('{outputRoot}') is not inside GROUPDOCS_MCP_STORAGE_PATH " +
        $"('{storageRoot}'). Signed files will be written where this server cannot read them back, so " +
        "signing then verifying or searching the result will fail with 'not found in storage'. " +
        "Set the output folder to a subdirectory of the storage folder.");
}
