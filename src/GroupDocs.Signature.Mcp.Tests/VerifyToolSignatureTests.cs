using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Mcp.Tools;
using Moq;
using Xunit;

namespace GroupDocs.Signature.Mcp.Tests;

/// <summary>
/// Verify against real signed documents, produced in the test by the sign tool itself.
/// </summary>
/// <remarks>
/// These cover the rework away from <c>Signature.Verify</c>. That API answers "does this document contain
/// the signature I describe?" and, on PDF, records nothing at all unless the signer's certificate is
/// supplied - so it reported zero signatures for documents that plainly were signed. Verify now enumerates
/// with Search, which performs the same per-signature cryptographic check.
/// </remarks>
public class VerifyToolSignatureTests
{
    private const string CertificatePassword = "test-password";
    private static readonly string SamplePdfPath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "sample.pdf");

    private readonly Mock<IFileResolver> _resolver = new();
    private readonly Mock<ILicenseManager> _licenseManager = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly OutputHelper _output;
    private readonly Dictionary<string, byte[]> _written = [];

    public VerifyToolSignatureTests()
    {
        _output = new OutputHelper(_storage.Object, Microsoft.Extensions.Options.Options.Create(new McpConfig()));

        // Capture what sign produces so verify can read it back.
        _storage
            .Setup(s => s.WriteFileAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, byte[] bytes, bool _, CancellationToken _) =>
            {
                _written[name] = bytes;
                return name;
            });

        _storage
            .Setup(s => s.GetDownloadUrlAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    [Fact]
    public async Task Verify_FindsASignatureThatSearchAlsoFinds()
    {
        var signed = await SignWith(Valid());

        var verify = await VerifyJson(signed);
        var search = JsonDocument.Parse(Json(await SearchDigitalJson(signed))).RootElement;

        // The defect this replaces: search reported 1, verify reported 0, for the same file.
        Assert.Equal(search.GetProperty("found").GetInt32(), verify.GetProperty("found").GetInt32());
        Assert.True(verify.GetProperty("found").GetInt32() > 0);
        Assert.True(verify.GetProperty("isValid").GetBoolean());
        Assert.Empty(verify.GetProperty("certificateWarnings").EnumerateArray());
    }

    [Fact]
    public async Task Verify_ReportsAnExpiredCertificate()
    {
        var signed = await SignWith(Expired(), allowExpired: true);

        var verify = await VerifyJson(signed);

        Assert.True(verify.GetProperty("found").GetInt32() > 0);
        // Impossible before the rework: verify never surfaced a certificate at all.
        Assert.NotEmpty(verify.GetProperty("certificateWarnings").EnumerateArray());
        Assert.Contains("expired", verify.GetProperty("certificateWarnings")[0].GetString()!);
    }

    [Fact]
    public async Task SearchDigital_ReturnsReasonLocationAndContact()
    {
        var signed = await SignWith(Valid(),
            reason: "I approve this document", location: "Prague, CZ", contact: "qa@example.com");

        var search = JsonDocument.Parse(Json(await SearchDigitalJson(signed))).RootElement;
        var signature = search.GetProperty("signatures")[0];

        // These live on the PDF-specific subclass; reading only the base type dropped them silently.
        Assert.Equal("I approve this document", signature.GetProperty("reason").GetString());
        Assert.Equal("Prague, CZ", signature.GetProperty("location").GetString());
        Assert.Equal("qa@example.com", signature.GetProperty("contact").GetString());
    }

    [Fact]
    public async Task Verify_OnAnUnsignedDocument_ClaimsNothing()
    {
        GivenStoredFile("plain.pdf", await File.ReadAllBytesAsync(SamplePdfPath));

        var verify = await VerifyJson("plain.pdf");

        Assert.Equal(0, verify.GetProperty("found").GetInt32());
        // Null, not true: nothing was cryptographically checked, so there is no verdict to give.
        Assert.Equal(JsonValueKind.Null, verify.GetProperty("isValid").ValueKind);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private async Task<string> SignWith(byte[] pfx, bool allowExpired = false,
        string? reason = null, string? location = null, string? contact = null)
    {
        var pdf = await File.ReadAllBytesAsync(SamplePdfPath);
        GivenStoredFile("sample.pdf", pdf);
        GivenStoredFile("cert.pfx", pfx);

        await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "sample.pdf" }, type: "digital",
            certificate: new FileInput { FilePath = "cert.pfx" },
            certificatePassword: CertificatePassword,
            allowExpired: allowExpired,
            reason: reason, location: location, contact: contact);

        var produced = _written.Keys.FirstOrDefault(k => k.Contains("_signed"));
        Assert.NotNull(produced);

        GivenStoredFile(produced, _written[produced]);
        return produced;
    }

    private readonly Dictionary<string, byte[]> _stored = [];

    private void GivenStoredFile(string name, byte[] bytes)
    {
        _stored[name] = bytes;
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileInput input, CancellationToken _) =>
            {
                var key = input.FilePath ?? input.FileName ?? string.Empty;
                return new ResolvedFile { FileName = key, Stream = new MemoryStream(_stored[key]) };
            });
    }

    private async Task<JsonElement> VerifyJson(string fileName) =>
        JsonDocument.Parse(Json(await VerifyTool.Verify(
            _resolver.Object, _licenseManager.Object,
            new FileInput { FilePath = fileName }, type: "digital"))).RootElement;

    private Task<string> SearchDigitalJson(string fileName) =>
        SearchDigitalSignaturesTool.SearchDigitalSignatures(
            _resolver.Object, _licenseManager.Object, new FileInput { FilePath = fileName });

    /// <summary>Strips the evaluation-mode prefix the tools prepend when no licence is configured.</summary>
    private static string Json(string body)
    {
        var start = body.IndexOf('{');
        Assert.True(start >= 0, $"expected JSON, got: {body[..Math.Min(300, body.Length)]}");
        return body[start..];
    }

    private static byte[] Expired() =>
        CreatePfx(DateTimeOffset.UtcNow.AddYears(-3), DateTimeOffset.UtcNow.AddYears(-1));

    private static byte[] Valid() =>
        CreatePfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

    private static byte[] CreatePfx(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=GroupDocs MCP Verify Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));

        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return certificate.ExportPkcs12(Pkcs12ExportPbeParameters.Pbes2Aes256Sha256, CertificatePassword);
    }
}
