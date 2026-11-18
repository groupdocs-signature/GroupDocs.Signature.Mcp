using GroupDocs.Signature.Domain;
using GroupDocs.Signature.Options;

namespace GroupDocs.Signature.Mcp.Tools;

/// <summary>
/// Searches a document, looking inside archives rather than past them.
/// </summary>
/// <remarks>
/// <c>Search&lt;T&gt;(options)</c> flattens its result and returns nothing for an archive, because the hits
/// sit one level down under a <see cref="DocumentResultSignature"/> per contained document. Signing a ZIP
/// therefore reported success while every later search of it came back empty.
/// <para>
/// The non-generic overload returns the same signatures for an ordinary document, so the tools can use it
/// for everything and only branch on what comes back.
/// </para>
/// </remarks>
internal static class ArchiveAwareSearch
{
    public static SearchResult Run(Signature signature, SearchOptions options) =>
        signature.Search([options]);

    /// <summary>Documents inside the archive, or empty when this is an ordinary document.</summary>
    public static List<(string FileName, List<T> Items)> Documents<T>(SearchResult result) where T : BaseSignature =>
        result.Succeeded.OfType<DocumentResultSignature>()
            .Select(d => (d.FileName, d.Succeeded.OfType<T>().ToList()))
            .ToList();

    /// <summary>Signatures of an ordinary document.</summary>
    public static List<T> Signatures<T>(SearchResult result) where T : BaseSignature =>
        result.Succeeded.OfType<T>().ToList();
}
