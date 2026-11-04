# Licensing and data protection

Each copy of Inventory Management only works on the one PC it is licensed for.
If a customer gives the program, its data or its license to someone else,
none of it works for that person.

| What gets copied to another PC | What happens there |
|---|---|
| The program folder | Activation screen. The other PC has a different Machine ID. |
| `license.lic` | Rejected: "This license was issued for a different PC." |
| `inventory.db` (with or without `inventory.key`) | Cannot be opened. The database is encrypted with SQLCipher, and its key is protected by Windows DPAPI on the original PC only. DB Browser for SQLite cannot open it either. |
| A backup file (`*.imbak`) | Cannot be restored or opened unless that PC is licensed to the **same customer**. |
| An edited license (for example, a changed Machine ID or customer name) | Rejected: the signature no longer matches. |

## How it works

- **Machine ID.** The app shows a 25-character ID, for example
  `TTA6K-V8MAK-SEAUG-TYN2V-MYLSM`. It is a hash of the Windows installation ID
  (MachineGuid) plus the BIOS manufacturer and model. It changes if Windows is
  reinstalled or the PC is replaced.
- **License file.** The vendor signs the license with an ECDSA P-256
  **private** key. The app contains only the **public** key
  (`src/InventoryManagement.Licensing/ProductPublicKey.cs`). The public key can
  check a license but can never create one.
- **Database key.** A random 256-bit key is created on first run and stored in
  `%AppData%\InventoryManagement\inventory.key`, encrypted with DPAPI
  (machine scope). If a database from an older, unencrypted version is found,
  it is encrypted automatically on the first run.
- **Backup key.** Each license carries a backup key derived from the vendor
  secret and the **Customer ID**. Every license issued to the same Customer ID
  has the same backup key. That is why a customer's own backups still restore
  after a PC replacement.

## Vendor workflow

All commands run from the repository root. The generator is a vendor-only
tool: **never ship it or `keys\vendor-keys.json` to customers.**

### One-time setup (already done)

```powershell
dotnet run --project tools\InventoryManagement.LicenseGenerator -- init-keys --keys keys\vendor-keys.json
```

This prints a public key. Paste it into `ProductPublicKey.cs` and rebuild.
The keys in this repository were generated on 2026-10-31.

Back up `keys\vendor-keys.json` somewhere safe and offline, such as an
encrypted USB drive or a password manager. If it is lost, no new licenses can
be issued. If it leaks, anyone can issue licenses. Do not run `init-keys`
again: a new key pair invalidates every license already issued.

### Issuing a license for a customer

1. The customer installs the app and starts it. The activation screen shows
   their **Machine ID**, with a Copy button. They send it to you.
2. You run:

   ```powershell
   dotnet run --project tools\InventoryManagement.LicenseGenerator -- issue --keys keys\vendor-keys.json --customer-id C001 --customer-name "Acme Traders" --machine-id TTA6K-V8MAK-SEAUG-TYN2V-MYLSM --out licenses\acme-traders.lic
   ```

   - `--customer-id`: your own permanent ID for this customer. **Always use the
     same ID for the same customer**, because it determines their backup key.
   - `--machine-id`: exactly what the customer sent. Case and dashes don't matter.
3. Optional check before sending:

   ```powershell
   dotnet run --project tools\InventoryManagement.LicenseGenerator -- verify --license licenses\acme-traders.lic --machine-id TTA6K-V8MAK-SEAUG-TYN2V-MYLSM
   ```

4. Send the `.lic` file to the customer. On the activation screen they click
   **Load License File...** and select it. The app starts, and the license is
   kept in `%AppData%\InventoryManagement\license.lic`.

### Customer replaces their PC or reinstalls Windows

1. They take a backup on the old PC, if it still works (or use an automatic
   backup from their backup folder).
2. They install the app on the new PC and send you the new Machine ID.
3. You issue a license with the **same `--customer-id`** and the new Machine ID.
4. They activate, then use **Backup > Restore From File...** and restart the app.

### Your own test PC

```powershell
dotnet run --project tools\InventoryManagement.LicenseGenerator -- machine-id
```

This prints this PC's Machine ID. Issue a license for it as shown above.

## Limits (be aware)

This protection stops casual sharing and copying, which is the realistic
threat. A skilled attacker with full control of a licensed PC could still
reverse-engineer a .NET application, for example to patch out the license
check. To raise that bar, add code obfuscation and Authenticode signing in the
packaging stage.
