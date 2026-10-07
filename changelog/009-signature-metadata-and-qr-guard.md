---
id: 009
date: 2026-10-07
type: feature
---

# Reason, location and contact on digital signatures; page convention documented; interim QR payload guard

## What changed

- **`sign` gained `reason`, `location` and `contact` parameters**, mapped to `DigitalSignOptions.Reason`,
  `.Location` and `.Contact`. Type `digital` only.
- **`search_digital_signatures` returns `reason`, `location` and `contact`.** The values were already being
  produced by the engine but dropped: they live on the `PdfDigitalSignature` subclass, and the tool read only
  the base `DigitalSignature`. The fix is a cast.
- **The page convention is documented** in all five tools that report one: `page` is 1-based, and `page: 0`
  means the signature sits outside the page body, in a header or a footer. The engine documents neither the
  convention nor the zero case, and it contradicts the sign side, where `SignOptions.PageNumber` has a
  minimum of 1.
- **`verify` now names that field `page`**, matching the four search tools. It previously emitted
  `pageNumber` for the same thing.
- **Interim guard against the QR encoder defect.** `sign` refuses a QR payload that mixes CJK characters with
  an em dash (U+2014) or a heavy check mark (U+2714). The engine encodes those to a symbol whose CJK
  characters are each replaced by their low byte - U+6570 reads back as `p`, U+5B57 as `W` - while the
  characters around them survive.

## Why

Reason, location and contact are the first thing a reader of a signed PDF looks for, and the tool was
discarding them. The page convention is invisible from the outside and an agent will read `page: 0` as "page
zero" rather than "header or footer".

The QR guard refuses rather than warns because the failure is silent and the result is plausible: the symbol
scans cleanly and hands the reader wrong data, so nothing downstream can catch it. A refused call is
recoverable; a corrupted signature is not noticed.

## Migration / impact

- All additions. `reason`, `location` and `contact` are optional on `sign` and new fields in the search
  response; nothing was renamed or removed there.
- **`verify` renamed `pageNumber` to `page`** in its signature entries. That field shipped in the previous
  release, so anything reading it must be updated.
- **Breaking for one input shape:** QR payloads matching the corruption trigger now fail where they
  previously produced a corrupted code. They never produced a correct one.
- Setting `reason`, `location` or `contact` **changes the visible signature** on PDF:
  `DigitalSignOptions.ShowProperties` defaults to true, and the appearance renders as
  `Digitally signed by {contact} Date: {date} Reason: {reason} Location: {location}`.
- These three are stored in PDF only. On Word, Excel and PowerPoint the values are accepted on signing and
  always come back null from search, which the tool descriptions state so null is not read as "no reason
  given".

## Removing the QR guard

`FindQrCorruptionTrigger` and `IsCjk` in `Tools/SignTool.cs`, their call site, and the two test theories in
`SignToolTests.cs` exist only until the engine's QR encoder is fixed. The trigger set is empirical, taken
from reproductions on both channels rather than from the encoder, so it both over- and under-matches. Delete
all of it when the engine fix ships.
