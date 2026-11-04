<#
多個樣本各自建置於本次暫存資料夾：簽章完整的 net462 樣本必須通過，net48 樣本必須被拒絕。
一次性 RSA 測試金鑰不讀正式環境變數，不輸出內容，完成或失敗均釋放並刪除整個專屬資料夾。
樣本只供 PE 解析，絕不載入執行；斷言結果內容而非只看結束碼，避免任意失敗造成假綠燈。
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('speechmessage-packaging-' + [Guid]::NewGuid().ToString('N'))))
$null = New-Item -ItemType Directory -Path $fixtureRoot
$utf8 = New-Object Text.UTF8Encoding($false)
$failures = 0
try {
    $keyPath = Join-Path $fixtureRoot 'fixture.snk'
    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
    try {
        $rsa.PersistKeyInCsp = $false
        [IO.File]::WriteAllBytes($keyPath, $rsa.ExportCspBlob($true))
    } finally { $rsa.Dispose() }
    $escapedKey = [Security.SecurityElement]::Escape($keyPath)
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <SignAssembly>true</SignAssembly>
    <AssemblyOriginatorKeyFile>$escapedKey</AssemblyOriginatorKeyFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@
    $source = @'
namespace SpeechMessage.Dynamics.Plugins.Diagnostics
{
    /// <summary>Inert metadata-only fixture. No code is executed.</summary>
    public sealed class DiagnosticsPingPlugin { }
}
'@
    $projectPath = Join-Path $fixtureRoot 'Fixture.csproj'
    [IO.File]::WriteAllText($projectPath, ($project -replace "`r?`n", "`r`n") + "`r`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Fixture.cs'), ($source -replace "`r?`n", "`r`n") + "`r`n", $utf8)
    $samples = @('signed', 'unsigned', 'wrong-framework', 'legacy-net48', 'missing-plugin', 'unexpected-reference', 'drawing', 'oversize', 'invalid-image')
    foreach ($sample in $samples) {
        $signed = $sample -ne 'unsigned'
        $sampleOutput = Join-Path $fixtureRoot $sample
        $sampleProject = $project
        $sampleSource = $source
        $failedCheck = switch ($sample) {
            'unsigned' { 'strongName' }
            'wrong-framework' { 'targetFramework' }
            'legacy-net48' { 'targetFramework' }
            'missing-plugin' { 'diagnosticsPlugin' }
            'unexpected-reference' { 'allowedReferences' }
            'drawing' { 'noDrawing' }
            'oversize' { 'size' }
            'invalid-image' { 'readable' }
        }
        if ($sample -eq 'wrong-framework') { $sampleProject = $sampleProject.Replace('<TargetFramework>net462</TargetFramework>', '<TargetFramework>net10.0</TargetFramework>') }
        if ($sample -eq 'legacy-net48') { $sampleProject = $sampleProject.Replace('<TargetFramework>net462</TargetFramework>', '<TargetFramework>net48</TargetFramework>') }
        if ($sample -eq 'missing-plugin') { $sampleSource = $sampleSource.Replace('DiagnosticsPingPlugin', 'OtherType') }
        if ($sample -eq 'unexpected-reference') {
            $sampleProject = $sampleProject.Replace('<ItemGroup>', '<ItemGroup><Reference Include="Microsoft.CSharp" />')
            $sampleSource = $sampleSource.Replace('public sealed class DiagnosticsPingPlugin { }', 'public sealed class DiagnosticsPingPlugin { /// <summary>Inert dynamic metadata dependency.</summary>' + "`r`n" + 'public static object ReadValue(dynamic value) { return value.Value; } }')
        }
        if ($sample -eq 'drawing') {
            $sampleProject = $sampleProject.Replace('<ItemGroup>', '<ItemGroup><Reference Include="System.Drawing" />')
            $sampleSource = $sampleSource.Replace('public sealed class DiagnosticsPingPlugin { }', 'public sealed class DiagnosticsPingPlugin { /// <summary>Inert forbidden GDI metadata.</summary>' + "`r`n" + 'public static System.Drawing.Color ReadColor() { return System.Drawing.Color.Red; } }')
        }
        [IO.File]::WriteAllText($projectPath, ($sampleProject -replace "`r?`n", "`r`n") + "`r`n", $utf8)
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Fixture.cs'), ($sampleSource -replace "`r?`n", "`r`n") + "`r`n", $utf8)
        & dotnet build $projectPath -c Release -p:SignAssembly=$signed -o $sampleOutput --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed; this is not a functional red result.' }
        $assemblyPath = Join-Path $sampleOutput 'Fixture.dll'
        if ($sample -eq 'oversize') {
            $sizedStream = [IO.File]::OpenWrite($assemblyPath)
            try { $sizedStream.SetLength(16 * 1024 * 1024) } finally { $sizedStream.Dispose() }
        }
        if ($sample -eq 'invalid-image') { [IO.File]::WriteAllBytes($assemblyPath, [byte[]]@(1, 2, 3, 4)) }
        $checker = Join-Path $PSScriptRoot '../Test-PluginAssembly.ps1'
        $output = @(& powershell -NoProfile -File $checker -AssemblyPath $assemblyPath 2>&1)
        $code = $LASTEXITCODE
        $jsonLine = $output | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
        if ($null -eq $jsonLine) { throw 'Checker did not return structured evidence.' }
        $result = $jsonLine.ToString() | ConvertFrom-Json
        if ($sample -eq 'signed') {
            $passed = $code -eq 0 -and $result.ok -eq $true -and $result.checks.strongName -eq $true -and
                $result.checks.targetFramework -eq $true -and $result.checks.allowedReferences -eq $true -and
                $result.checks.noDrawing -eq $true -and $result.checks.diagnosticsPlugin -eq $true -and $result.checks.size -eq $true
        } else {
            $passed = $code -eq 1 -and $result.ok -eq $false -and $result.checks.$failedCheck -eq $false
        }
        if ($passed) { Write-Output ('PASS ' + $sample) }
        else { $failures++; Write-Output ('FAIL ' + $sample + ': contract not satisfied') }
    }
    # 多檔結果必須逐檔輸出，且任何一個未簽章都讓整體失敗；不能只驗最後一個 DLL。
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'signed/Fixture.dll') -Destination (Join-Path $fixtureRoot 'a-signed.dll')
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'unsigned/Fixture.dll') -Destination (Join-Path $fixtureRoot 'z-unsigned.dll')
    $output = @(& powershell -NoProfile -File $checker -AssemblyPath (Join-Path $fixtureRoot '*.dll') 2>&1)
    $code = $LASTEXITCODE
    $results = @($output | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | ForEach-Object { $_.ToString() | ConvertFrom-Json })
    $passed = $code -eq 1 -and $results.Count -eq 2 -and @($results | Where-Object { $_.ok }).Count -eq 1
    if ($passed) { Write-Output 'PASS mixed-directory' } else { $failures++; Write-Output 'FAIL mixed-directory: every DLL must be inspected' }
} finally {
    # 先解析絕對目標並核對專屬暫存邊界，再以同一個 PowerShell 直接移除，禁止跨殼層組命令。
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedFixture)).StartsWith('speechmessage-packaging-', [StringComparison]::Ordinal)) {
        throw 'Refused cleanup outside the owned fixture directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Force -Recurse
}
Write-Output ('Plugin packaging samples: total=' + ($samples.Count + 1) + ' failed=' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
