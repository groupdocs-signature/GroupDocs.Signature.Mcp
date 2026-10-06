---
id: 007
date: 2026-10-06
type: breaking
---

# `verify` reports what is really in the document, and `sign` stops inventing signature text

## What changed

### `verify` no longer reports meaningless counts

`verify` used to answer `isValid: true, succeeded: 0, failed: 0` for a PDF that
`search_digital_signatures` found a signature in. Three separate reasons, all in the engine API it was
built on:

- `Signature.Verify` answers "does this document contain the signature I describe?" - it is not an
  enumeration API. `Succeeded` can only ever contain signatures the caller already specified.
- For **PDF only**, the engine adds a digital signature to `Succeeded` inside its certificate-comparison
  branch, so a call without the signer's `.pfx` records nothing however many signatures exist. Word, Excel,
  PowerPoint and image formats add it unconditionally - the same call reported `1` on a signed DOCX and `0`
  on a signed PDF.
- `VerificationResult.Failed` is never populated anywhere in the engine ("Currently this property is not
  supported" in its own API docs), so `failed` was structurally always `0`.

The tool is now built on `Search`, which enumerates what is actually in the document and runs the same
per-signature cryptographic check for digital signatures. The response is:

- `found` - how many signatures matched
- `signatures` - one entry each, tagged with its `type`; digital entries carry `isValid`, `signTime` and
  the full `certificate` block including `isExpired`, `isNotYetValid` and `validWhenSigned`
- `isValid` - true only when every digital signature passed, and **`null`** when the document has no
  digital signature, because text, QR code and barcode signatures carry nothing to verify
- `certificateWarnings` - as added in 006, and now actually reachable

`succeeded` and `failed` are gone. A new optional `text` parameter filters text, QR code and barcode
results; previously those types verified with no criterion at all and therefore always answered invalid.

### `sign` rejects a missing signature text

With `type` text, qrcode or barcode and no `text`, the tool used to sign with the literal string `"Signed"`,
save the file, and report success indistinguishably from a real signing - for QR code and barcode, a
scannable code whose payload was the word "Signed". It now returns
`A text signature requires text. Provide the 'text' parameter.` and writes nothing. Whitespace-only text is
rejected the same way; it previously signed an invisible mark.

Argument checks (type, text) moved ahead of the document copy and open, so a call that cannot succeed no
longer does that work first. An unknown `type` still reports itself as an unknown type rather than
complaining about the missing text.

## Why

Both tools returned success-shaped answers that were not true: `verify` said a document was valid while
reporting that it had checked nothing, and `sign` produced a signed document carrying a mark nobody asked
for. Neither failure is visible to a caller without opening the result.

## Migration / impact

- **Breaking: the `verify` response shape changed.** `succeeded` and `failed` are removed; `found`,
  `signatures` and a nullable `isValid` replace them. Anything reading those two fields must be updated -
  note they only ever carried `0` for PDF digital verification.
- **Breaking: `sign` without `text`** now fails for text, qrcode and barcode. The behaviour it replaces -
  signing the word "Signed" - has no legitimate use, so callers hitting this were getting a wrong result
  already.
- The companion integration-test repo may assert on the old `verify` fields and on signing without text; it
  needs a matching change.
- No change to `search_*`, `get_document_info`, or digital signing with a certificate.
