using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InventoryManagement.Licensing;

/// <summary>
/// Turns a <see cref="LicenseData"/> into the text stored in license.lic and
/// back. The file holds the JSON payload and an ECDSA P-256 signature over
/// those exact payload bytes - the signature is verified against the bytes
/// as they were decoded, so there is no JSON canonicalization to get wrong,
/// and changing a single character of the payload (customer name, machine
/// ID, anything) invalidates it.
///
/// Only the vendor holds the private key (see the License Generator tool);
/// the application ships with just the public key, which can verify a
/// license but never create one.
/// </summary>
public static class LicenseCodec
{
    public const string BeginMarker = "-----BEGIN INVENTORY MANAGEMENT LICENSE-----";
    public const string EndMarker = "-----END INVENTORY MANAGEMENT LICENSE-----";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public static string Encode(LicenseData license, ECDsa privateKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(license, SerializerOptions);
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256);

        var builder = new StringBuilder();
        builder.AppendLine(BeginMarker);
        builder.Append(Base64UrlEncode(payload)).Append('.').AppendLine(Base64UrlEncode(signature));
        builder.AppendLine(EndMarker);
        return builder.ToString();
    }

    /// <summary>
    /// Returns the license only if the text is well-formed AND its signature
    /// verifies against <paramref name="publicKey"/>. Never throws for bad
    /// input - a tampered or garbage file is an expected case, not an error.
    /// </summary>
    public static LicenseData? TryDecode(string licenseText, ECDsa publicKey, out string? error)
    {
        error = null;

        var body = licenseText
            .Replace(BeginMarker, string.Empty, StringComparison.Ordinal)
            .Replace(EndMarker, string.Empty, StringComparison.Ordinal)
            .Trim();

        var parts = body.Split('.');
        if (parts.Length != 2)
        {
            error = "The license file is not in the expected format.";
            return null;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Base64UrlDecode(parts[0]);
            signature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            error = "The license file is not in the expected format.";
            return null;
        }

        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256))
        {
            error = "The license file's signature is not valid. It may have been edited or was not issued for this product.";
            return null;
        }

        try
        {
            var license = JsonSerializer.Deserialize<LicenseData>(payload, SerializerOptions);
            if (license is null || license.FormatVersion != LicenseData.CurrentFormatVersion)
            {
                error = "The license file uses an unsupported format version.";
                return null;
            }

            return license;
        }
        catch (JsonException)
        {
            error = "The license file's contents could not be read.";
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var base64 = text.Trim().Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return Convert.FromBase64String(base64);
    }
}
