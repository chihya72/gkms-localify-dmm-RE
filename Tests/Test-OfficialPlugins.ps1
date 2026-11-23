param(
    [Parameter(Mandatory = $true)][string]$OfficialPluginOutput,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Overlay
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$OfficialPluginOutput = (Resolve-Path -LiteralPath $OfficialPluginOutput).Path
$frameworkStage = Join-Path $repository "out\Framework\$Configuration"
[string[]]$arguments = @('build', (Join-Path $repository 'Gkms.sln'), '-c', $Configuration, '--nologo', '-v', 'quiet', "-p:StageRoot=$frameworkStage")
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Framework build failed: $LASTEXITCODE" }
[string[]]$arguments = @('build', (Join-Path $PSScriptRoot 'Gkms.Tests.csproj'), '-c', $Configuration, '--nologo', '-v', 'quiet', '-p:BuildProjectReferences=false', "-p:OfficialPluginOutput=$OfficialPluginOutput")
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Integration test build failed: $LASTEXITCODE" }
$work = Join-Path (Join-Path $repository 'out\Integration') ([Guid]::NewGuid().ToString('N'))
$runtime = Join-Path $work 'gakumas-local\re'
[void](New-Item -ItemType Directory -Path $runtime -Force)
foreach ($source in @((Join-Path $frameworkStage 'gakumas-local\re'), (Join-Path $OfficialPluginOutput 'gakumas-local\re'))) {
    foreach ($entry in Get-ChildItem -LiteralPath $source -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination $runtime -Recurse -Force
    }
}
Copy-Item -LiteralPath (Join-Path $OfficialPluginOutput 'plugin-packages') -Destination (Join-Path $work 'plugin-packages') -Recurse
$tests = Join-Path $PSScriptRoot "bin\$Configuration\net6.0\Gkms.Tests.dll"
$plugins = Join-Path $runtime 'plugins'
[string[]]$arguments = @($tests, $plugins, $work)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Integration suite failed: $LASTEXITCODE" }
if ($Overlay) {
    $parameters = @{ Configuration = $Configuration; Tests = $true }
    & (Join-Path $repository 'Bridge\Build-Native.ps1') @parameters
    if (-not $? -or $LASTEXITCODE -ne 0) { throw "Native fixture build failed: $LASTEXITCODE" }
    [string[]]$arguments = @($tests, '--overlay', $plugins, $work)
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Overlay verification failed: $LASTEXITCODE" }
}
Write-Output "Integration artifacts: $work"
