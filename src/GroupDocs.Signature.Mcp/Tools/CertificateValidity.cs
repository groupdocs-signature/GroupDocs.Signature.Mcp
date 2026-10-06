using System.Globalization;
using System.Security.Cryptography.X509Certificates;

namespace GroupDocs.Signature.Mcp.Tools;

/// <summary>
/// Reports whether a signing certificate was inside its validity period, for the search and verify tools.
/// </summary>
/// <remarks>
/// The engine itself only enforces this when signing (<c>DigitalSignOptions.AllowExpired</c> /
/// <c>AllowNotYetValid</c>). When reading an existing document it hands over the certificate dates and
/// says nothing about them, so the tools have to draw the conclusion themselves.
/// <para>
/// The distinction that matters is <b>expired now</b> versus <b>expired when the signature was made</b>.
/// A signature made while the certificate was valid stays valid after the certificate expires - reporting
/// that as a problem would be a false alarm. Only a signature made outside the validity period is one.
/// </para>
/// </remarks>
internal static class CertificateValidity
{
    /// <summary>Prefix used wherever a certificate warning is surfaced, including the sign tool.</summary>
    public const string WarningPrefix = "Certificate warning: ";

    public static bool IsExpired(X509Certificate2 certificate, DateTime utcNow) =>
        certificate.NotAfter.ToUniversalTime() < utcNow;

    public static bool IsNotYetValid(X509Certificate2 certificate, DateTime utcNow) =>
        certificate.NotBefore.ToUniversalTime() > utcNow;

    /// <summary>
    /// Whether the signature was made inside the certificate's validity period, or <c>null</c> when the
    /// document carries no usable sign time.
    /// </summary>
    public static bool? WasValidWhenSigned(X509Certificate2 certificate, DateTime? signTime)
    {
        var signed = ToUtc(signTime);
        if (signed == null) return null;

        return signed.Value >= certificate.NotBefore.ToUniversalTime()
            && signed.Value <= certificate.NotAfter.ToUniversalTime();
    }

    /// <summary>
    /// A human-readable warning, or <c>null</c> when there is nothing worth saying. Wording follows the
    /// engine's own messages so that output is consistent with what signing reports.
    /// </summary>
    public static string? Describe(X509Certificate2 certificate, DateTime? signTime, DateTime utcNow)
    {
        var who = $"(subject \"{certificate.Subject}\", thumbprint {certificate.Thumbprint})";
        var notAfter = Format(certificate.NotAfter);
        var notBefore = Format(certificate.NotBefore);
        var signed = ToUtc(signTime);

        if (signed != null)
        {
            if (signed.Value > certificate.NotAfter.ToUniversalTime())
                return $"The signing certificate had already expired on {notAfter} UTC when this signature "
                     + $"was made on {Format(signed.Value)} UTC {who}. Validators report such a signature as not valid.";

            if (signed.Value < certificate.NotBefore.ToUniversalTime())
                return $"The signing certificate was not valid until {notBefore} UTC, which is after this "
                     + $"signature was made on {Format(signed.Value)} UTC {who}. Validators report such a signature as not valid.";

            // Valid when signed. Worth mentioning only if it has since expired, and explicitly not an alarm.
            if (IsExpired(certificate, utcNow))
                return $"The signing certificate expired on {notAfter} UTC, after this signature was made on "
                     + $"{Format(signed.Value)} UTC {who}. The signature was made inside the certificate's validity period.";

            return null;
        }

        // No sign time to compare against: report the certificate's state as of now.
        if (IsExpired(certificate, utcNow))
            return $"The signing certificate expired on {notAfter} UTC {who}. "
                 + "The document carries no sign time, so it cannot be told whether the signature predates the expiry.";

        if (IsNotYetValid(certificate, utcNow))
            return $"The signing certificate is not valid until {notBefore} UTC {who}. "
                 + "Validators report a signature made before that date as not valid.";

        return null;
    }

    /// <summary>
    /// Normalises a sign time to UTC, treating the unset default as "not recorded".
    /// </summary>
    /// <remarks>
    /// The engine reports <c>SignTime</c> already in UTC but with <see cref="DateTimeKind.Unspecified"/>.
    /// Running <c>ToUniversalTime</c> on it would subtract the machine's offset a second time and shift the
    /// value into the past - on a UTC+3 machine that was enough to report a perfectly valid signature as
    /// made before its certificate existed. An unspecified kind is therefore taken as UTC, not as local.
    /// </remarks>
    private static DateTime? ToUtc(DateTime? value)
    {
        if (value == null || value.Value == default || value.Value == DateTime.MinValue) return null;

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
