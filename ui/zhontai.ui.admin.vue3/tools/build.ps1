[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$frontendRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$output = [IO.Path]::GetFullPath((Join-Path $frontendRoot 'dist'))
if ((Split-Path $output -Parent) -ne $frontendRoot -or (Split-Path $output -Leaf) -ne 'dist') {
    throw 'Build output must be the frontend dist directory.'
}
if (Test-Path -LiteralPath $output) {
    $directory = Get-Item -LiteralPath $output
    if ($directory.FullName -ne $output -or $directory.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Refusing to clean a linked or unexpected build output directory.'
    }
    # Clean only the verified generated output, including old dependency chunks.
    Remove-Item -LiteralPath $output -Recurse -Force
}
Push-Location $frontendRoot
try {
    & pnpm exec vite build
    exit $LASTEXITCODE
} finally { Pop-Location }
