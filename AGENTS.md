# AGENTS.md — Guide for AI coding agents

Brief orientation for AI coding agents (Claude Code, Copilot, Cursor, Aider, Amp, Codex) working in this repository.

## What this repo is

A standalone **MCP server** for [GroupDocs.Signature for .NET](https://products.groupdocs.com/signature) — exposes document signing, signature search, and signature verification operations as AI-callable tools via the Model Context Protocol.

Published to NuGet as `GroupDocs.Signature.Mcp` with the `McpServer` package type, and to `ghcr.io/groupdocs-signature/signature-net-mcp` + `docker.io/groupdocs/signature-net-mcp` as a container image.

## MCP tools exposed

| Tool | Description |
|---|---|
| `Sign` | Signs a document (text / QR code / barcode / digital certificate); saves the signed file as `<name>_signed.<ext>` |
| `Verify` | Verifies signatures (text / QR / barcode / digital / all) and returns a validity report as JSON |
| `SearchTextSignatures` | Finds embedded text signatures with optional substring filter |
| `SearchBarcodes` | Finds barcode signatures with optional decoded-text filter; can return inline barcode images |
| `SearchQrCodes` | Finds QR code signatures with optional decoded-text filter; can return inline QR images |
| `SearchDigitalSignatures` | Finds digital certificate signatures and returns signer, issuer, validity, serial number |
| `SearchImageSignatures` | Finds embedded image signatures (logos, stamps, picture overlays) and returns them as base64 PNGs |
| `GetDocumentInfo` | Returns file type, page count, size, and per-page dimensions as JSON (read-only) |

All tools accept `FileInput` (resolved via `IFileResolver`) and an optional `password` for protected documents.

## Folder layout

```
src/                                                       ← all projects + sln + Directory.Build.props
  GroupDocs.Signature.Mcp/
    Program.cs                                             ← host bootstrap + stdio transport
    SignatureLicenseManager.cs                             ← applies GroupDocs.Total license
    Tools/
      SignTool.cs                                          ← [McpServerTool] — Sign
      VerifyTool.cs                                        ← [McpServerTool] — Verify
      SearchTextSignaturesTool.cs                          ← [McpServerTool] — SearchTextSignatures
      SearchBarcodesTool.cs                                ← [McpServerTool] — SearchBarcodes
      SearchQrCodesTool.cs                                 ← [McpServerTool] — SearchQrCodes
      SearchDigitalSignaturesTool.cs                       ← [McpServerTool] — SearchDigitalSignatures
      SearchImageSignaturesTool.cs                         ← [McpServerTool] — SearchImageSignatures
      GetDocumentInfoTool.cs                               ← [McpServerTool] — GetDocumentInfo
    .mcp/
      server.json                                          ← NuGet.org reads this to generate mcp.json snippet
    GroupDocs.Signature.Mcp.csproj                         ← PackageType=McpServer + ToolCommandName
  GroupDocs.Signature.Mcp.Tests/                           ← xUnit + Moq unit tests
  GroupDocs.Signature.Mcp.sln
  Directory.Build.props
build/
  dependencies.props                                       ← single source of truth for all versions
changelog/                                                 ← one MD file per change (see changelog/README.md)
docker/
  Dockerfile                                               ← multi-stage, runtime on aspnet:10.0
  docker-compose.yml
.github/workflows/                                         ← build_packages.yml, run_tests.yml, publish_prod.yml, publish_docker.yml
```

## Dependencies

- `GroupDocs.Mcp.Core` + `GroupDocs.Mcp.Local.Storage` — infrastructure NuGet packages from the [GroupDocs.Mcp.Core](https://github.com/groupdocs/GroupDocs.Mcp.Core) repo
- `GroupDocs.Signature` — the actual signing/verification engine
- `ModelContextProtocol` — MCP SDK for .NET
- `Microsoft.Extensions.Hosting` — host builder for the stdio server
- `System.Security.Cryptography.Pkcs` — explicit pin overriding the engine's transitive 7.0.0 (CVE GHSA-555c-2p6r-68mm). Re-evaluate when the engine bumps its transitive.

## Commands you can run

```bash
# Restore + build
dotnet restore
dotnet build src/GroupDocs.Signature.Mcp.sln -c Release

# Run unit tests
dotnet test src/GroupDocs.Signature.Mcp.sln -c Release

# Run the server locally (stdio)
dotnet run --project src/GroupDocs.Signature.Mcp

# Local pack (writes to ./build_out) — validates server.json version matches dependencies.props
pwsh ./build.ps1

# Build + run the Docker image
docker build -f docker/Dockerfile -t signature-net-mcp:local .
docker run --rm -i -v $(pwd)/documents:/data signature-net-mcp:local
```

## Version scheme

CalVer `YY.MM.N`. The version lives in **two** places that MUST stay in lockstep:
1. `build/dependencies.props` → `<GroupDocsSignatureMcp>`
2. `src/GroupDocs.Signature.Mcp/.mcp/server.json` → both top-level `"version"` and `packages[0].version`

`build.ps1` enforces this at pack time (`Assert-ServerJsonVersionMatchesDependencies`) — if they drift, the build fails.

## Pre-shipped pitfall remediations

The following cross-product pitfalls were addressed at clone time and are already in the codebase:

- **Pitfall #18 (unhandled exceptions in tool methods)**: all 8 tools wrap their engine calls in a top-level `try { … } catch (Exception ex) { return FormatException(ex, …); }` block. Engine failures surface as a descriptive `"<Op> failed for '<file>': <type>: <msg>"` string instead of MCP's opaque `"An error occurred invoking '<tool>'"`. Do not remove these wrappers.
- **Pitfall #16 (JSON via TruncateText)**: tools that return JSON (`Verify`, `Search*`, `GetDocumentInfo`) call `JsonSerializer.Serialize(...)` directly and never pipe their JSON through `OutputHelper.TruncateText`. The truncation marker is plain text and breaks JSON parsing.

## Native-deps note

On Linux, `System.Drawing.Common` (used by the engine to render text / QR / barcode signature glyphs onto document pages) requires `libgdiplus` + `libfontconfig1`. Because the engine rasterizes text glyphs, `ttf-mscorefonts-installer` is also installed (with the debconf EULA accept + `fc-cache`) so Arial / Times New Roman etc. are discoverable. The `System.Drawing.EnableUnixSupport` runtime host config option is set in the csproj. `SkiaSharp.NativeAssets.Linux.NoDependencies` is intentionally NOT pinned — neither the framework subproject nor the upstream engine declares a direct SkiaSharp need at clone time.

## House rules

1. **Tools must have rich `[Description("...")]` strings** — these are what AI agents read via the MCP protocol. Write them as task-oriented sentences, not method-signature summaries. Always include: supported formats, response shape, failure prefix.
2. **Never add new env vars beyond** `GROUPDOCS_MCP_STORAGE_PATH`, `GROUPDOCS_MCP_OUTPUT_PATH`, `GROUPDOCS_LICENSE_PATH` without updating `server.json`, `docker-compose.yml`, and `README.md` together.
3. **Tests use xUnit + Moq** — mock `IFileResolver`, `IFileStorage`, `ILicenseManager`, `OutputHelper`.
4. **Changelog entries required** — any PR that changes behaviour adds `changelog/NNN-slug.md`.
5. **Do not edit `obj/` or `build_out/`** — build artifacts.
6. **Target framework is `net10.0` only** — required by `dnx` and the MCP SDK.

## Release flow

See [RELEASE.md](RELEASE.md) for the exact per-release checklist.

## What NOT to change

- Do not hardcode the version in `.csproj` — it flows from `$(GroupDocsSignatureMcp)` in `dependencies.props`.
- Do not remove the `<PackageType>McpServer</PackageType>` or `<ToolCommandName>groupdocs-signature-mcp</ToolCommandName>` from the csproj — NuGet.org discoverability and `dnx` invocation depend on them.
- Do not change the `.mcp/server.json` schema URL without cross-checking with the NuGet MCP docs.
