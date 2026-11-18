using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Domain;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class SearchTextSignaturesTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Searches a document for embedded text signatures (stamps, labels, native text annotations) " +
        "and returns each signature's text content, page, position, and implementation type. " +
        "Supports PDF, DOCX, XLSX, PPTX, and 30+ more document formats. " +
        "Optionally filters to signatures whose text contains a specific string. " +
        "Note: this searches for signature objects — to search for arbitrary text inside document content use a text-extraction tool instead. " +
        "Do NOT pre-check whether the file exists — pass the filename the user provided directly. " +
        "Returns a JSON object with `found` (count) and `signatures` (array with `page`, `text`, `implementation`, `left`, `top`, `width`, `height` per signature). " +
        "`page` is 1-based, and `page: 0` means the signature sits outside the page body - in a header or a footer. " +
        "Only applied signatures are returned; document body text is excluded unless includeDocumentContent is set. " +
        "On failure, the response text starts with 'Text signature search failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> SearchTextSignatures(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file,
        [Description("Return only text signatures whose content contains this string. Omit to return all text signatures.")] string? text = null,
        [Description(
            "Also return text that is part of the document body rather than a signature. Default false. " +
            "Office formats keep ordinary text in objects the engine reports alongside real signatures, so leaving this " +
            "false is what makes the result a list of signatures.")] bool includeDocumentContent = false,
        [Description("Password for protected documents.")] string? password = null)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);

        var tempInput = Path.Combine(Path.GetTempPath(), $"gd_mcp_{Guid.NewGuid()}{Path.GetExtension(resolved.FileName)}");
        try
        {
            await using (var fs = File.Create(tempInput))
                await resolved.Stream.CopyToAsync(fs);

            var loadOptions = password != null ? new LoadOptions { Password = password } : null;
            using var sig = loadOptions != null
                ? new Signature(tempInput, loadOptions)
                : new Signature(tempInput);

            // SkipExternal keeps only what the engine marks as an applied signature. Without it a signed
            // PPTX also returns its body text as a "signature". Note IsSignature is true for both, so it
            // cannot be used to filter after the fact.
            var options = new TextSearchOptions { AllPages = true, SkipExternal = !includeDocumentContent };
            if (!string.IsNullOrEmpty(text))
            {
                options.Text = text;
                options.MatchType = TextMatchType.Contains;
            }

            var searchResult = ArchiveAwareSearch.Run(sig, options);

            var prefix = licenseManager.IsLicensed
                ? string.Empty
                : "[Evaluation mode] Results may be limited.\n\n";

            static object Project(TextSignature t) => new
            {
                page = t.PageNumber,
                text = t.Text,
                implementation = t.SignatureImplementation.ToString(),
                left = t.Left,
                top = t.Top,
                width = t.Width,
                height = t.Height
            };

            // An archive keeps its signatures one level down, per contained document.
            var documents = ArchiveAwareSearch.Documents<TextSignature>(searchResult);
            if (documents.Count > 0)
            {
                var perDocument = documents.Select(d => new
                {
                    fileName = d.FileName,
                    found = d.Items.Count,
                    signatures = d.Items.Select(Project).ToArray()
                }).ToArray();

                return prefix + JsonSerializer.Serialize(new
                {
                    found = perDocument.Sum(d => d.found),
                    archive = true,
                    documents = perDocument
                }, JsonOptions);
            }

            var signatures = ArchiveAwareSearch.Signatures<TextSignature>(searchResult);
            if (signatures.Count == 0)
            {
                var hint = text != null ? $" containing '{text}'" : string.Empty;
                return $"{prefix}No text signatures{hint} found in '{resolved.FileName}'.";
            }

            // Pitfall #16: return raw JSON.
            return prefix + JsonSerializer.Serialize(
                new { found = signatures.Count, signatures = signatures.Select(Project).ToArray() }, JsonOptions);
        }
        catch (Exception ex)
        {
            return ToolError.Format("Text signature search", resolved.FileName, ex);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }
}
