using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GroupDocs.Signature.Mcp.Tools;
using Xunit;

namespace GroupDocs.Signature.Mcp.Tests;

/// <summary>
/// Date logic behind the certificate flags in the search and verify tools. Pure comparisons, no engine.
/// </summary>
public class CertificateValidityTests
{
    private static readonly DateTime Now = new(2026, 10, 05, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public void Expired_IsDetected()
    {
        using var cert = Cert(Now.AddYears(-3), Now.AddYears(-1));

        Assert.True(CertificateValidity.IsExpired(cert, Now));
        Assert.False(CertificateValidity.IsNotYetValid(cert, Now));
    }

    [Fact]
    public void NotYetValid_IsDetected()
    {
        using var cert = Cert(Now.AddYears(1), Now.AddYears(3));

        Assert.False(CertificateValidity.IsExpired(cert, Now));
        Assert.True(CertificateValidity.IsNotYetValid(cert, Now));
    }

    [Fact]
    public void SignatureMadeInsideValidity_IsNotFlagged_EvenAfterTheCertificateExpires()
    {
        using var cert = Cert(Now.AddYears(-3), Now.AddYears(-1));
        var signedWhileValid = Now.AddYears(-2);

        Assert.True(CertificateValidity.WasValidWhenSigned(cert, signedWhileValid));

        // The certificate has expired by now, but the signature predates that. Saying it is invalid
        // would be a false alarm, so the wording must make clear the signature itself is fine.
        var described = CertificateValidity.Describe(cert, signedWhileValid, Now);
        Assert.NotNull(described);
        Assert.Contains("after this signature was made", described);
        Assert.DoesNotContain("not valid", described);
    }

    [Fact]
    public void SignatureMadeAfterExpiry_IsFlagged()
    {
        using var cert = Cert(Now.AddYears(-3), Now.AddYears(-1));

        Assert.False(CertificateValidity.WasValidWhenSigned(cert, Now.AddDays(-1)));

        var described = CertificateValidity.Describe(cert, Now.AddDays(-1), Now);
        Assert.NotNull(described);
        Assert.Contains("had already expired", described);
        Assert.Contains("not valid", described);
    }

    [Fact]
    public void SignatureMadeBeforeValidityStarts_IsFlagged()
    {
        using var cert = Cert(Now.AddDays(10), Now.AddYears(1));

        Assert.False(CertificateValidity.WasValidWhenSigned(cert, Now));

        var described = CertificateValidity.Describe(cert, Now, Now);
        Assert.NotNull(described);
        Assert.Contains("was not valid until", described);
        Assert.Contains("after this signature was made", described);
    }

    [Fact]
    public void UnspecifiedKindSignTime_IsTreatedAsUtc_NotLocal()
    {
        // Regression guard. The engine returns SignTime in UTC with Kind=Unspecified. Converting it as
        // local time subtracts the machine's offset and pushes the signature before the certificate's
        // start, reporting a perfectly valid signature as invalid. Only fails on machines east of UTC,
        // which is exactly why it escaped the first time.
        var notBefore = new DateTime(2026, 10, 05, 10, 00, 00, DateTimeKind.Utc);
        using var cert = Cert(notBefore, notBefore.AddYears(1));

        var signedJustAfterIssue = DateTime.SpecifyKind(
            new DateTime(2026, 10, 05, 11, 00, 00), DateTimeKind.Unspecified);

        Assert.True(CertificateValidity.WasValidWhenSigned(cert, signedJustAfterIssue));
        Assert.Null(CertificateValidity.Describe(cert, signedJustAfterIssue, Now));
    }

    [Fact]
    public void NoSignTime_ReportsTheCertificateStateInstead()
    {
        using var expired = Cert(Now.AddYears(-3), Now.AddYears(-1));

        Assert.Null(CertificateValidity.WasValidWhenSigned(expired, null));

        var described = CertificateValidity.Describe(expired, null, Now);
        Assert.NotNull(described);
        Assert.Contains("no sign time", described);
    }

    [Fact]
    public void HealthyCertificate_SaysNothing()
    {
        using var cert = Cert(Now.AddYears(-1), Now.AddYears(1));

        Assert.Null(CertificateValidity.Describe(cert, Now.AddDays(-1), Now));
        Assert.Null(CertificateValidity.Describe(cert, null, Now));
    }

    private static X509Certificate2 Cert(DateTime notBefore, DateTime notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Validity Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(new DateTimeOffset(notBefore), new DateTimeOffset(notAfter));
    }
}
