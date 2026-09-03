# Backlog & Known Issues

Running list of ideas, planned work, and known limitations for the
GroupDocs.Signature MCP server. Grouped by topic. Terse on purpose — each line is
a ticket, not an essay. `[ ]` = open, `[x]` = shipped (kept for context).

**Current surface (26.9.0):** `sign`, `verify`, `search_text_signatures`, `search_qr_codes`,
`search_barcodes`, `search_digital_signatures`, `search_image_signatures`, `get_document_info`.

> **This product's work is tracked in Redmine.** Epic
> [SIGNATURENET-5892](https://issue.saltov.dynabic.com/issues/SIGNATURENET-5892) carries 14 child
> tickets (`SIG-MCP-1` … `SIG-MCP-14`, ids 420600–420613), split across release 26.8 and a later
> release. **The tickets are the source of truth for scope and acceptance criteria** — this file
> is the in-repo index and does not restate them.

---

## Confirmed defects — external audit, 2026-08-16

Source: the Signature + Metadata deep round (202 logged JSON-RPC pairs across 19 server sessions,
Docker 26.7.2 vs NuGet 26.7.0, licensed, library baseline 26.6.0) plus the 12-product sweep. A
later validation round found **zero false positives**; several findings were confirmed in this
repo's own source.

`S#` = shared core (`GroupDocs.Mcp.Core`) · `M#` = this repo · `P#` = GroupDocs.Signature library

**Verdict: core signing and the entire search family are solid.** The defects concentrate in the
input contract, validation honesty, result truthfulness and image packaging. **Three are silent
data-integrity hazards.**

### Release 26.8 — fix first

- [ ] **SIG-MCP-1** (420600) · shared `FileInput` contract — **High**. `fileName` crashes every
      tool (`S1`); missing files return an opaque error (`S2`). Lands via `GroupDocs.Mcp.Core`;
      **scope is all 12 products, not just Signature.** Do this first — cheapest change, unblocks
      every agent-facing call.
- [ ] **SIG-MCP-2** (420601) · flag expired certificates on sign, search and verify — **High**.
      A cert with `validTo` in 2017 signs with no warning, and both `search_digital_signatures` and
      `verify` then report `isValid: true`. Source confirms the code only copies
      `NotBefore`/`NotAfter` and never compares (`SearchDigitalSignaturesTool.cs:64-65`).
- [ ] **SIG-MCP-3** (420602) · reject `sign` calls with omitted `text` — **Normal**. Today it
      silently signs the default payload `"Signed"` and reports success — a typo produces a legally
      meaningless signed artifact the agent believes carries the intended text.
- [ ] **SIG-MCP-4** (420603) · CJK-capable fonts in the Docker image — **High**. Any CJK text
      signature on PDF fails with `Font SimSun was not found`; the identical call succeeds on
      NuGet/Windows. Dockerfile-level, no application code.
- [ ] **SIG-MCP-5** (420604) · make the NuGet channel resolve its output folder — **High**. On
      NuGet no `filePath` form resolves a file in the output folder, so **sign → verify is
      impossible there** while working on Docker. Depends on SIG-MCP-1 (same resolver).
- [ ] **SIG-MCP-6** (420605) · available-files listing for missing documents — **Normal**. Largely
      falls out of SIG-MCP-1; kept separate so the inconsistency is verified closed, not assumed.
- [ ] **SIG-MCP-7** (420606) · interim guard against corrupt QR payloads — **High**. Must be
      cleanly removable when SIG-MCP-8 lands.

### Release 2

- [ ] **SIG-MCP-8** (420607) · root-cause and fix QR payload corruption — **High, data
      integrity**. Payloads mixing CJK with U+2014 or U+2714 are silently corrupted — each CJK
      character collapses to its low byte, `数字` becomes `pW` — while both `sign` and
      `search_qr_codes` report success. Reproduced 4× across 3 PDFs on **both** channels, ruling
      out packaging. Hypothesis: QR encoding-mode segmentation mishandling a Kanji/Byte transition.
      **The worst finding in the suite** — a human scanning that QR gets wrong data and nothing
      signals a problem.
- [ ] **SIG-MCP-9** (420608) · rework `verify` — **High**. Contradictory output (`isValid: true`
      with `succeeded: 0`); fresh signatures fail their own type; `type=all` fails wholesale on
      mixed documents. The library already has the right semantics
      (`TextVerifyOptions`/`MatchType`); the wrapper does not expose them
      (`VerifyTool.cs:67-76` sets no criteria). An API design task — agree the response schema
      first, and coordinate with SIG-MCP-2 so the expired state is representable.
- [ ] **SIG-MCP-10** (420609) · filter text-search noise on Office formats — **Normal**. A PPTX
      with one real signature returns 17 hits (page placeholders, WordArt).
- [ ] **SIG-MCP-11** (420610) · return `Reason` / `Location` / `Contact` from digital search —
      **Low**. The wrapper prints the base `DigitalSignature` type instead of the derived
      `PdfDigitalSignature`. Ships with SIG-MCP-13 or the round-trip stays untestable.
- [ ] **SIG-MCP-12** (420611) · handle ZIP honestly — **Low**. Signing a ZIP reports success but no
      `search_*` call finds the signature. Support it properly, or refuse it explicitly.
- [ ] **SIG-MCP-13** (420612) · add `reason` / `location` / `contact` to digital sign — **Feature,
      Low**. Pairs with SIG-MCP-11.
- [ ] **SIG-MCP-14** (420613) · document the `page: 0` convention — **Support, Low**. Signatures in
      headers/footers report `page: 0`; deliberate, documented nowhere.

### Not yet ticketed

- [ ] **P5** Text signatures do not round-trip byte-exactly on PDF — **Low**. Found in the audit;
      **no Redmine ticket exists.** File one or record a decision to accept it. **P2**

---

## Known issues & limitations

- Core signing and all five `search_*` families work correctly on the happy path.
- `verify` is **currently unusable for any decision** (SIG-MCP-9) — do not build workflows on it
  until that lands.
- The two channels are documented as interchangeable but are not: CJK fails on Docker
  (SIG-MCP-4), and output-folder resolution fails on NuGet (SIG-MCP-5).
- Output collisions dedup to `' (N)'` — the family convention.
- **Capability gaps** (library features with no MCP surface, unscheduled): appearance/page
  parameters on `sign`; the `image` sign type; `generate_preview`; `delete_signatures`; metadata
  sign/search; `encodeType` for barcode/QR (currently hardcoded Code128/QR), output `fileName`,
  atomic multi-signature. Items 1–3 sit on the rendering path and are **blocked on the
  System.Drawing analysis and replacement decision** — designing those APIs first risks committing
  to types (`Color`, `Font`, sizing) the replacement will change.

---

## Testing & CI

The audit's validation round showed this repo's suites cannot see these defects:

- [ ] **The suite has no digital-signing test at all** — which is why SIG-MCP-2 (expired certs)
      shipped. **P1**
- [ ] `verify` tests assert JSON **shape only, not meaning** — they observed the broken
      `isValid:false / succeeded:0 / failed:0` output live and passed. Assert semantics. **P1**
- [ ] No unicode payload appears in any test — SIG-MCP-8's corruption is invisible. Add mixed
      CJK + U+2014/U+2714 round-trip fixtures. **P1**
- [ ] Tighten the error oracles: they accept `IsError || contains(...)`, so they **pass on the
      exact defect** reported. Assert the promised `Available files:` text. **P1**
- [ ] Add the two mandatory probes: the **`fileName`-only form**, and a **missing file**. **P1**
- [ ] Add a `channel: [dnx, docker]` axis — SIG-MCP-4 is Docker-only and SIG-MCP-5 is NuGet-only;
      **neither can be caught by a single-channel matrix**. This product is the clearest argument
      for the change. **P1**
- [ ] Per-tool Linux smoke test in image CI. **P1**
- [ ] macOS integration leg hangs (family-wide) — `timeout-minutes: 20` is committed locally but
      unpushed here. Push it, and stream the `dnx` child's stderr to an uploaded file. **P1**

> Note: this repo's Tests clone currently has uncommitted local changes to `ErrorHandlingTests.cs`,
> `McpServerFixture.cs`, `GetDocumentInfoTests.cs`, `SearchBarcodesTests.cs`,
> `SearchDigitalSignaturesTests.cs` and a new `TestParallelization.cs`. Reconcile before starting.

## Documentation & discoverability

- [ ] Document the channel differences honestly until SIG-MCP-4/5 land. **P1**
- [ ] Document the `page: 0` convention (SIG-MCP-14). **P2**
- [ ] Licensing section covering the metered option once it ships. **P1**

## Platform & infra (longer-term)

- [ ] Metered licensing (`GROUPDOCS_METERED_PUBLIC_KEY` / `_PRIVATE_KEY`) via
      `GroupDocs.Mcp.Core`, plus the `get_license_status` tool. **P1**
- [ ] System.Drawing replacement decision — blocks the three rendering-path capability gaps. **P1**
- [ ] HTTP/SSE transport for shared/team deploys (stdio stays default). **P2**

---

*Evidence: `TEMP_ThirdPartyAnalysis/signature.md`, `TEST-REPORT.md` (the deep round the epic cites
section-by-section), `VALIDATION-REPORT.md` (why the green suites miss these). Conventions: any
behaviour change ships with a `changelog/NNN-*.md` entry and a CalVer bump. Integration tests
target the published NuGet via `dnx`, so new-tool tests only pass once the matching version is
live.*
