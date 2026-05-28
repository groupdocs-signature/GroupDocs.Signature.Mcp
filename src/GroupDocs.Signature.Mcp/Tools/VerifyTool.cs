using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class VerifyTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Verifies signatures in a document and returns the result (valid/invalid, counts) as JSON. Supports text, QR code, barcode, and digital signatures. " +
        "Supports PDF, DOCX, XLSX, PPTX, and 30+ more document formats. " +
        "Call this tool immediately whenever the user asks to verify a signature, check if a document is signed, or validate a signature. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided. " +
        "The tool resolves files from storage and returns an error with available files if a name is not found. " +
        "Returns a JSON object with `isValid`, `succeeded` (count), and `failed` (count) fields. " +
        "On failure, the response text starts with 'Verification failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> Verify(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file,
        [Description("Signature type to verify: text, qrcode, barcode, digital, all")] string type = "all",
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

            var verifyOptions = BuildVerifyOptions(type);
            var result = sig.Verify(verifyOptions);

            // Pitfall #16: return raw JSON, never via OutputHelper.TruncateText.
            return JsonSerializer.Serialize(new
            {
                isValid = result.IsValid,
                succeeded = result.Succeeded.Count,
                failed = result.Failed.Count
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            // Pitfall #18 — surface engine exceptions descriptively.
            return FormatException(ex, resolved.FileName, type);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }

    private static List<VerifyOptions> BuildVerifyOptions(string type) => type.ToLowerInvariant() switch
    {
        "text"    => [new TextVerifyOptions    { AllPages = true }],
        "qrcode"  => [new QrCodeVerifyOptions  { AllPages = true }],
        "barcode" => [new BarcodeVerifyOptions  { AllPages = true }],
        "digital" => [new DigitalVerifyOptions()],
        "all"     => [new TextVerifyOptions { AllPages = true }, new QrCodeVerifyOptions { AllPages = true },
                      new BarcodeVerifyOptions { AllPages = true }, new DigitalVerifyOptions()],
        _ => throw new ArgumentException($"Unknown type '{type}'. Supported: text, qrcode, barcode, digital, all.")
    };

    private static string FormatException(Exception ex, string fileName, string type)
    {
        var sb = new StringBuilder();
        sb.Append($"Verification failed for '{fileName}' (type: '{type}'): ");
        sb.Append($"{ex.GetType().FullName}: {ex.Message}");
        var inner = ex.InnerException;
        for (int depth = 0; inner != null && depth < 5; depth++, inner = inner.InnerException)
            sb.Append($" | inner({depth}): {inner.GetType().FullName}: {inner.Message}");
        return sb.ToString();
    }
}
