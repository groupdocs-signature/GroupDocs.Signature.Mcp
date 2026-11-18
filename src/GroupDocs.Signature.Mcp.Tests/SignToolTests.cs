using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Mcp.Tools;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GroupDocs.Signature.Mcp.Tests;

public class SignToolTests
{
    private readonly Mock<IFileResolver> _resolver = new();
    private readonly Mock<ILicenseManager> _licenseManager = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly OutputHelper _output;

    public SignToolTests()
    {
        _output = new OutputHelper(_storage.Object, Microsoft.Extensions.Options.Options.Create(new McpConfig()));
    }

    [Fact]
    public async Task Sign_WhenResolverThrows_PropagatesException()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("missing.pdf"));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            SignTool.Sign(
                _resolver.Object, _storage.Object, _licenseManager.Object, _output,
                new FileInput { FilePath = "missing.pdf" }, type: "text", text: "hello"));
    }

    [Fact]
    public async Task Sign_WhenResolverThrows_DoesNotWriteToStorage()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("missing.pdf"));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            SignTool.Sign(
                _resolver.Object, _storage.Object, _licenseManager.Object, _output,
                new FileInput { FilePath = "missing.pdf" }, type: "text", text: "hello"));

        _storage.Verify(
            s => s.WriteFileAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Sign_SetsLicense_BeforeResolving()
    {
        var sequence = new List<string>();

        _licenseManager.Setup(l => l.SetLicense()).Callback(() => sequence.Add("license"));
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("resolve"))
            .ThrowsAsync(new InvalidOperationException("short-circuit"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SignTool.Sign(
                _resolver.Object, _storage.Object, _licenseManager.Object, _output,
                new FileInput { FilePath = "anything.pdf" }, type: "text", text: "hello"));

        Assert.Equal(new[] { "license", "resolve" }, sequence);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("qrcode")]
    [InlineData("barcode")]
    public async Task Sign_WithoutText_IsRejected_AndWritesNothing(string type)
    {
        GivenAnyResolvableFile();

        var result = await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "doc.pdf" }, type: type);

        // It used to sign the document with the literal word "Signed" and save it.
        Assert.Contains("requires text", result);
        Assert.Contains("'text' parameter", result);
        ThenNothingWasWritten();
    }

    [Fact]
    public async Task Sign_WithBlankText_IsRejected()
    {
        GivenAnyResolvableFile();

        var result = await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "doc.pdf" }, type: "text", text: "   ");

        // Whitespace would otherwise sign an invisible mark.
        Assert.Contains("requires text", result);
        ThenNothingWasWritten();
    }

    [Fact]
    public async Task Sign_UnknownType_ReportsTheType_NotTheMissingText()
    {
        GivenAnyResolvableFile();

        var result = await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "doc.pdf" }, type: "banana");

        Assert.Contains("Unknown signature type", result);
        Assert.DoesNotContain("requires text", result);
        ThenNothingWasWritten();
    }

    [Fact]
    public async Task Sign_WithAFontName_PassesItToTheEngine()
    {
        GivenRealDocument();

        var result = await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "sample.pdf" }, type: "text", text: "hello",
            font: "NoSuchFontXYZ");

        // A font name the machine does not have proves the parameter reached the engine - and does so
        // without depending on which fonts happen to be installed on the build agent.
        Assert.Contains("NoSuchFontXYZ", result);
        ThenNothingWasWritten();
    }

    [Fact]
    public async Task Sign_QrPayload_WhenWhatWasEncodedDiffers_IsRefused()
    {
        // Licensed path: the tool reads the QR back and compares. Here the comparison fails because the
        // unlicensed barcode reader rewrites what it returns - which is exactly the mismatch branch under
        // test. The real corruption case is covered by the live probe, which needs a licence.
        _licenseManager.Setup(l => l.IsLicensed).Returns(true);
        GivenRealDocument();

        var result = await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "sample.pdf" }, type: "qrcode",
            text: "— 数字签名");

        Assert.Contains("QR payload refused", result);
        ThenNothingWasWritten();
    }

    [Theory]
    [InlineData("数字签名")]          // CJK alone
    [InlineData("数字签名 —")]   // the same symbol AFTER the CJK round-trips fine
    [InlineData("— 数字签名")]   // even the corrupting shape, when unlicensed
    public async Task Sign_QrPayload_WithoutALicence_IsNeverRefused(string payload)
    {
        // Without a licence the reader cannot be trusted, so verification is skipped rather than refusing
        // everything. The old predicate had no such gate and refused payloads that were perfectly fine.
        GivenRealDocument();

        var result = await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "sample.pdf" }, type: "qrcode", text: payload);

        Assert.DoesNotContain("QR payload refused", result);
    }

    private void GivenRealDocument()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "sample.pdf"));
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ResolvedFile { FileName = "sample.pdf", Stream = new MemoryStream(bytes) });
    }

    private void GivenAnyResolvableFile() =>
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ResolvedFile { FileName = "doc.pdf", Stream = new MemoryStream([1, 2, 3]) });

    private void ThenNothingWasWritten() =>
        _storage.Verify(
            s => s.WriteFileAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task Sign_PassesFileInputToResolver_Unchanged()
    {
        var input = new FileInput { FilePath = "doc.docx" };
        FileInput? captured = null;

        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .Callback<FileInput, CancellationToken>((fi, _) => captured = fi)
            .ThrowsAsync(new InvalidOperationException("short-circuit"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SignTool.Sign(
                _resolver.Object, _storage.Object, _licenseManager.Object, _output,
                input, type: "text", text: "hello"));

        Assert.Same(input, captured);
    }
}
