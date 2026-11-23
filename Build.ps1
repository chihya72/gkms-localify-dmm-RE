param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$StageRoot,
    [string]$SdkOutput
)
$ErrorActionPreference = 'Stop'
if (-not $StageRoot) { $StageRoot = Join-Path $PSScriptRoot "out\Framework\$Configuration" }
if (-not $SdkOutput) { $SdkOutput = Join-Path $PSScriptRoot 'out\sdk' }
$StageRoot = [IO.Path]::GetFullPath($StageRoot)
$SdkOutput = [IO.Path]::GetFullPath($SdkOutput)
[string[]]$arguments = @('build', (Join-Path $PSScriptRoot 'Gkms.sln'), '-c', $Configuration, '--nologo', '-v', 'minimal', "-p:StageRoot=$StageRoot")
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Framework build failed: $LASTEXITCODE" }
[string[]]$arguments = @('pack', (Join-Path $PSScriptRoot 'Sdk\Gkms.Sdk.csproj'), '-c', $Configuration, '--no-build', '--no-restore', '--nologo', '-o', $SdkOutput)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "SDK package failed: $LASTEXITCODE" }
Write-Output "Framework: $StageRoot"
Write-Output "SDK feed: $SdkOutput"
