param(
    [ValidateSet('x86','x64')][string]$Arch,
    [string]$Version = '0.1.0',
    [string]$Output = 'out'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
$rid = if ($Arch -eq 'x86') {'win-x86'} else {'win-x64'}
$outRoot = Join-Path $root $Output
$publishDir = Join-Path $outRoot "publish-$Arch"
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish src/NajmCard.Desktop/NajmCard.Desktop.csproj --configuration Release --runtime $rid --self-contained true --output $publishDir -p:PublishReadyToRun=false -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid" }
$exe = Join-Path $publishDir 'NajmCard.exe'
if (!(Test-Path $exe)) { throw 'Published EXE missing' }
$sourceIcon=Join-Path $root 'src/NajmCard.Desktop/Assets/NajmCard.ico'
if (!(Test-Path $sourceIcon)) {throw 'NajmCard.ico missing before packaging'}
# Verify Win32 icon is embedded inside the executable, not only in the installer.
Add-Type -AssemblyName System.Drawing
$embedded=[System.Drawing.Icon]::ExtractAssociatedIcon($exe)
if ($null -eq $embedded) {throw "Embedded Windows icon missing in $exe"}
$embedded.Dispose()

# PE header check protects against accidentally labelling x64 packages as x86.
$bytes = [System.IO.File]::ReadAllBytes($exe)
if ($bytes.Length -lt 256 -or $bytes[0] -ne 77 -or $bytes[1] -ne 90) { throw 'Invalid Portable Executable' }
$offset = [BitConverter]::ToInt32($bytes, 0x3c)
if ($offset -lt 64 -or $offset -gt $bytes.Length-6 -or $bytes[$offset] -ne 80 -or $bytes[$offset+1] -ne 69) { throw 'Invalid PE header' }
$machine = [BitConverter]::ToUInt16($bytes, $offset+4)
$expected = if ($Arch -eq 'x86') { 0x14c } else { 0x8664 }
if ($machine -ne $expected) { throw "PE architecture mismatch: $machine != $expected" }

$zip = Join-Path $outRoot "NajmCard-v$Version-$Arch-Portable.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
if (!$iscc) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (!(Test-Path $iscc)) { throw "Inno Setup compiler ISCC.exe missing: $iscc" }
& $iscc "/DSourceDir=$publishDir" "/DArch=$Arch" "/DAppVersion=$Version" "/O$outRoot" "/FNajmCard-v$Version-$Arch-Installer" /Q installer/NajmCard.iss
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $Arch" }
$installer = Join-Path $outRoot "NajmCard-v$Version-$Arch-Installer.exe"
if (!(Test-Path $installer)) { throw "Installer missing: $installer" }
foreach ($f in @($zip, $installer)) { if ((Get-Item $f).Length -lt 100000) { throw "Package suspiciously small: $f" } }
@($zip,$installer) | ForEach-Object {
    $hash = (Get-FileHash -Algorithm SHA256 $_).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $_ -Leaf)"
} | Set-Content -Encoding ascii (Join-Path $outRoot "SHA256SUMS-$Arch.txt")
Write-Host "Successfully packaged $Arch Portable and Installer"
