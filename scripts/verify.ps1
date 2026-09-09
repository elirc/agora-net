param(
    [ValidateSet('All', 'Catalog', 'Domain', 'Docs')]
    [string]$Suite = 'All'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    # Catch broken local learning links before running the more expensive checks.
    $documents = @(Get-Item 'README.md') + @(Get-ChildItem -Path 'docs', 'astradocs' -Recurse -Filter '*.md')
    $brokenLinks = @()
    foreach ($document in $documents) {
        $content = [IO.File]::ReadAllText($document.FullName)
        foreach ($match in [regex]::Matches($content, '\[[^\]]+\]\(([^\s)]+)\)')) {
            $target = $match.Groups[1].Value
            if ($target -match '^(https?://|mailto:|#)') { continue }
            $relativePath = ($target -split '#', 2)[0]
            $resolvedPath = Join-Path $document.DirectoryName $relativePath
            if (-not (Test-Path -LiteralPath $resolvedPath)) {
                $brokenLinks += "$($document.FullName): $target"
            }
        }
    }
    if ($brokenLinks.Count -gt 0) { throw ($brokenLinks -join [Environment]::NewLine) }
    Write-Host "Local file links checked in $($documents.Count) Markdown documents (anchors are not checked)."

    if ($Suite -eq 'Docs') { return }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { $null }
    if (-not $dotnetPath) {
        $userSdk = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.dotnet/dotnet.exe'
        if (Test-Path -LiteralPath $userSdk) { $dotnetPath = $userSdk }
    }
    if (-not $dotnetPath) { throw 'Install the .NET 10 SDK or add it to PATH; see docs/learning/01-first-hour.md.' }

    $arguments = @('test', 'Agora.slnx', '--nologo')
    if ($Suite -eq 'Catalog') { $arguments += @('--filter', 'FullyQualifiedName~CatalogSearchApiTests') }
    if ($Suite -eq 'Domain') {
        $arguments += @('--filter', 'FullyQualifiedName~MoneyTests|FullyQualifiedName~InventoryItemTests|FullyQualifiedName~OrderStateMatrixTests')
    }
    & $dotnetPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
