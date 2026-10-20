param([switch]$RunTests)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet restore HeadphoneAutoSwitch.csproj --configfile NuGet.Config --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet publish HeadphoneAutoSwitch.csproj -c Release --no-restore --self-contained false -o dist --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    # Remove only the old generated program files after publishing the renamed executable.
    foreach ($name in @('HeadphoneAutoSwitch.exe', 'HeadphoneAutoSwitch.dll', 'HeadphoneAutoSwitch.deps.json', 'HeadphoneAutoSwitch.runtimeconfig.json', 'HeadphoneAutoSwitch.pdb')) {
        $obsoletePath = Join-Path $PSScriptRoot "dist\$name"
        if (Test-Path -LiteralPath $obsoletePath) {
            try { Remove-Item -LiteralPath $obsoletePath -ErrorAction Stop }
            catch { Write-Warning "Close the older app version to remove its obsolete file: $obsoletePath" }
        }
    }
    Copy-Item -LiteralPath README.md,LICENSE,THIRD-PARTY-NOTICES.md -Destination dist
    if ($RunTests) {
        dotnet restore tests/HeadphoneAutoSwitch.Tests.csproj --configfile NuGet.Config --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Test restore failed' }
        dotnet run --project tests/HeadphoneAutoSwitch.Tests.csproj -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    }
} finally {
    Pop-Location
}
