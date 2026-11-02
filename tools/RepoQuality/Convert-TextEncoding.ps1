[CmdletBinding()]
param([string[]]$Path, [string]$ManifestPath, [switch]$Apply, [switch]$DryRun)
[IO.File]::WriteAllText($ManifestPath, '{"dryRun":false,"files":[{"status":"not-implemented","originalEncoding":null,"beforeSha256":null,"afterSha256":null,"chineseStringLiteralsUnchanged":false,"error":"NotImplemented"}]}', (New-Object Text.UTF8Encoding($false)))
Write-Output 'FAIL NotImplemented: lossless conversion is not implemented.'
exit 1
