using System.ComponentModel;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Domain;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class SignTool
{
    [McpServerTool, Description(
        "Signs a document with a text, QR code, barcode, or digital certificate signature and saves the signed file to storage. " +
        "Supports PDF, DOCX, XLSX, PPTX, and 30+ more document formats. " +
        "Call this tool immediately whenever the user asks to sign a document, add a signature, or apply a digital signature. " +
        "Do NOT pre-check whether files exist — just pass the filenames the user provided. " +
        "The tool resolves files from storage and returns an error with available files if a name is not found. " +
        "Returns a saved-path message ('Signed <file> with <type> signature') and the download URL or storage path. " +
        "On failure, the response text starts with 'Signing failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> Sign(
        IFileResolver resolver,
        IFileStorage storage,
        ILicenseManager licenseManager,
        OutputHelper output,
        FileInput file,
        [Description("Signature type: text, qrcode, barcode, digital")] string type,
        [Description("Signature text (for text/qrcode/barcode types)")] string? text = null,
        [Description("Digital certificate file (for type 'digital')")] FileInput? certificate = null,
        [Description("Certificate password")] string? certificatePassword = null,
        [Description("Password for protected documents")] string? password = null)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);

        var outputName = $"{Path.GetFileNameWithoutExtension(resolved.FileName)}_signed{Path.GetExtension(resolved.FileName)}";
        var tempInput = Path.Combine(Path.GetTempPath(), $"gd_mcp_{Guid.NewGuid()}{Path.GetExtension(resolved.FileName)}");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"gd_mcp_{Guid.NewGuid()}{Path.GetExtension(resolved.FileName)}");
        string? tempCert = null;

        try
        {
            await using (var fs = File.Create(tempInput))
                await resolved.Stream.CopyToAsync(fs);

            var loadOptions = password != null ? new LoadOptions { Password = password } : null;
            using var sig = loadOptions != null
                ? new Signature(tempInput, loadOptions)
                : new Signature(tempInput);

            SignOptions signOptions;
            var typeLower = type.ToLowerInvariant();

            if (typeLower == "digital")
            {
                if (certificate == null)
                    return "Digital signature requires a certificate file. Provide the 'certificate' parameter.";

                using var certResolved = await resolver.ResolveAsync(certificate);
                tempCert = Path.Combine(Path.GetTempPath(), $"gd_mcp_cert_{Guid.NewGuid()}{Path.GetExtension(certResolved.FileName)}");
                await using (var fs = File.Create(tempCert))
                    await certResolved.Stream.CopyToAsync(fs);

                signOptions = new DigitalSignOptions(tempCert) { Password = certificatePassword };
            }
            else
            {
                signOptions = typeLower switch
                {
                    "text" => new TextSignOptions(text ?? "Signed"),
                    "qrcode" => new QrCodeSignOptions(text ?? "Signed") { EncodeType = QrCodeTypes.QR },
                    "barcode" => new BarcodeSignOptions(text ?? "Signed") { EncodeType = BarcodeTypes.Code128 },
                    _ => throw new ArgumentException($"Unknown signature type '{type}'. Supported: text, qrcode, barcode, digital.")
                };
            }

            sig.Sign(tempOutput, signOptions);

            if (!File.Exists(tempOutput))
                return licenseManager.IsLicensed
                    ? $"Signing failed — output file was not created for type '{type}'."
                    : "[Evaluation mode] Signing did not produce output. Set GROUPDOCS_LICENSE_PATH for full support.";

            var outputBytes = await File.ReadAllBytesAsync(tempOutput);
            var savedPath = await storage.WriteFileAsync(outputName, outputBytes, rewrite: false);

            var prefix = licenseManager.IsLicensed ? string.Empty : "[Evaluation mode] Output may include watermarks.\n\n";
            var description = $"{prefix}Signed '{resolved.FileName}' with {type} signature";
            return await output.BuildFileOutputAsync(savedPath, description);
        }
        catch (Exception ex)
        {
            // Surface the underlying engine exception instead of letting it bubble
            // to MCP's generic "An error occurred invoking 'sign'." wrapper.
            // Pattern per Pitfall #18.
            return ToolError.Format("Signing", resolved.FileName, ex, $" (type: '{type}')");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
            if (tempCert != null && File.Exists(tempCert)) File.Delete(tempCert);
        }
    }
}
