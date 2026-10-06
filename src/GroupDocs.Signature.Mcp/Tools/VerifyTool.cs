using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Domain;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class VerifyTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Checks the signatures in a document and reports what is there and whether it holds up. Supports text, QR code, barcode, and digital signatures. " +
        "Supports PDF, DOCX, XLSX, PPTX, and 30+ more document formats. " +
        "Call this tool immediately whenever the user asks to verify a signature, check if a document is signed, or validate a signature. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided. " +
        "The tool resolves files from storage and returns an error with available files if a name is not found. " +
        "Returns a JSON object with `found` (how many signatures matched), `signatures` (one entry each, with its `type`), " +
        "`isValid`, and `certificateWarnings` (array, empty when nothing is wrong). " +
        "`isValid` is true only when every digital signature passed its cryptographic check; it is null when the document has no digital " +
        "signature, because text, QR code and barcode signatures carry nothing to verify - for those, `found` is the answer. " +
        "`isValid` does NOT cover whether the certificate behind a digital signature was inside its validity period: always report " +
        "`certificateWarnings` too, because a signature can be `isValid: true` here and still be rejected by Adobe Acrobat or Word. " +
        "On failure, the response text starts with 'Verification failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> Verify(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file,
        [Description("Signature type to check: text, qrcode, barcode, digital, all")] string type = "all",
        [Description("Only report signatures whose text contains this value. Applies to types text, qrcode and barcode; ignored for 'digital', which has no text.")] string? text = null,
        [Description("Password for protected documents")] string? password = null)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);

        var tempInput = Path.Combine(Path.GetTempPath(), $"gd_mcp_{Guid.NewGuid()}{Path.GetExtension(resolved.FileName)}");

        try
        {
            var typeLower = type.ToLowerInvariant();
            if (typeLower is not ("text" or "qrcode" or "barcode" or "digital" or "all"))
                throw new ArgumentException($"Unknown type '{type}'. Supported: text, qrcode, barcode, digital, all.");

            await using (var fs = File.Create(tempInput))
                await resolved.Stream.CopyToAsync(fs);

            var loadOptions = password != null ? new LoadOptions { Password = password } : null;
            using var sig = loadOptions != null
                ? new Signature(tempInput, loadOptions)
                : new Signature(tempInput);

            // Built on Search, not on Signature.Verify. Verify answers "does this document contain the
            // signature I describe?" and can only ever return signatures the caller already specified -
            // on PDF it records nothing at all unless the signer's certificate is supplied, so it reported
            // succeeded: 0 for documents that plainly are signed. Search enumerates what is really there
            // and performs the same per-signature cryptographic check for digital signatures.
            var utcNow = DateTime.UtcNow;
            var entries = new List<object>();
            var certificateWarnings = new List<string>();
            bool? digitalValid = null;

            if (typeLower is "digital" or "all")
            {
                foreach (var d in sig.Search<DigitalSignature>(SignatureType.Digital))
                {
                    digitalValid = (digitalValid ?? true) && d.IsValid;

                    var certificate = d.Certificate;
                    if (certificate != null)
                    {
                        var warning = CertificateValidity.Describe(certificate, d.SignTime, utcNow);
                        if (warning != null && !certificateWarnings.Contains(warning)) certificateWarnings.Add(warning);
                    }

                    entries.Add(new
                    {
                        type = "digital",
                        isValid = d.IsValid,
                        signTime = d.SignTime,
                        comments = d.Comments,
                        certificate = certificate == null ? null : new
                        {
                            subject = certificate.Subject,
                            issuer = certificate.Issuer,
                            validFrom = certificate.NotBefore,
                            validTo = certificate.NotAfter,
                            thumbprint = certificate.Thumbprint,
                            isExpired = CertificateValidity.IsExpired(certificate, utcNow),
                            isNotYetValid = CertificateValidity.IsNotYetValid(certificate, utcNow),
                            validWhenSigned = CertificateValidity.WasValidWhenSigned(certificate, d.SignTime)
                        }
                    });
                }
            }

            if (typeLower is "text" or "all")
            {
                var options = new TextSearchOptions { AllPages = true };
                if (!string.IsNullOrWhiteSpace(text)) { options.Text = text; options.MatchType = TextMatchType.Contains; }

                foreach (var t in sig.Search<TextSignature>(options))
                    entries.Add(new { type = "text", pageNumber = t.PageNumber, text = t.Text });
            }

            if (typeLower is "qrcode" or "all")
            {
                var options = new QrCodeSearchOptions { AllPages = true };
                if (!string.IsNullOrWhiteSpace(text)) { options.Text = text; options.MatchType = TextMatchType.Contains; }

                foreach (var q in sig.Search<QrCodeSignature>(options))
                    entries.Add(new { type = "qrcode", pageNumber = q.PageNumber, encodeType = q.EncodeType?.TypeName, text = q.Text });
            }

            if (typeLower is "barcode" or "all")
            {
                var options = new BarcodeSearchOptions { AllPages = true };
                if (!string.IsNullOrWhiteSpace(text)) { options.Text = text; options.MatchType = TextMatchType.Contains; }

                foreach (var b in sig.Search<BarcodeSignature>(options))
                    entries.Add(new { type = "barcode", pageNumber = b.PageNumber, encodeType = b.EncodeType?.TypeName, text = b.Text });
            }

            var prefix = licenseManager.IsLicensed
                ? string.Empty
                : "[Evaluation mode] Results may be limited.\n\n";

            // Pitfall #16: return raw JSON, never via OutputHelper.TruncateText.
            return prefix + JsonSerializer.Serialize(new
            {
                found = entries.Count,
                // Null rather than false when there is no digital signature: nothing was cryptographically
                // checked, and claiming "false" would read as "the signatures are bad".
                isValid = digitalValid,
                signatures = entries,
                certificateWarnings
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            // Pitfall #18 — surface engine exceptions descriptively.
            return ToolError.Format("Verification", resolved.FileName, ex, $" (type: '{type}')");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }
}
