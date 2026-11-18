using System.IO.Compression;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using GroupDocs.Signature.Mcp.Tools;
using Moq;
using Xunit;

namespace GroupDocs.Signature.Mcp.Tests;

/// <summary>
/// Signing an archive signs the documents inside it, so searching must look inside too.
/// </summary>
/// <remarks>
/// The generic Search&lt;T&gt; overload flattens and returns nothing for an archive, which is why signing a
/// ZIP reported success while every later search came back empty. The archive is built here rather than
/// committed, so no binary fixture is needed.
/// </remarks>
public class ArchiveSearchTests
{
    private static readonly string SamplePdfPath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "sample.pdf");

    private readonly Mock<IFileResolver> _resolver = new();
    private readonly Mock<ILicenseManager> _licenseManager = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly OutputHelper _output;
    private readonly Dictionary<string, byte[]> _stored = [];

    public ArchiveSearchTests()
    {
        _output = new OutputHelper(_storage.Object, Microsoft.Extensions.Options.Options.Create(new McpConfig()));

        _storage
            .Setup(s => s.WriteFileAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, byte[] bytes, bool _, CancellationToken _) =>
            {
                _stored[name] = bytes;
                return name;
            });

        _storage
            .Setup(s => s.GetDownloadUrlAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<FileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileInput input, CancellationToken _) =>
            {
                var key = input.FilePath ?? input.FileName ?? string.Empty;
                return new ResolvedFile { FileName = key, Stream = new MemoryStream(_stored[key]) };
            });
    }

    [Fact]
    public async Task SearchText_InsideASignedArchive_ReportsEachDocument()
    {
        _stored["bundle.zip"] = BuildArchiveContaining("doc.pdf", await File.ReadAllBytesAsync(SamplePdfPath));

        await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "bundle.zip" }, type: "text", text: "InsideProbe");

        var signed = _stored.Keys.First(k => k.Contains("_signed"));
        var body = await SearchTextSignaturesTool.SearchTextSignatures(
            _resolver.Object, _licenseManager.Object, new FileInput { FilePath = signed });

        var json = JsonDocument.Parse(body[body.IndexOf('{')..]).RootElement;

        // The defect: this returned "No text signatures found" for a ZIP that had just been signed.
        Assert.DoesNotContain("No text signatures", body);
        Assert.True(json.GetProperty("archive").GetBoolean());

        var documents = json.GetProperty("documents").EnumerateArray().ToArray();
        Assert.NotEmpty(documents);
        Assert.Contains(documents, d => d.GetProperty("fileName").GetString() == "doc.pdf");

        // The signature text itself is not asserted: these tests run unlicensed, where evaluation mode
        // rewrites what a search returns. The live probe covers the content with a licence.
    }

    [Fact]
    public async Task SearchText_OnAnOrdinaryDocument_KeepsTheFlatShape()
    {
        _stored["plain.pdf"] = await File.ReadAllBytesAsync(SamplePdfPath);

        await SignTool.Sign(
            _resolver.Object, _storage.Object, _licenseManager.Object, _output,
            new FileInput { FilePath = "plain.pdf" }, type: "text", text: "FlatProbe");

        var signed = _stored.Keys.First(k => k.Contains("_signed"));
        var body = await SearchTextSignaturesTool.SearchTextSignatures(
            _resolver.Object, _licenseManager.Object, new FileInput { FilePath = signed });

        var json = JsonDocument.Parse(body[body.IndexOf('{')..]).RootElement;

        // Switching to the non-generic overload must not change what ordinary documents return.
        Assert.False(json.TryGetProperty("archive", out _));
        Assert.True(json.GetProperty("signatures").GetArrayLength() > 0);
    }

    private static byte[] BuildArchiveContaining(string entryName, byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var target = entry.Open();
            target.Write(content, 0, content.Length);
        }
        return buffer.ToArray();
    }
}
