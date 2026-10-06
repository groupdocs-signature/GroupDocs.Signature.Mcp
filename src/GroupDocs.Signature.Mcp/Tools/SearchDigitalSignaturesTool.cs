using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Domain;
using GroupDocs.Signature.Options;
using ModelContextProtocol.Server;

namespace GroupDocs.Signature.Mcp.Tools;

[McpServerToolType]
public static class SearchDigitalSignaturesTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Searches a document for digital certificate signatures and returns details for each: " +
        "signer name, issuer, certificate serial number, validity period, sign timestamp, validity status, " +
        "and any comments or reason attached to the signature. " +
        "Supports PDF and Office documents (DOCX, XLSX, PPTX). " +
        "Do NOT pre-check whether the file exists — pass the filename the user provided directly. " +
        "Returns a JSON object with `found` (count), `signatures` (array with `signTime`, `isValid`, `comments`, `thumbprint`, and a nested `certificate` object) " +
        "and `certificateWarnings` (array, empty when nothing is wrong). " +
        "Each `certificate` also carries `isExpired` and `isNotYetValid` (its state right now) and `validWhenSigned` " +
        "(whether the signature was made inside the certificate's validity period, null when the document records no sign time). " +
        "Judge a signature by `validWhenSigned`: a certificate that expired AFTER the document was signed does not make that signature invalid. " +
        "On failure, the response text starts with 'Digital signature search failed for' followed by the underlying exception type, message, and inner-exception chain.")]
    public static async Task<string> SearchDigitalSignatures(
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

            var signatures = sig.Search<DigitalSignature>(SignatureType.Digital);

            var prefix = licenseManager.IsLicensed
                ? string.Empty
                : "[Evaluation mode] Results may be limited.\n\n";

            if (signatures.Count == 0)
                return $"{prefix}No digital signatures found in '{resolved.FileName}'.";

            var utcNow = DateTime.UtcNow;
            var certificateWarnings = new List<string>();

            var results = signatures.Select(d =>
            {
                var certificate = d.Certificate;
                if (certificate != null)
                {
                    // The engine reports the dates but never draws a conclusion from them, so the caller
                    // would otherwise have to compare them itself to notice an invalid signature.
                    var warning = CertificateValidity.Describe(certificate, d.SignTime, utcNow);
                    if (warning != null && !certificateWarnings.Contains(warning)) certificateWarnings.Add(warning);
                }

                return new
                {
                    signTime = d.SignTime,
                    isValid = d.IsValid,
                    comments = d.Comments,
                    thumbprint = d.Thumbprint,
                    certificate = certificate == null ? null : new
                    {
                        subject = certificate.Subject,
                        issuer = certificate.Issuer,
                        serialNumber = certificate.SerialNumber,
                        validFrom = certificate.NotBefore,
                        validTo = certificate.NotAfter,
                        thumbprint = certificate.Thumbprint,
                        isExpired = CertificateValidity.IsExpired(certificate, utcNow),
                        isNotYetValid = CertificateValidity.IsNotYetValid(certificate, utcNow),
                        validWhenSigned = CertificateValidity.WasValidWhenSigned(certificate, d.SignTime)
                    }
                };
            }).ToArray();

            // Pitfall #16: return raw JSON.
            return prefix + JsonSerializer.Serialize(
                new { found = signatures.Count, signatures = results, certificateWarnings }, JsonOptions);
        }
        catch (Exception ex)
        {
            return ToolError.Format("Digital signature search", resolved.FileName, ex);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }
}
