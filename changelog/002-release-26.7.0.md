---
id: 002
date: 2026-07-15
version: 26.7.0
type: maintenance
---

# Release 26.7.0 — engine bump to 26.6.0 + shared error formatter

## What changed

- Bumped the MCP package version `GroupDocs.Signature.Mcp` `26.5.0` → `26.7.0` (CalVer).
- Bumped the engine `GroupDocs.Signature` `26.2.0` → `26.6.0` (latest stable on NuGet;
  a four-minor jump). No public API drift affecting the tool surface — `Sign`,
  `Verify`, all five `Search*` tools, and `GetDocumentInfo` compile and behave
  unchanged against 26.6.0.
- Extracted the per-tool private `FormatException` helpers (duplicated across all
  eight tools) into a single shared `Tools/ToolError.cs`
  (`ToolError.Format(op, file, ex, subjectSuffix = null)`). Every tool's `catch`
  now calls it. Output text is byte-for-byte identical to the previous per-tool
  helpers — the failure prefixes (`Signing failed for`, `Verification failed for`,
  `Barcode search failed for`, `Document-info lookup failed for`, etc.) are
  preserved, so integration tests that match on the prefix are unaffected.
  `Sign` and `Verify` pass their `(type: '<type>')` subject via the new
  `subjectSuffix` parameter.

## Why

Routine maintenance release: pick up the latest stable engine and consolidate the
duplicated error-formatting boilerplate (8 identical copies) into one shared helper
per the cross-product MCP conventions (shared `ToolError` supersedes per-class
`FormatException` once ≥3 tools exist).

## Migration / impact

- No behavioural change to any tool's success or failure output.
- Engine transitive note: as of 26.6.0 the resolved `GroupDocs.Signature.Net80`
  runtime package now declares `SkiaSharp 3.119.0` +
  `SkiaSharp.NativeAssets.Linux.NoDependencies 3.119.0` transitively (it did not at
  clone time). The MCP still does not pin SkiaSharp manually — the native
  `libSkiaSharp` now arrives transitively (Pitfall S). The existing
  `System.Security.Cryptography.Pkcs 9.0.0` override remains in place.
