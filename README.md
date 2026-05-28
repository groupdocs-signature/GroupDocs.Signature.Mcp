# GroupDocs.Signature MCP Server

MCP server that exposes [GroupDocs.Signature](https://products.groupdocs.com/signature) as AI-callable tools
for Claude, Cursor, GitHub Copilot, and other MCP agents.

## Installation

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

**Run directly with `dnx` (recommended — no install step):**

```bash
dnx GroupDocs.Signature.Mcp --yes
```

Pulls the latest stable release on every invocation. To pin to a specific
version (recommended for shared configs and CI), append `@<version>`:

```bash
dnx GroupDocs.Signature.Mcp@26.5.0 --yes
```

**Or install as a global dotnet tool:**

```bash
dotnet tool install -g GroupDocs.Signature.Mcp
groupdocs-signature-mcp
```

**Or run via Docker:**

```bash
docker run --rm -i \
  -v $(pwd)/documents:/data \
  ghcr.io/groupdocs-signature/signature-net-mcp:latest
```

## Native prerequisites

The underlying GroupDocs engine renders signature glyphs (text, QR codes,
barcodes) onto document pages via `System.Drawing` (GDI+). When you run the
server **natively** (via `dnx` or the global dotnet tool) on Linux or macOS,
install the native `libgdiplus` library and a fonts package first:

| Platform | Setup |
|---|---|
| Windows | Nothing — GDI+ is built into the OS. |
| Linux | `sudo apt-get install -y libgdiplus libfontconfig1 ttf-mscorefonts-installer` |
| macOS | `brew install mono-libgdiplus` |
| Docker | Nothing — the image already bundles libgdiplus, libfontconfig1, and ttf-mscorefonts-installer. |

Skipping this on Linux/macOS surfaces as `DllNotFoundException: libgdiplus` in
the tool response. The simplest zero-setup option on Linux/macOS is the
**Docker image**.

## Available MCP Tools

| Tool | Description |
|---|---|
| `Sign` | Sign a document with a text, QR code, barcode, or digital certificate signature; saves the signed file as `<name>_signed.<ext>` |
| `Verify` | Verify signatures in a document (text, QR code, barcode, digital, or all) and return a validity report |
| `SearchTextSignatures` | Find embedded text signatures (stamps, labels, native text annotations) with optional substring filter |
| `SearchBarcodes` | Find barcode signatures (Code39, Code128, EAN, etc.) with optional decoded-text filter and optional inline image |
| `SearchQrCodes` | Find QR code signatures with optional decoded-text filter and optional inline image |
| `SearchDigitalSignatures` | Find digital certificate signatures and return signer, issuer, serial number, validity status |
| `SearchImageSignatures` | Find embedded image signatures (logos, stamp images, picture overlays) and return them as base64 PNGs |
| `GetDocumentInfo` | Return file type, page count, size, and per-page dimensions as JSON (no modification) |

## Example prompts

- "Sign contract.pdf with a QR code containing 'Signed by Alice'"
- "Verify all signatures in agreement.docx and tell me if any failed"
- "Search invoice.pdf for QR codes — return the decoded text and the QR images"
- "Are there any digital signatures in this report.pdf? If so, who signed it?"
- "Add a text signature 'APPROVED' to the bottom-right of every page of policy.pdf"

## Configuration

| Variable | Description | Default |
|---|---|---|
| `GROUPDOCS_MCP_STORAGE_PATH` | Base folder for input and output files | current directory |
| `GROUPDOCS_MCP_OUTPUT_PATH` | *(Optional)* separate folder for output files | `GROUPDOCS_MCP_STORAGE_PATH` |
| `GROUPDOCS_LICENSE_PATH` | Path to GroupDocs license file | (evaluation mode) |

## Usage with Claude Desktop

```json
{
  "mcpServers": {
    "groupdocs-signature": {
      "type": "stdio",
      "command": "dnx",
      "args": ["GroupDocs.Signature.Mcp", "--yes"],
      "env": {
        "GROUPDOCS_MCP_STORAGE_PATH": "/path/to/documents"
      }
    }
  }
}
```

> To pin to a specific version, replace `"GroupDocs.Signature.Mcp"` with
> `"GroupDocs.Signature.Mcp@26.5.0"` in `args`. Pinning is recommended for
> shared / committed configs to avoid surprise upgrades.

## Usage with VS Code / GitHub Copilot

NuGet.org generates a ready-to-use `mcp.json` snippet on the [package page](https://www.nuget.org/packages/GroupDocs.Signature.Mcp).
Copy it directly into your `.vscode/mcp.json`.

Alternatively, add manually to `.vscode/mcp.json`:

```json
{
  "inputs": [
    {
      "type": "promptString",
      "id": "storage_path",
      "description": "Base folder for input and output files.",
      "password": false
    }
  ],
  "servers": {
    "groupdocs-signature": {
      "type": "stdio",
      "command": "dnx",
      "args": ["GroupDocs.Signature.Mcp", "--yes"],
      "env": {
        "GROUPDOCS_MCP_STORAGE_PATH": "${input:storage_path}"
      }
    }
  }
}
```

> Same pinning rule as above — swap `"GroupDocs.Signature.Mcp"` for
> `"GroupDocs.Signature.Mcp@26.5.0"` to lock to a specific release.

## Usage with Docker Compose

```bash
cd docker
docker compose up
```

Edit `docker/docker-compose.yml` to point volumes at your local documents folder.

## License

MIT — see [LICENSE](LICENSE)

<!-- mcp-name: io.github.groupdocs-signature/groupdocs-signature-mcp -->
