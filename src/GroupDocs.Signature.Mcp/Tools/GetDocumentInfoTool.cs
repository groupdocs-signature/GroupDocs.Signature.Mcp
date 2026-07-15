using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class GetDocumentInfoTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Returns the file type, page count, size, and per-page dimensions of a document as JSON, without modifying the file. " +
        "Supports PDF, DOCX, XLSX, PPTX, images, and 30+ more document formats. " +
        "Call this tool whenever the user asks to inspect a document, check its page count, or get its details — " +
        "useful as a precondition check before sign / verify / search (e.g. 'how many pages does this PDF have?'). " +
        "Do NOT pre-check whether the file exists — just pass the filename the user provided. " +
        "Returns a JSON object with fields `fileName`, `fileType`, `pageCount`, `size`, and `pages` (array of `{ number, width, height }`). " +
        "On failure, the response text starts with 'Document-info lookup failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> GetDocumentInfo(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file,
        [Description("Password for protected documents")] string? password = null)
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

            var info = sig.GetDocumentInfo();
            if (info == null)
                return $"Could not retrieve document information for '{resolved.FileName}'.";

            // Use reflection to harvest fields robustly across IDocumentInfo subtypes
            // (subtype-specific layout per format — see Step 11 of clone-to-new-product.md).
            var infoType = info.GetType();
            int? pageCount = TryGetInt(info, infoType, "PageCount");
            string? fileTypeName = TryGetString(info, infoType, "FileType")
                                    ?? TryGetString(info, infoType, "FileFormat");
            long? size = TryGetLong(info, infoType, "Size");

            var pages = ExtractPages(info, infoType);

            var payload = new
            {
                fileName = resolved.FileName,
                fileType = fileTypeName,
                pageCount = pageCount,
                size = size,
                pages = pages,
            };

            // Pitfall #16: raw JSON — never piped through OutputHelper.TruncateText.
            return JsonSerializer.Serialize(payload, JsonOptions);
        }
        catch (Exception ex)
        {
            // Pitfall #18 — surface engine exceptions descriptively.
            return ToolError.Format("Document-info lookup", resolved.FileName, ex);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }

    private static int? TryGetInt(object obj, Type t, string name)
    {
        var v = t.GetProperty(name)?.GetValue(obj);
        return v is int n ? n : (int?)null;
    }

    private static long? TryGetLong(object obj, Type t, string name)
    {
        var v = t.GetProperty(name)?.GetValue(obj);
        if (v is long l) return l;
        if (v is int i)  return i;
        return null;
    }

    private static string? TryGetString(object obj, Type t, string name)
    {
        var v = t.GetProperty(name)?.GetValue(obj);
        return v?.ToString();
    }

    private static object[]? ExtractPages(object info, Type infoType)
    {
        var pagesProp = infoType.GetProperty("Pages")?.GetValue(info);
        if (pagesProp is not System.Collections.IEnumerable pages)
            return null;

        var list = new List<object>();
        int idx = 0;
        foreach (var p in pages)
        {
            idx++;
            if (p == null) continue;
            var pt = p.GetType();
            list.Add(new
            {
                number = TryGetInt(p, pt, "Number") ?? idx,
                width  = TryGetDouble(p, pt, "Width"),
                height = TryGetDouble(p, pt, "Height"),
            });
        }
        return list.ToArray();
    }

    private static double? TryGetDouble(object obj, Type t, string name)
    {
        var v = t.GetProperty(name)?.GetValue(obj);
        return v switch
        {
            double d => d,
            float f  => f,
            int i    => i,
            long l   => l,
            _        => null,
        };
    }
}
