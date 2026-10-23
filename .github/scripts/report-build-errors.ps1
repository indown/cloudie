# Prints every distinct compiler error from an MSBuild "ErrorsOnly" file log, both into the
# job summary and as annotations. GitHub shows at most ten error annotations per step, so
# several errors are packed into each annotation instead of one per error.
param([string]$LogFile = "build-errors.log")

# The csc problem matcher would otherwise re-parse the lines printed below and use up the cap.
Write-Output "::remove-matcher owner=csc::"

if (-not (Test-Path $LogFile)) {
    Write-Host "No $LogFile; the failure happened before the compiler ran."
    exit 0
}

$root = [regex]::Escape((Get-Location).Path + [IO.Path]::DirectorySeparatorChar)
$errors = @(Get-Content $LogFile |
    Where-Object { $_ -match ':\s+error\s+' } |
    ForEach-Object { (($_ -replace $root, '') -replace '\s*\[[^\]]*\]\s*$', '').Trim() } |
    Sort-Object -Unique)

if ($errors.Count -eq 0) {
    Write-Host "No compiler errors in $LogFile."
    exit 0
}

Write-Host "$($errors.Count) distinct compiler error(s):"
$errors | ForEach-Object { Write-Host "  $_" }

if ($env:GITHUB_STEP_SUMMARY) {
    $fence = '```'
    $summary = "### Compiler errors ($($errors.Count))`n`n${fence}text`n$($errors -join "`n")`n${fence}"
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $summary
}

$perAnnotation = 12
for ($start = 0; $start -lt $errors.Count; $start += $perAnnotation) {
    $end = [Math]::Min($start + $perAnnotation, $errors.Count) - 1
    $message = ($errors[$start..$end] -join "`n") -replace '%', '%25' -replace "`r", '%0D' -replace "`n", '%0A'
    Write-Output "::error title=Compiler errors $($start + 1)-$($end + 1) of $($errors.Count)::$message"
}
