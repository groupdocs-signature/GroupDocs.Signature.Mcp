using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Mcp.Tools;
using Moq;
using Xunit;

namespace GroupDocs.Signature.Mcp.Tests;

/// <summary>
/// Covers the certificate-validity behaviour the engine introduced in 26.9: signing with a certificate
/// outside its validity period is rejected unless the caller opts in with allowExpired / allowNotYetValid.
/// </summary>
/// <remarks>
/// Unlike <see cref="SignToolTests"/>, these tests let the resolver return real content and reach the
/// engine. Certificates are generated per test so they can never expire into or out of the case they test.
/// </remarks>
public class SignToolCertificateTests
{
    private const string CertificatePassword = "test-password";
    private const string DocumentName = "sample.pdf";
    private const string CertificateName = "cert.pfx";

    private static readonly string SamplePdfPath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "sample.pdf");

    private readonly Mock<IFileResolver> _resolver = new();
    private readonly Mock<ILicenseManager> _licenseManager = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly OutputHelper _output;

    public SignToolCertificateTests()
    {
        _output = new OutputHelper(_storage.Object, Microsoft.Extensions.Options.Options.Create(new McpConfig()));

        _storage
            .Setup(s => s.WriteFileAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, byte[] _, bool _, CancellationToken _) => name);

        _storage
            .Setup(s => s.GetDownloadUrlAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    // ---- the four cases the engine change introduces -------------------------------------------------

    [Fact]
    public async Task Sign_ExpiredCertificate_WithoutFlag_FailsWithActionableMessage()
    {
        GivenDocumentAndCertificate(Expired());

        var result = await SignDigital();

        Assert.Contains("Signing failed for", result);
        Assert.Contains("expired on", result);
        // The message must name the property to set, otherwise the caller cannot act on it.
        Assert.Contains("DigitalSignOptions.AllowExpired", result);
        ThenNothingWasWritten();
    }

    [Fact]
    public async Task Sign_ExpiredCertificate_WithAllowExpired_SignsAndWarns()
    {
        GivenDocumentAndCertificate(Expired());

        var result = await SignDigital(allowExpired: true);

        Assert.DoesNotContain("Signing failed for", result);
        Assert.Contains("Certificate warning:", result);
        Assert.Contains("expired on", result);
    }

    [Fact]
    public async Task Sign_NotYetValidCertificate_WithoutFlag_FailsWithActionableMessage()
    {
        GivenDocumentAndCertificate(NotYetValid());

        var result = await SignDigital();

        Assert.Contains("Signing failed for", result);
        Assert.Contains("is not valid until", result);
        Assert.Contains("DigitalSignOptions.AllowNotYetValid", result);
        ThenNothingWasWritten();
    }

    [Fact]
    public async Task Sign_NotYetValidCertificate_WithAllowNotYetValid_SignsAndWarns()
    {
        GivenDocumentAndCertificate(NotYetValid());

        var result = await SignDigital(allowNotYetValid: true);

        Assert.DoesNotContain("Signing failed for", result);
        Assert.Contains("Certificate warning:", result);
        Assert.Contains("is not valid until", result);
    }

    // ---- guards: nothing else changed ---------------------------------------------------------------

    [Fact]
    public async Task Sign_ValidCertificate_SignsWithoutWarning()
    {
        GivenDocumentAndCertificate(Valid());

        var result = await SignDigital();

        Assert.DoesNotContain("Signing failed for", result);
        // The collector must stay quiet when there is nothing wrong with the certificate.
        Assert.DoesNotContain("Certificate warning:", result);
    }

    [Fact]
    public async Task Sign_TextSignature_IgnoresAllowExpired()
    {
        GivenDocumentAndCertificate(Valid());

        var result = await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = DocumentName }, type: "text", text: "hello",
            allowExpired: true, allowNotYetValid: true);

        // No certificate is involved in a text signature, so the flags mean nothing and must not
        // change the response.
        Assert.DoesNotContain("Signing failed for", result);
        Assert.DoesNotContain("Certificate warning:", result);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private Task<string> SignDigital(bool allowExpired = false, bool allowNotYetValid = false) =>
        SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = DocumentName },
            type: "digital",
            certificate: new FileInput { FilePath = CertificateName },
            certificatePassword: CertificatePassword,
            allowExpired: allowExpired,
            allowNotYetValid: allowNotYetValid);

    private void GivenDocumentAndCertificate(byte[] pfx)
    {
        var pdf = File.ReadAllBytes(SamplePdfPath);

        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileInput input, CancellationToken _) => input.FilePath == CertificateName
                ? new ResolvedFile { FileName = CertificateName, Stream = new MemoryStream(pfx) }
                : new ResolvedFile { FileName = DocumentName, Stream = new MemoryStream(pdf) });
    }

    private void ThenNothingWasWritten() =>
        _storage.Verify(
            s => s.WriteFileAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private static byte[] Expired() =>
        CreatePfx(DateTimeOffset.UtcNow.AddYears(-3), DateTimeOffset.UtcNow.AddYears(-1));

    private static byte[] NotYetValid() =>
        CreatePfx(DateTimeOffset.UtcNow.AddYears(1), DateTimeOffset.UtcNow.AddYears(3));

    private static byte[] Valid() =>
        CreatePfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

    /// <summary>
    /// Builds a self-signed PFX in memory, so no certificate is committed and none can expire by itself.
    /// </summary>
    private static byte[] CreatePfx(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=GroupDocs MCP Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));

        using var certificate = request.CreateSelfSigned(notBefore, notAfter);

        // ExportPkcs12 rather than Export(X509ContentType.Pfx, ...): on .NET 9+ loading goes through
        // X509CertificateLoader, which rejects the legacy RC2/3DES encryption the old API produces.
        return certificate.ExportPkcs12(Pkcs12ExportPbeParameters.Pbes2Aes256Sha256, CertificatePassword);
    }
}
