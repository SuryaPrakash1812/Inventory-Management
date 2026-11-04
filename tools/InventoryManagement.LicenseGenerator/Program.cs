using InventoryManagement.Licensing;

// Vendor-only command-line tool for issuing customer licenses.
// See docs\Licensing.md for the full workflow.

return args.Length == 0 ? Usage() : args[0].ToLowerInvariant() switch
{
    "init-keys" => InitKeys(Options(args)),
    "issue" => Issue(Options(args)),
    "verify" => Verify(Options(args)),
    "show-public-key" => ShowPublicKey(Options(args)),
    "machine-id" => PrintMachineId(),
    _ => Usage(),
};

static int PrintMachineId()
{
    // The same value the app's activation screen shows on this PC - handy
    // for licensing the vendor's own test/demo machine.
    Console.WriteLine(MachineIdentity.GetCurrentMachineId());
    return 0;
}

static int InitKeys(Dictionary<string, string> options)
{
    var path = options.GetValueOrDefault("keys") ?? "vendor-keys.json";
    if (File.Exists(path) && !options.ContainsKey("force"))
    {
        Console.Error.WriteLine($"'{path}' already exists. Regenerating keys invalidates EVERY license already issued.");
        Console.Error.WriteLine("Pass --force true if that is really what you want.");
        return 1;
    }

    var keys = VendorKeys.CreateNew();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    keys.Save(path);

    Console.WriteLine($"Vendor keys written to {Path.GetFullPath(path)}");
    Console.WriteLine("Keep this file private and backed up. Paste this public key into");
    Console.WriteLine(@"src\InventoryManagement.Licensing\ProductPublicKey.cs and rebuild the app:");
    Console.WriteLine();
    Console.WriteLine(keys.PublicKeyPem);
    return 0;
}

static int Issue(Dictionary<string, string> options)
{
    if (!options.TryGetValue("customer-id", out var customerId)
        || !options.TryGetValue("customer-name", out var customerName)
        || !options.TryGetValue("machine-id", out var machineId))
    {
        Console.Error.WriteLine("issue requires --customer-id, --customer-name and --machine-id.");
        return Usage();
    }

    var keys = VendorKeys.Load(options.GetValueOrDefault("keys") ?? "vendor-keys.json");
    var output = options.GetValueOrDefault("out") ?? "license.lic";

    string licenseText;
    try
    {
        licenseText = keys.Issue(customerId, customerName, machineId, DateTimeOffset.UtcNow);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    File.WriteAllText(output, licenseText);
    Console.WriteLine($"License for '{customerName}' ({customerId}) on machine {machineId} written to {Path.GetFullPath(output)}");
    return 0;
}

static int Verify(Dictionary<string, string> options)
{
    if (!options.TryGetValue("license", out var licensePath) || !options.TryGetValue("machine-id", out var machineId))
    {
        Console.Error.WriteLine("verify requires --license and --machine-id.");
        return Usage();
    }

    var publicKey = options.TryGetValue("keys", out var keysPath)
        ? VendorKeys.Load(keysPath).PublicKeyPem
        : ProductPublicKey.Pem;

    var result = new LicenseValidator(publicKey).Validate(File.ReadAllText(licensePath), machineId);
    if (!result.IsValid)
    {
        Console.Error.WriteLine($"INVALID: {result.Error}");
        return 1;
    }

    var license = result.License!;
    Console.WriteLine($"VALID: license {license.LicenseId} for '{license.CustomerName}' ({license.CustomerId}), "
        + $"machine {license.MachineId}, issued {license.IssuedAtUtc:u}");
    return 0;
}

static int ShowPublicKey(Dictionary<string, string> options)
{
    Console.WriteLine(VendorKeys.Load(options.GetValueOrDefault("keys") ?? "vendor-keys.json").PublicKeyPem);
    return 0;
}

static Dictionary<string, string> Options(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < args.Length - 1; i += 2)
    {
        options[args[i].TrimStart('-')] = args[i + 1];
    }

    return options;
}

static int Usage()
{
    Console.WriteLine("""
        Inventory Management license generator (VENDOR ONLY - never ship to customers)

          init-keys        [--keys vendor-keys.json] [--force true]
          issue            --customer-id C001 --customer-name "Acme Traders" --machine-id XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
                           [--keys vendor-keys.json] [--out license.lic]
          verify           --license license.lic --machine-id XXXXX-... [--keys vendor-keys.json]
          show-public-key  [--keys vendor-keys.json]
          machine-id       (prints THIS PC's Machine ID)

        Re-issuing for the same --customer-id (e.g. after a PC replacement) keeps
        the same backup key, so that customer's existing backups still restore.
        """);
    return 1;
}
