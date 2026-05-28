---
id: 001
date: 2026-05-29
version: 26.5.0
type: feature
---

# Initial public release of GroupDocs.Signature MCP Server

## What changed

- NuGet package `GroupDocs.Signature.Mcp` published with the `McpServer` package type.
- Eight MCP tools exposed:
  - `Sign` — signs a document with a text, QR code, barcode, or digital certificate signature and saves the signed file as `<name>_signed.<ext>`.
  - `Verify` — verifies signatures (text / QR / barcode / digital / all) and returns a validity report as JSON.
  - `SearchTextSignatures` — finds embedded text signatures with an optional substring filter.
  - `SearchBarcodes` — finds barcode signatures (Code39, Code128, EAN, etc.) with an optional decoded-text filter; can return inline barcode images.
  - `SearchQrCodes` — finds QR code signatures with an optional decoded-text filter; can return inline QR images.
  - `SearchDigitalSignatures` — finds digital certificate signatures and returns signer, issuer, serial number, and validity status.
  - `SearchImageSignatures` — finds embedded image signatures (logos, stamps, picture overlays) and returns them as base64 PNGs.
  - `GetDocumentInfo` — returns file type, page count, size, and per-page dimensions as JSON; read-only.
- The seven engine-touching tools (`Sign`, `Verify`, the five `Search*` tools) are ported from the GroupDocs.Signature framework subproject; a Pitfall #18 catch-and-format exception wrapper was added at clone time to each — engine failures surface as a descriptive `"<Op> failed for '<file>': <type>: <msg>"` string instead of MCP's opaque generic wrapper. `GetDocumentInfo` is added per Step 11 of the clone instructions; it serializes JSON directly (Pitfall #16) and uses reflection to extract `PageCount`/`Pages` across subtype-specific `IDocumentInfo` shapes.
- Installable via `dnx GroupDocs.Signature.Mcp@26.5.0 --yes` (.NET 10 SDK required), `dotnet tool install -g GroupDocs.Signature.Mcp`, or Docker.
- Docker image published to `ghcr.io/groupdocs-signature/signature-net-mcp` and `docker.io/groupdocs/signature-net-mcp`.
- Environment variables: `GROUPDOCS_MCP_STORAGE_PATH`, optional `GROUPDOCS_MCP_OUTPUT_PATH`, `GROUPDOCS_LICENSE_PATH`.
- Native dependencies on Linux: `libgdiplus` + `libfontconfig1` + `ttf-mscorefonts-installer` (the engine rasterizes signature glyphs — text, QR codes, barcodes, certificate annotations — onto document pages, so MS core fonts are required for correct rendering). `System.Drawing.EnableUnixSupport` is set in the csproj. `SkiaSharp.NativeAssets.Linux.NoDependencies` is intentionally **NOT** pinned — neither the framework subproject nor the upstream engine declares a direct SkiaSharp dependency at clone time; revisit if a runtime `libSkiaSharp.so` not found surfaces on Linux integration runs.
- The Pkcs CVE GHSA-555c-2p6r-68mm transitively pulled by `GroupDocs.Signature` (`System.Security.Cryptography.Pkcs 7.0.0`) is overridden by an explicit `<PackageReference>` to 9.0.0 in the csproj. Re-evaluate when the engine bumps its transitive.
- License is applied via Pattern A (the cross-product License-class style): `new GroupDocs.Signature.License().SetLicense(licensePath)` (the framework's `SignatureLicenseManager.cs` uses this verbatim — no caveats observed in the engine).

## Why

Exposes GroupDocs.Signature for .NET as AI-callable MCP tools for Claude, Cursor, VS Code / GitHub Copilot, and other MCP-compatible agents, so document signing, signature search, and verification can be invoked from natural-language instructions without bespoke per-agent integration.

## Migration / impact

First release — no migration required.
