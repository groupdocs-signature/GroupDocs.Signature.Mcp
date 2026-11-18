---
id: 010
date: 2026-11-17
type: breaking
---

# Text search returns only signatures, archives report what was signed, QR payloads are verified

## What changed

- **`search_text_signatures` no longer returns document body text.** It now sets
  `TextSearchOptions.SkipExternal`, so a signed PPTX returns the signature instead of the signature plus a
  paragraph of slide text. New `includeDocumentContent` parameter restores the old result. `verify` does the
  same for its text branch, without a parameter.
- **Archives are handled end to end.** `SignTool` kept the `SignResult` it was discarding and now lists each
  document inside the archive with its signature count. The five `search_*` tools and `verify` look inside
  archives instead of past them: they use `Search(List<SearchOptions>)` and walk the
  `DocumentResultSignature` per contained document. Searching a signed ZIP returned nothing at all before.
  Archive responses carry `archive: true` and a `documents` array; ordinary documents are unchanged.
- **QR payloads are verified instead of guessed at.** The interim predicate that refused payloads mixing CJK
  with an em dash or check mark is replaced by reading the QR back out of the signed document and comparing
  it to what was asked for. Exact, where the predicate was wrong in both directions.
- **The server warns at startup** when `GROUPDOCS_MCP_OUTPUT_PATH` is not inside
  `GROUPDOCS_MCP_STORAGE_PATH`, and `README.md` and `llms.txt` now state the constraint.

## Why

### Search noise

`SignatureImplementation` cannot separate a signature from body text - both report `Native`. Neither can
`BaseSignature.IsSignature`, which is `true` for both. `SkipExternal` is the only property that works;
measured on a signed PPTX it keeps the signature and drops the body text, and changes nothing on DOCX or
XLSX. It is deliberately not applied to QR code and barcode searches, where finding a code that was already
in the document is a normal request.

### QR verification

The predicate was built on the reported trigger "CJK together with U+2014 or U+2714". Measuring the engine
directly showed that is wrong:

| Payload | Result |
|---|---|
| `— 数字签名`, `€ 数字签名`, `• 数字签名`, `– 数字签名` | corrupted |
| `数字签名 —` - the same symbol after the CJK | intact |
| `→ 数字签名` | intact |
| Cyrillic or accented text before CJK | intact |

So the symbol must *precede* the CJK, and the set is wider than the two characters guarded on - the old
predicate passed `€ 数字签名`, which corrupts, and refused `数字签名 —`, which does not. Reading the code
back and comparing needs no rule at all.

Verification runs **only when licensed**: without a licence the barcode reader truncates what it returns and
appends an evaluation notice, so every payload would look corrupted.

### Output folder

Files are written to the output folder but read back only from the storage folder, so when the two are not
nested the server writes documents it can never read. Docker works because its compose file nests them. The
real fix belongs in `GroupDocs.Mcp.Local.Storage`; this release makes the misconfiguration loud and
documents the constraint.

## Migration / impact

- **Breaking: `search_text_signatures` returns fewer results on Office formats.** What it drops was never a
  signature. Pass `includeDocumentContent: true` for the old behaviour.
- **Breaking: `verify` with `type: "text"` likewise.** No parameter there.
- Archive signing output gained lines; the saved file is unchanged.
- **New response shape for archives only**: `archive: true` plus a `documents` array of
  `{ fileName, found, signatures }`. `verify` entries gain a `document` field, null outside archives.
  Ordinary documents keep exactly the shape they had.
- QR signing of non-ASCII payloads costs one extra read of the signed document, licensed only.
- Known gap: the underlying QR encoder defect is unfixed. See the engine report in the ticket - the fix is
  Aspose.BarCode's, it is ILMerged into the engine, and it cannot be upgraded independently.
