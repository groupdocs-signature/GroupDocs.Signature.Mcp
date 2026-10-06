using System.ComponentModel;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Domain;
// ILogger and LogLevel below are GroupDocs.Signature's own, not Microsoft.Extensions.Logging's.
// Do not add that namespace to this file - both names would become ambiguous.
using GroupDocs.Signature.Logging;
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
        "On failure, the response text starts with 'Signing failed for' followed by the underlying exception type, message, and inner-exception chain. " +
        "Signing with a digital certificate that is outside its validity period fails unless allowExpired or allowNotYetValid is set; " +
        "when one of them is set, the response additionally contains a 'Certificate warning: ...' line for each affected certificate.")]
    public static async Task<string> Sign(
        IFileResolver resolver,
        IFileStorage storage,
        ILicenseManager licenseManager,
        OutputHelper output,
        FileInput file,
        [Description("Signature type: text, qrcode, barcode, digital")] string type,
        [Description("Signature text. REQUIRED for types text, qrcode and barcode - the call is rejected without it, and nothing is signed. Not used by type 'digital'.")] string? text = null,
        [Description("Digital certificate file (for type 'digital')")] FileInput? certificate = null,
        [Description("Certificate password")] string? certificatePassword = null,
        [Description("Password for protected documents")] string? password = null,
        [Description(
            "Font family name for a text signature, for example 'Arial' or 'Noto Sans CJK SC'. " +
            "Applies to type 'text' only; QR code and barcode signatures do not take a font. " +
            "REQUIRED when the text contains Chinese, Japanese or Korean characters: the engine looks fonts up by " +
            "family name and the default it picks for those scripts is not present on Linux, so signing fails with " +
            "'Font <name> was not found' unless you name an installed family such as 'Noto Sans CJK SC'.")] string? font = null,
        [Description(
            "Sign even if the certificate's validity period has already ended. Applies to type 'digital' only. " +
            "Default false: signing with an expired certificate fails and no file is written. " +
            "Set true only when that certificate must be used anyway - the document IS signed, but signature validators " +
            "report such a signature as NOT valid.")] bool allowExpired = false,
        [Description(
            "Sign even if the certificate's validity period has not started yet. Applies to type 'digital' only. " +
            "Default false: signing with such a certificate fails and no file is written. " +
            "Usually means the certificate was issued for a later date or the machine clock is wrong. " +
            "Set true only when that certificate must be used anyway - the document IS signed, but signature validators " +
            "report such a signature as NOT valid.")] bool allowNotYetValid = false)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);

        var outputName = $"{Path.GetFileNameWithoutExtension(resolved.FileName)}_signed{Path.GetExtension(resolved.FileName)}";
        var tempInput = Path.Combine(Path.GetTempPath(), $"gd_mcp_{Guid.NewGuid()}{Path.GetExtension(resolved.FileName)}");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"gd_mcp_{Guid.NewGuid()}{Path.GetExtension(resolved.FileName)}");
        string? tempCert = null;

        try
        {
            var typeLower = type.ToLowerInvariant();

            // Check the arguments before touching the document. A call that cannot succeed should not
            // copy the file to disk or open it in the engine first.
            if (typeLower is not ("text" or "qrcode" or "barcode" or "digital"))
                throw new ArgumentException($"Unknown signature type '{type}'. Supported: text, qrcode, barcode, digital.");

            // Refuse rather than invent content. This used to fall back to the literal word "Signed",
            // which signed and saved a document carrying a mark the caller never asked for - and for
            // qrcode/barcode, a scannable code whose payload was the word "Signed".
            if (typeLower != "digital" && string.IsNullOrWhiteSpace(text))
                return $"A {typeLower} signature requires text. Provide the 'text' parameter.";

            await using (var fs = File.Create(tempInput))
                await resolved.Stream.CopyToAsync(fs);

            var loadOptions = password != null ? new LoadOptions { Password = password } : null;

            // Only the digital path can produce certificate-validity warnings, and only when the caller
            // opted in. Attaching a logger on the text/qrcode/barcode paths would start surfacing
            // unrelated engine warnings in their output, which must stay exactly as it is today.
            var warnings = typeLower == "digital" && (allowExpired || allowNotYetValid)
                ? new WarningCollector()
                : null;

            // SignatureSettings.Logger has a private setter, so the logger can only be supplied through
            // the constructor. LogLevel is narrowed to Warning so the collector never picks up the
            // engine's Trace chatter, nor the Error it logs on its way out of a failed Sign().
            var settings = warnings != null
                ? new SignatureSettings(warnings) { LogLevel = LogLevel.Warning }
                : null;

            using var sig = CreateSignature(tempInput, loadOptions, settings);

            SignOptions signOptions;

            if (typeLower == "digital")
            {
                if (certificate == null)
                    return "Digital signature requires a certificate file. Provide the 'certificate' parameter.";

                using var certResolved = await resolver.ResolveAsync(certificate);
                tempCert = Path.Combine(Path.GetTempPath(), $"gd_mcp_cert_{Guid.NewGuid()}{Path.GetExtension(certResolved.FileName)}");
                await using (var fs = File.Create(tempCert))
                    await certResolved.Stream.CopyToAsync(fs);

                signOptions = new DigitalSignOptions(tempCert)
                {
                    Password = certificatePassword,
                    // Passed through verbatim. Both default to false, so the engine's secure default
                    // (reject a certificate outside its validity period) is never weakened implicitly.
                    AllowExpired = allowExpired,
                    AllowNotYetValid = allowNotYetValid
                };
            }
            else
            {
                // Type and text were validated at the top of the method, before the document was opened.
                // Flow analysis cannot tie that guard to this branch, so state the guarantee once here
                // rather than suppressing it at three call sites.
                var signatureText = text!;

                signOptions = typeLower switch
                {
                    "text" => CreateTextOptions(signatureText, font),
                    // QrCodeSignOptions and BarcodeSignOptions have no Font property, so `font` cannot
                    // apply to them; its description says so.
                    "qrcode" => new QrCodeSignOptions(signatureText) { EncodeType = QrCodeTypes.QR },
                    _ => new BarcodeSignOptions(signatureText) { EncodeType = BarcodeTypes.Code128 }
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
            var description = $"{prefix}Signed '{resolved.FileName}' with {type} signature{FormatWarnings(warnings)}";
            return await output.BuildFileOutputAsync(savedPath, description);
        }
        catch (Exception ex)
        {
            // Surface the underlying engine exception instead of letting it bubble
            // to MCP's generic "An error occurred invoking 'sign'." wrapper.
            // Pattern per Pitfall #18.
            return AppendFontHint(ToolError.Format("Signing", resolved.FileName, ex, $" (type: '{type}')"), font);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
            if (tempCert != null && File.Exists(tempCert)) File.Delete(tempCert);
        }
    }

    /// <summary>
    /// Adds the one thing the engine's font error does not say: which parameter fixes it.
    /// </summary>
    /// <remarks>
    /// The engine names the font it could not find, but not how to choose another. On Linux this is the
    /// usual first encounter with CJK text, because the family it picks by default for those scripts is a
    /// Windows font. Matching on the message text is deliberate and additive: if the wording ever changes,
    /// the hint simply stops being added and the original error is returned unchanged.
    /// </remarks>
    private static string AppendFontHint(string message, string? font)
    {
        if (!message.Contains("Font ", StringComparison.Ordinal) ||
            !message.Contains("was not found", StringComparison.Ordinal))
            return message;

        return message + (string.IsNullOrWhiteSpace(font)
            ? "\n\nName an installed font with the 'font' parameter - for example font: \"Noto Sans CJK SC\" " +
              "for Chinese, \"Noto Sans CJK JP\" for Japanese, \"Noto Sans CJK KR\" for Korean."
            : $"\n\nThe font '{font}' is not installed on this server. Try another installed family - for example " +
              "\"Noto Sans CJK SC\" for Chinese, \"Noto Sans CJK JP\" for Japanese, \"Noto Sans CJK KR\" for Korean.");
    }

    // Only touches Font when the caller named one, so the engine's own default is untouched otherwise.
    private static TextSignOptions CreateTextOptions(string text, string? fontFamily)
    {
        var options = new TextSignOptions(text);
        if (!string.IsNullOrWhiteSpace(fontFamily))
            options.Font = new SignatureFont { FamilyName = fontFamily };
        return options;
    }

    // Picks the Signature overload that matches what we actually have. When settings is null the call
    // is identical to the pre-26.9 code, which keeps the text/qrcode/barcode paths byte-for-byte unchanged.
    // An if-chain rather than a tuple switch: a tuple pattern does not flow-narrow the locals, so the
    // three-argument call would warn CS8604 under <Nullable>enable</Nullable>.
    private static Signature CreateSignature(string path, LoadOptions? loadOptions, SignatureSettings? settings)
    {
        if (loadOptions != null && settings != null) return new Signature(path, loadOptions, settings);
        if (loadOptions != null) return new Signature(path, loadOptions);
        if (settings != null) return new Signature(path, settings);
        return new Signature(path);
    }

    private const string CertificateWarningPrefix = "Certificate warning: ";

    private static string FormatWarnings(WarningCollector? collector)
    {
        var messages = collector?.Snapshot();
        if (messages == null || messages.Count == 0) return string.Empty;
        return "\n\n" + string.Join("\n", messages.Select(m => CertificateWarningPrefix + m));
    }

    /// <summary>
    /// Collects engine warnings so the tool can return them to the caller.
    /// </summary>
    /// <remarks>
    /// Deliberately writes nowhere. This server speaks MCP over stdio, and the engine's own
    /// <c>ConsoleLogger</c> writes to stdout, which is the JSON-RPC channel - using it would corrupt
    /// the protocol. One instance per Sign call, so nothing is shared between requests.
    /// </remarks>
    private sealed class WarningCollector : ILogger
    {
        private readonly List<string> _messages = [];
        private readonly object _gate = new();

        public void Warning(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            lock (_gate)
            {
                // The engine can validate the document certificate and a VBA project certificate in the
                // same pass; de-duplicate so the caller never sees the same line twice.
                if (!_messages.Contains(message)) _messages.Add(message);
            }
        }

        // LogLevel is narrowed to Warning, so these are not reached. They must still never throw:
        // the engine calls the logger from inside Sign().
        public void Error(string message, Exception ex) { }

        public void Trace(string message) { }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_gate) return _messages.ToArray();
        }
    }
}
