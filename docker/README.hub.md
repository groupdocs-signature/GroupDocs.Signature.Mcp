# GroupDocs.Signature MCP Server

MCP server that exposes [GroupDocs.Signature](https://products.groupdocs.com/signature) as AI-callable tools for Claude, Cursor, GitHub Copilot, and other MCP agents.

## Quick start

```bash
docker run --rm -i \
  -v $(pwd)/documents:/data \
  groupdocs/signature-net-mcp:latest
```

## Use with Claude Desktop

```json
{
  "mcpServers": {
    "groupdocs-signature": {
      "command": "docker",
      "args": ["run", "--rm", "-i", "-v", "/path/to/documents:/data", "groupdocs/signature-net-mcp:latest"]
    }
  }
}
```

## Tools

- **Sign** — Sign a document with a text, QR code, barcode, or digital certificate signature; saves the signed file as `<name>_signed.<ext>`
- **Verify** — Verify signatures in a document (text, QR code, barcode, digital, or all) and return a validity report
- **SearchTextSignatures** — Find embedded text signatures (stamps, labels, native text annotations) with optional substring filter
- **SearchBarcodes** — Find barcode signatures (Code39, Code128, EAN, etc.) with optional decoded-text filter and optional inline image
- **SearchQrCodes** — Find QR code signatures with optional decoded-text filter and optional inline image
- **SearchDigitalSignatures** — Find digital certificate signatures and return signer, issuer, serial number, validity status
- **SearchImageSignatures** — Find embedded image signatures (logos, stamp images, picture overlays) and return them as base64 PNGs
- **GetDocumentInfo** — Return file type, page count, size, and per-page dimensions as JSON (no modification)

## Tags & environment

- Tags: `latest` + an immutable version tag per release matching NuGet (e.g. `26.7.1`).
  Platforms: `linux/amd64`, `linux/arm64`. Also on GHCR: `ghcr.io/groupdocs-signature/signature-net-mcp`.
- `GROUPDOCS_MCP_STORAGE_PATH` (default `/data`), `GROUPDOCS_MCP_OUTPUT_PATH` (optional),
  `GROUPDOCS_LICENSE_PATH` — mount your license and point at it to leave evaluation mode
  (see the Licensing section in the GitHub README for the exact evaluation limits).

Full docs, one-click installs for other clients, and licensing details:
**https://github.com/groupdocs-signature/GroupDocs.Signature.Mcp**
