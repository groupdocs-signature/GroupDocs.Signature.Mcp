using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Domain;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class SearchQrCodesTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Searches a document for QR code signatures and returns each QR code's decoded text, page, position, " +
        "and — when returnImage is true — the QR code graphic as a base64-encoded PNG. " +
        "Supports PDF, DOCX, XLSX, PPTX, and 30+ more document formats. " +
        "Optionally filters results to QR codes whose decoded text contains a specific string. " +
        "Do NOT pre-check whether the file exists — pass the filename the user provided directly. " +
        "Returns a JSON object with `found` (count) and `signatures` (array with `page`, `type`, `text`, position, dimensions, and optional `imageBase64`). " +
        "On failure, the response text starts with 'QR code search failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> SearchQrCodes(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file,
        [Description("Return only QR codes whose decoded text contains this string. Omit to return all QR codes.")] string? text = null,
        [Description("Include the QR code graphic as a base64 PNG in the response.")] bool returnImage = false,
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

            var options = new QrCodeSearchOptions
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

            var signatures = sig.Search<QrCodeSignature>(options);

            var prefix = licenseManager.IsLicensed
                ? string.Empty
                : "[Evaluation mode] Results may be limited.\n\n";

            if (signatures.Count == 0)
            {
                var hint = text != null ? $" matching '{text}'" : string.Empty;
                return $"{prefix}No QR code signatures{hint} found in '{resolved.FileName}'.";
            }

            var results = signatures.Select(q => new
            {
                page = q.PageNumber,
                type = q.EncodeType?.TypeName,
                text = q.Text,
                left = q.Left,
                top = q.Top,
                width = q.Width,
                height = q.Height,
                imageBase64 = returnImage && q.Content?.Length > 0
                    ? $"data:image/png;base64,{Convert.ToBase64String(q.Content)}"
                    : null
            }).ToArray();

            // Pitfall #16: return raw JSON.
            return prefix + JsonSerializer.Serialize(new { found = signatures.Count, signatures = results }, JsonOptions);
        }
        catch (Exception ex)
        {
            return FormatException(ex, resolved.FileName);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }

    private static string FormatException(Exception ex, string fileName)
    {
        var sb = new StringBuilder();
        sb.Append($"QR code search failed for '{fileName}': ");
        sb.Append($"{ex.GetType().FullName}: {ex.Message}");
        var inner = ex.InnerException;
        for (int depth = 0; inner != null && depth < 5; depth++, inner = inner.InnerException)
            sb.Append($" | inner({depth}): {inner.GetType().FullName}: {inner.Message}");
        return sb.ToString();
    }
}
