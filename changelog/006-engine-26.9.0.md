---
id: 006
date: 2026-10-05
type: breaking
---

# Engine bump to 26.9.0 + certificate validity reported on sign, search and verify

## What changed

- Bumped the engine `GroupDocs.Signature` `26.6.0` -> `26.9.0`. No public API drift affecting the tool
  surface: `Sign`, `Verify`, all five `Search*` tools and `GetDocumentInfo` compile and behave unchanged.
- **`Sign` gained two optional parameters, `allowExpired` and `allowNotYetValid`** (both `bool`, default
  `false`), mapped to the new `DigitalSignOptions.AllowExpired` / `.AllowNotYetValid`. They apply to
  `type: "digital"` only; on text, QR code and barcode signatures there is no certificate, so they are
  ignored and the response is unchanged.
- When one of the flags is set and the certificate really is outside its validity period, the engine signs
  and logs a warning. That warning is now returned to the caller as a `Certificate warning: ...` line
  appended to the `Sign` response, next to the existing `[Evaluation mode]` prefix.
- **`search_digital_signatures` now judges the certificates it reports.** Each `certificate` object gained
  `isExpired` and `isNotYetValid` (its state right now) and `validWhenSigned` (whether the signature was
  made inside the validity period, `null` when the document records no sign time). The response gained a
  top-level `certificateWarnings` array. Previously the tool returned `validFrom` / `validTo` and left the
  caller to compare them, so an expired certificate read as a plain valid signature.
- **`verify` gained the same `certificateWarnings` array.** Its `isValid` covers the signatures themselves,
  not the certificate behind them, so a signature made with an expired certificate could come back valid
  here and still be rejected by Adobe Acrobat or Word.
- New `Tools/CertificateValidity.cs` holds the shared date logic, so both tools word their findings the
  same way the engine does. It deliberately distinguishes **expired now** from **expired when the signature
  was made**: a signature made while the certificate was valid stays valid after it expires, and reporting
  that as a problem would be a false alarm.
- Removed three transitive security pins from `GroupDocs.Signature.Mcp.csproj`, all of which existed only
  because of the 26.6.0 dependency graph:
  - `System.Security.Cryptography.Pkcs 9.0.0` - the engine now brings `10.0.11` itself, so the pin would
    have *downgraded* it;
  - `Microsoft.Bcl.Memory 9.0.18` - the `Microsoft.Extensions.DataIngestion` preview chain that pulled it
    is no longer referenced;
  - `Microsoft.NETCore.Jit 1.0.18` - `Microsoft.NETCore.Portable.Compatibility` is no longer referenced.

  `dotnet restore` is clean of NU1903 after the removal.
- Added `src/GroupDocs.Signature.Mcp.Tests/SignToolCertificateTests.cs` and the repo's first test fixture,
  `TestData/sample.pdf`. These tests let the resolver return real content and reach the engine, unlike the
  existing mock-only suite. Test certificates are generated in-process with `CertificateRequest`, so none
  is committed and none can expire. `CertificateValidityTests.cs` covers the date logic on its own, which
  needs `InternalsVisibleTo` for the test project.

## Why

In 26.9 the engine rejects a certificate outside its validity period by default. Before this change the
server signed with an expired certificate silently, which produces a signature that every validator reports
as not valid. Taking the engine's secure default without exposing the opt-in would have removed that
capability outright, with no way for a caller to ask for it back, so the two travel together.

## Migration / impact

- **Breaking for callers who sign with an expired or not-yet-valid certificate.** That call now fails and
  writes no file. The error names the certificate, the date and the property to set, for example:
  `The signing certificate expired on 2016-12-31 12:14 UTC (subject "CN=...", thumbprint ...). ... To sign
  anyway, set DigitalSignOptions.AllowExpired to true.` To keep the old behaviour, pass
  `allowExpired: true` (or `allowNotYetValid: true`) and expect a `Certificate warning:` line in the
  response.
- `search_digital_signatures` and `verify` responses gained fields; nothing was renamed or removed, so
  existing consumers are unaffected.
- **Known limitation:** `verify` currently reports `succeeded: 0, failed: 0` for a document that
  `search_digital_signatures` finds a signature in, because `BuildVerifyOptions` passes
  `DigitalVerifyOptions` with no criteria. Its `certificateWarnings` array is therefore always empty in
  practice. The flagging code is in place and will start reporting as soon as that separate defect is
  fixed.
- No change to any other tool's success or failure output.
- Engine transitive note: on `net10.0` the resolved runtime package is now `GroupDocs.Signature.Net100
  26.9.0` instead of the `Net80` fallback. `SkiaSharp 3.119.0` +
  `SkiaSharp.NativeAssets.Linux.NoDependencies 3.119.0` still arrive transitively and are still not pinned,
  so the Docker image's native package list is unchanged. `Aspose.Drawing.Common` moved `25.11.0` ->
  `26.8.0`.
