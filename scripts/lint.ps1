$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $files = @(Get-ChildItem -Path $projectRoot -Filter '*.cs' -File | ForEach-Object { $_.Name })
    & dotnet format whitespace --folder $projectRoot --include $files --verify-no-changes --verbosity quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $longLines = 0
    foreach ($file in $files) {
        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadLines((Join-Path $projectRoot $file))) {
            $lineNumber++
            if ($line.Length -gt 140) {
                Write-Output ("{0}:{1}: line has {2} characters (limit: 140)" -f $file, $lineNumber, $line.Length)
                $longLines++
            }
        }
    }
    if ($longLines -gt 0) {
        Write-Output ("{0} lines exceed 140 characters." -f $longLines)
        exit 1
    }
}
finally {
    Pop-Location
}
