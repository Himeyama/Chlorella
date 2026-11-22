$ErrorActionPreference = 'Stop'

$version = (& dotnet msbuild ./Chlorella.csproj -nologo -getProperty:Version -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'Could not read a valid application version.'
}
foreach ($component in $version.Split('.')) {
    if ([uint64]$component -gt 65535) { throw 'A version component exceeds 65535.' }
}
if ($env:GITHUB_REF -like 'refs/tags/*' -and $env:GITHUB_REF_NAME -ne "v$version") {
    throw "Tag $env:GITHUB_REF_NAME does not match version $version."
}

$publishDir = Join-Path $PWD 'artifacts/publish-release'
$distributionDir = Join-Path $PWD 'artifacts/distribution'
$installerDir = Join-Path $PWD 'artifacts/installer-release'
if ((Test-Path $publishDir) -or (Test-Path $distributionDir) -or (Test-Path $installerDir)) {
    throw 'Release output directories must be empty.'
}
dotnet publish ./Chlorella.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
foreach ($file in @('Chlorella.exe', 'Chlorella.pri', 'App.xbf', 'MainWindow.xbf', 'Assets/AppIcon.ico')) {
    if (!(Test-Path (Join-Path $publishDir $file))) { throw "Missing published file: $file" }
}

$hsisRoot = Join-Path (Split-Path $PWD) 'hsis'
$hsisDir = Join-Path $hsisRoot 'artifacts/cli'
dotnet publish (Join-Path $hsisRoot 'src/Hsis.Cli/Hsis.Cli.csproj') -c Release -r win-x64 --self-contained true -o $hsisDir
if ($LASTEXITCODE -ne 0) { throw 'HSIS CLI publish failed.' }
Copy-Item (Join-Path $hsisRoot 'src/Hsis.App/Hsis.ico') (Join-Path $hsisDir 'Template/src/Hsis.App/Hsis.ico')
& (Join-Path $hsisDir 'hsis.exe') build ./installer/Chlorella.nsi --output $installerDir "-DAPP_VERSION=$version" "-DSOURCE_DIR=$publishDir"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$setups = @(Get-ChildItem $installerDir -Recurse -File -Filter "Chlorella-$version-win-x64-Setup.exe")
if ($setups.Count -ne 1) { throw 'Expected exactly one setup executable.' }
$setup = $setups[0]
New-Item -ItemType Directory -Path $distributionDir | Out-Null
Copy-Item $setup.FullName $distributionDir
$setupPath = Join-Path $distributionDir $setup.Name
$hash = (Get-FileHash $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$setupPath.sha256" -Value "$hash  $($setup.Name)" -Encoding ascii
