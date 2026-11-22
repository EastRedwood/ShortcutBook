# Builds publish\installer\ShortcutBook-Setup-<version>.exe
# Needs: .NET SDK 10, Inno Setup 6 (winget install JRSoftware.InnoSetup)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$build = (Get-Content (Join-Path $root '.build_number') -Raw).Trim()
$version = "1.0.$build"

# SkipShellRegister: publishing must not re-point the user's Explorer registration at the build folder.
dotnet publish ShortcutBook.csproj -c Release -r win-x64 --self-contained true -p:SkipShellRegister=true -o publish\app
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found' }

& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'ShortcutBook.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
Get-Item (Join-Path $root "publish\installer\ShortcutBook-Setup-$version.exe") | Select-Object FullName, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
