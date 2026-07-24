# Claude Code

```bash
claude mcp add groupdocs-signature -- dnx GroupDocs.Signature.Mcp --yes
```

With storage folder and license:

```bash
claude mcp add groupdocs-signature -e GROUPDOCS_MCP_STORAGE_PATH=/path/to/documents -e GROUPDOCS_LICENSE_PATH=/path/to/GroupDocs.Total.lic -- dnx GroupDocs.Signature.Mcp --yes
```

Pin a version by replacing `GroupDocs.Signature.Mcp` with `GroupDocs.Signature.Mcp@26.7.1`.
