param([ValidateSet('Debug','Release')][string]$Configuration='Release', [switch]$Tests)
$ErrorActionPreference='Stop'
$repoPath=Split-Path -Parent $PSScriptRoot
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
[string[]]$vsArguments=@('-latest','-products','*','-requires','Microsoft.VisualStudio.Component.VC.Tools.x86.x64','-property','installationPath')
$vsRoot=& $vswhere @vsArguments
if($LASTEXITCODE -ne 0) { throw "vswhere failed: $LASTEXITCODE" }
if(!$vsRoot) { throw 'Visual Studio C++ x64 build tools are required.' }
$vcRoot=Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$kitsRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk=Get-ChildItem -LiteralPath (Join-Path $kitsRoot 'Include') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$dotnetRoot=Join-Path $env:ProgramFiles 'dotnet'
$hostPack=Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Host.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$hostNative=Join-Path $hostPack.FullName 'runtimes\win-x64\native'
$output=Join-Path $PSScriptRoot "bin\$Configuration\net6.0"
$obj=Join-Path $PSScriptRoot "obj\$Configuration\net6.0\native"
if($Tests) { $obj=Join-Path $repoPath 'Tests\obj\native'; $output=$obj }
[void][IO.Directory]::CreateDirectory($obj)
[void][IO.Directory]::CreateDirectory($output)
$compiler=Join-Path $vcRoot.FullName 'bin\Hostx64\x64\cl.exe'
$savedInclude=$env:INCLUDE; $savedLib=$env:LIB
try {
    $env:INCLUDE=@((Join-Path $vcRoot.FullName 'include'),(Join-Path $sdk.FullName 'ucrt'),(Join-Path $sdk.FullName 'shared'),(Join-Path $sdk.FullName 'um'),(Join-Path $sdk.FullName 'winrt'),$hostNative,$PSScriptRoot) -join ';'
    $sdkLib=Join-Path $kitsRoot ('Lib\'+$sdk.Name)
    $env:LIB=@((Join-Path $vcRoot.FullName 'lib\x64'),(Join-Path $sdkLib 'ucrt\x64'),(Join-Path $sdkLib 'um\x64'),$hostNative) -join ';'
    $objects=@()
    foreach($file in @('buffer.c','hook.c','trampoline.c','hde64.c')) {
        $source=Join-Path $PSScriptRoot $file
        $object=Join-Path $obj ([IO.Path]::GetFileNameWithoutExtension($file)+'.obj')
        [string[]]$arguments=@('/nologo','/c','/O2','/MT','/W3',('/Fo'+$object),$source)
        & $compiler @arguments
        if($LASTEXITCODE -ne 0) { throw "Native C compilation failed: $file ($LASTEXITCODE)" }
        $objects+=,$object
    }
    $source=Join-Path $PSScriptRoot 'NativeLoader.cpp'
    if(!$Tests) {
        $assembler=Join-Path $vcRoot.FullName 'bin\Hostx64\x64\ml64.exe'
        $proxyObject=Join-Path $obj 'VersionProxy.obj'
        [string[]]$asmArguments=@('/nologo','/c',('/Fo'+$proxyObject),(Join-Path $PSScriptRoot 'VersionProxy.asm'))
        & $assembler @asmArguments
        if($LASTEXITCODE -ne 0) { throw "Proxy assembly failed: $LASTEXITCODE" }
        $objects+=,$proxyObject
        $overlayObject=Join-Path $obj 'overlay.obj'
        [string[]]$overlayArguments=@('/nologo','/c','/O2','/MT','/EHsc','/std:c++17','/W4',('/Fo'+$overlayObject),(Join-Path $PSScriptRoot 'NativeOverlay.cpp'))
        & $compiler @overlayArguments
        if($LASTEXITCODE -ne 0) { throw "Native overlay compilation failed: $LASTEXITCODE" }
        $objects+=,$overlayObject
    }
    $target=Join-Path $output 'version.dll'
    if($Tests) { $source=Join-Path $repoPath 'Tests\NativeFixture.cpp'; $target=Join-Path $output 'NativeFixture.dll' }
    [string[]]$arguments=@('/nologo','/LD','/O2','/MT','/EHsc','/std:c++17','/W4',('/Fo'+(Join-Path $obj 'loader.obj')), $source)+$objects+@('/link',('/OUT:'+$target),('/IMPLIB:'+(Join-Path $obj 'native.lib')),'/INCREMENTAL:NO','/OPT:NOICF','kernel32.lib','libnethost.lib','advapi32.lib','ole32.lib','shell32.lib','user32.lib','d3dcompiler.lib')
    if(!$Tests) { $arguments+=,('/DEF:'+(Join-Path $PSScriptRoot 'version.def')) }
    & $compiler @arguments
    if($LASTEXITCODE -ne 0) { throw "Native link failed: $LASTEXITCODE" }
    if($Tests) {
        [string[]]$probeArguments=@('/nologo','/O2','/MT','/EHsc','/std:c++17','/W4',('/Fo'+(Join-Path $obj 'probe.obj')),('/Fe'+(Join-Path $output 'NativeHostProbe.exe')),(Join-Path $repoPath 'Tests\NativeHostProbe.cpp'))+$objects+@('/link','/INCREMENTAL:NO','libnethost.lib','advapi32.lib','ole32.lib','shell32.lib')
        & $compiler @probeArguments
        if($LASTEXITCODE -ne 0) { throw "Native host test build failed: $LASTEXITCODE" }
        [string[]]$overlayTestArguments=@('/nologo','/LD','/O2','/MT','/EHsc','/std:c++17','/W4',('/Fo'+(Join-Path $obj 'overlay-fixture.obj')),(Join-Path $repoPath 'Tests\OverlayFixture.cpp'))+$objects+@('/link',('/OUT:'+(Join-Path $output 'OverlayFixture.dll')),('/IMPLIB:'+(Join-Path $obj 'overlay-fixture.lib')),'/INCREMENTAL:NO','user32.lib','d3d11.lib','d3dcompiler.lib')
        & $compiler @overlayTestArguments
        if($LASTEXITCODE -ne 0) { throw "Native overlay test build failed: $LASTEXITCODE" }
    }
} finally { $env:INCLUDE=$savedInclude; $env:LIB=$savedLib }
