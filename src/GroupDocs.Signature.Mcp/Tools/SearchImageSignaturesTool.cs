using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Domain;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class SearchImageSignaturesTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Searches a document for embedded image signatures (logo, stamp image, picture overlays) " +
        "and returns each image's page, position, size, and content as a base64-encoded PNG ready for display. " +
        "Supports PDF, DOCX, XLSX, PPTX, and 30+ more document formats. " +
        "Do NOT pre-check whether the file exists — pass the filename the user provided directly. " +
        "Returns a JSON object with `found` (count) and `signatures` (array with `index`, `page`, position, dimensions, `sizeBytes`, and `imageBase64`). " +
        "`page` is 1-based, and `page: 0` means the signature sits outside the page body - in a header or a footer. " +
        "On failure, the response text starts with 'Image signature search failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> SearchImageSignatures(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file,
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

            var options = new ImageSearchOptions
            {
                AllPages = true,
                ReturnContent = true,
                ReturnContentType = FileType.PNG
            };

            var signatures = sig.Search<ImageSignature>(options);

            var prefix = licenseManager.IsLicensed
                ? string.Empty
                : "[Evaluation mode] Results may be limited.\n\n";

            if (signatures.Count == 0)
                return $"{prefix}No image signatures found in '{resolved.FileName}'.";

            var results = signatures.Select((img, idx) => new
            {
                index = idx + 1,
                page = img.PageNumber,
                left = img.Left,
                top = img.Top,
                width = img.Width,
                height = img.Height,
                sizeBytes = img.Size,
                imageBase64 = img.Content?.Length > 0
                    ? $"data:image/png;base64,{Convert.ToBase64String(img.Content)}"
                    : null
            }).ToArray();

            // Pitfall #16: return raw JSON.
            return prefix + JsonSerializer.Serialize(new { found = signatures.Count, signatures = results }, JsonOptions);
        }
        catch (Exception ex)
        {
            return ToolError.Format("Image signature search", resolved.FileName, ex);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }
}
