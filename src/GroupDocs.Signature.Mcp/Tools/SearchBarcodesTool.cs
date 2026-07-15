using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Domain;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class SearchBarcodesTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Searches a document for barcode signatures (Code39, Code128, EAN, QR-adjacent 1D barcodes, etc.) " +
        "and returns each barcode's decoded text value, page, position, and — when returnImage is true — the barcode graphic as a base64-encoded PNG. " +
        "Supports PDF, DOCX, XLSX, PPTX, and 30+ more document formats. " +
        "Optionally filters results to barcodes whose text contains a specific string. " +
        "Do NOT pre-check whether the file exists — pass the filename the user provided directly. " +
        "Returns a JSON object with `found` (count) and `signatures` (array with `page`, `type`, `text`, position, dimensions, and optional `imageBase64`). " +
        "On failure, the response text starts with 'Barcode search failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> SearchBarcodes(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file,
        [Description("Return only barcodes whose decoded text contains this string. Omit to return all barcodes.")] string? text = null,
        [Description("Include the barcode graphic as a base64 PNG in the response.")] bool returnImage = false,
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

            var options = new BarcodeSearchOptions
            {
                AllPages = true,
                ReturnContent = returnImage,
                ReturnContentType = FileType.PNG
            };

            if (!string.IsNullOrEmpty(text))
            {
                options.Text = text;
                options.MatchType = TextMatchType.Contains;
            }

            var signatures = sig.Search<BarcodeSignature>(options);

            var prefix = licenseManager.IsLicensed
                ? string.Empty
                : "[Evaluation mode] Results may be limited.\n\n";

            if (signatures.Count == 0)
            {
                var hint = text != null ? $" matching '{text}'" : string.Empty;
                return $"{prefix}No barcode signatures{hint} found in '{resolved.FileName}'.";
            }

            var results = signatures.Select(b => new
            {
                page = b.PageNumber,
                type = b.EncodeType?.TypeName,
                text = b.Text,
                left = b.Left,
                top = b.Top,
                width = b.Width,
                height = b.Height,
                imageBase64 = returnImage && b.Content?.Length > 0
                    ? $"data:image/png;base64,{Convert.ToBase64String(b.Content)}"
                    : null
            }).ToArray();

            // Pitfall #16: return raw JSON.
            return prefix + JsonSerializer.Serialize(new { found = signatures.Count, signatures = results }, JsonOptions);
        }
        catch (Exception ex)
        {
            return ToolError.Format("Barcode search", resolved.FileName, ex);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }
}
