param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src\MusicWave\MusicWave.csproj'
$outputPath = Join-Path $PSScriptRoot 'artifacts\MusicWave'
if ($Portable) {
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outputPath
} else {
    dotnet build $projectPath -c Release
}
if ($LASTEXITCODE -ne 0) { throw '构建失败' }
if ($Portable) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $outputPath '使用说明.md')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.txt') -Destination $outputPath
    $nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $runtimePackages = @(
        @{ Name = 'microsoft.netcore.app.runtime.win-x64'; Prefix = 'DOTNET' },
        @{ Name = 'microsoft.windowsdesktop.app.runtime.win-x64'; Prefix = 'WINDOWS_DESKTOP' }
    )
    foreach ($package in $runtimePackages) {
        $runtimePath = Get-ChildItem -LiteralPath (Join-Path $nugetPackages $package.Name) -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if ($null -ne $runtimePath) {
            $license = Join-Path $runtimePath.FullName 'LICENSE.TXT'
            if (-not (Test-Path -LiteralPath $license)) { $license = Join-Path $runtimePath.FullName 'LICENSE' }
            $notices = Join-Path $runtimePath.FullName 'THIRD-PARTY-NOTICES.TXT'
            if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination (Join-Path $outputPath ($package.Prefix + '_LICENSE.txt')) }
            if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination (Join-Path $outputPath ($package.Prefix + '_THIRD_PARTY_NOTICES.txt')) }
        }
    }
    Write-Output "免安装程序：$outputPath\MusicWave.exe"
}
