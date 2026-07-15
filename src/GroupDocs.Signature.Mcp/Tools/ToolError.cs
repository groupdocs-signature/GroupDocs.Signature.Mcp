using System.Text;

namespace GroupDocs.Signature.Mcp.Tools;

// Shared descriptive-error formatter for the tool surface. Engine failures are
// surfaced as text (not MCP's opaque "An error occurred invoking '<tool>'") so
// AI agents and integration tests can read the cause. The text always starts
// with "<op> failed for '<file>'[<subjectSuffix>]: ..." — integration tests
// match that prefix (Pitfall #18).
internal static class ToolError
{
    public static string Format(string op, string file, Exception ex, string? subjectSuffix = null)
    {
        var sb = new StringBuilder();
        sb.Append($"{op} failed for '{file}'{subjectSuffix}: {ex.GetType().FullName}: {ex.Message}");
        var inner = ex.InnerException;
        for (int depth = 0; inner != null && depth < 5; depth++, inner = inner.InnerException)
        {
            sb.Append($" | inner({depth}): {inner.GetType().FullName}: {inner.Message}");
        }
        return sb.ToString();
    }
}
