# Codex CLI (OpenAI)

```bash
codex mcp add groupdocs-signature -- dnx GroupDocs.Signature.Mcp --yes
```

Or add to `~/.codex/config.toml`:

```toml
[mcp_servers.groupdocs-signature]
command = "dnx"
args = ["GroupDocs.Signature.Mcp", "--yes"]

[mcp_servers.groupdocs-signature.env]
GROUPDOCS_MCP_STORAGE_PATH = "/path/to/documents"
# GROUPDOCS_LICENSE_PATH = "/path/to/GroupDocs.Total.lic"   # omit for evaluation mode
```

Pin a version by replacing `GroupDocs.Signature.Mcp` with `GroupDocs.Signature.Mcp@26.7.1`.
