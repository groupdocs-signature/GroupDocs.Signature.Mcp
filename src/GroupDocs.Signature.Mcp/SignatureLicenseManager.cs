using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroupDocs.Signature.Mcp;

public class SignatureLicenseManager : LicenseManager
{
    public SignatureLicenseManager(IOptions<McpConfig> config, ILogger<LicenseManager> logger)
        : base(config, logger)
    {
    }

    protected override void SetLicenseFromPath(string licensePath)
    {
        new GroupDocs.Signature.License().SetLicense(licensePath);
    }
}
