namespace InventoryManagement.Licensing;

/// <summary>
/// The vendor's license-signing PUBLIC key, compiled into the application.
/// It can only verify licenses, never create them, so it is safe to ship.
///
/// It must match the private key in the vendor key file the License
/// Generator uses. If that key file is ever regenerated (`init-keys`), paste
/// the new public key printed by the tool here and rebuild - every license
/// issued with the old key then stops working.
/// </summary>
public static class ProductPublicKey
{
    public const string Pem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEzge8LOUW/ob8G7ycgyRdfGUb9MUc
        SDG/41gVMgRdJIgrtV5YYjdjX9LDUB8egRKcCDLJ+GG9eH8hWuY19aVeMw==
        -----END PUBLIC KEY-----
        """;
}
