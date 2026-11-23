param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$properties = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)
$version = [string]$properties.Project.PropertyGroup.Version
$stage = Join-Path $PSScriptRoot ('out\Package\' + [Guid]::NewGuid().ToString('N'))
$parameters = @{ Configuration = $Configuration; StageRoot = $stage }
& (Join-Path $PSScriptRoot 'Build.ps1') @parameters
if (-not $? -or $LASTEXITCODE -ne 0) { throw 'Framework build failed.' }
$destination = Join-Path $PSScriptRoot 'out\Release'
[void][IO.Directory]::CreateDirectory($destination)
$archive = Join-Path $destination "Gkms-RE-$version-win-x64.zip"
$temporary = $archive + '.tmp'
if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporary)
Move-Item -LiteralPath $temporary -Destination $archive -Force
Write-Output "Framework release: $archive"
Write-Output ('SDK release: ' + (Join-Path $PSScriptRoot "out\sdk\Gkms.Sdk.$version.nupkg"))
