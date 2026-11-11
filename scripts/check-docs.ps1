#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$files = [System.Collections.Generic.List[string]]::new()
$files.Add((Join-Path $repoRoot 'README.md'))
$files.Add((Join-Path $repoRoot 'AGENTS.md'))
Get-ChildItem (Join-Path $repoRoot 'docs') -Filter '*.md' -File | ForEach-Object { $files.Add($_.FullName) }

$errors = [System.Collections.Generic.List[string]]::new()

foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file -Raw
    $lines = @(Get-Content -LiteralPath $file)
    $fenceCount = @($lines | Where-Object { $_ -match '^```' }).Count
    if ($fenceCount % 2 -ne 0) {
        $errors.Add("$file has an unbalanced code fence ($fenceCount fences).")
    }

    foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\(([^)]+)\)')) {
        $target = $match.Groups[1].Value
        if ($target -match '^https?://' -or $target.StartsWith('#')) {
            continue
        }

        $path = $target.Split('#')[0]
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $resolved = Join-Path (Split-Path -Parent $file) $path
        if (-not (Test-Path -LiteralPath $resolved)) {
            $errors.Add("$file links to missing '$target'.")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Checked $($files.Count) Markdown files: links and fences OK."
