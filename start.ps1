$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'Chlorella.csproj'
$executablePath = Join-Path $PSScriptRoot 'bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\Chlorella.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK is required. Install it and try again.'
}

& dotnet build $projectPath -p:Platform=x64 -p:RuntimeIdentifier=win-x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Built executable not found: $executablePath"
}

Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path $executablePath)
