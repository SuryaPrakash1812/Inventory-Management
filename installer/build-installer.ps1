# Builds the customer installer. Run it by double-clicking Build-Installer.cmd in the repository root.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $PSScriptRoot 'publish'
$output = Join-Path $PSScriptRoot 'output'

function Find-InnoCompiler {
    $folders = "${env:ProgramFiles(x86)}", $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs')
    Get-ChildItem -Path ($folders | ForEach-Object { Join-Path $_ 'Inno Setup *\ISCC.exe' }) -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}

$iscc = Find-InnoCompiler
if (-not $iscc) {
    Write-Host 'Inno Setup (free, needed once on this PC to build installers) is not installed.'
    if ((Read-Host 'Install it now with winget? [Y/N]') -notmatch '^[Yy]') {
        throw 'Inno Setup is required. Get it from https://jrsoftware.org/isinfo.php and try again.'
    }

    winget install --id JRSoftware.InnoSetup --exact --accept-package-agreements --accept-source-agreements
    $iscc = Find-InnoCompiler
    if (-not $iscc) {
        throw 'Inno Setup was not found after installing. Get it from https://jrsoftware.org/isinfo.php and try again.'
    }
}

$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
Write-Host "Building version $version..."

if (Test-Path $publish) {
    Remove-Item $publish -Recurse -Force
}

dotnet publish (Join-Path $root 'src\InventoryManagement.App\InventoryManagement.App.csproj') `
    -c Release -r win-x64 --self-contained true -p:DebugType=none -o $publish --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    throw 'Publishing the app failed - see the errors above.'
}

# Everything in this folder is sent to customers, so stop if anything private got in.
$private = Get-ChildItem $publish -Recurse -File -Include 'vendor-keys*', '*.lic', '*.key', '*.pfx', '*.db', '*LicenseGenerator*'
if ($private) {
    throw "Private files in the publish folder, not building:`n$($private.FullName -join "`n")"
}

foreach ($required in 'InventoryManagement.exe', 'e_sqlcipher.dll') {
    if (-not (Test-Path (Join-Path $publish $required))) {
        throw "$required is missing from the publish folder, not building."
    }
}

& $iscc /Qp (Join-Path $PSScriptRoot 'InventoryManagement.iss')
if ($LASTEXITCODE -ne 0) {
    throw 'Compiling the installer failed - see the errors above.'
}

$setup = Join-Path $output "InventoryManagement-Setup-$version.exe"
Write-Host "`nDone: $setup"
Start-Process explorer.exe "/select,`"$setup`""
