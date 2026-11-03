<#
.SYNOPSIS
以獨占的假 Git 工作樹驗證套件掃描邊界，不讀取外部專案或執行還原。
.DESCRIPTION
先追蹤檔案，再以真正 NTFS junction 取代父目錄；工具必須在 XML 解碼前回報 UnsafePath。
所有外部內容皆為合成文字，清理先解除本案 junction，再刪除經絕對前綴驗證的自有根。
不輸出來源內容；每個案例結束後外部假檔案雜湊必須不變，且可立即獨佔開啟。
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$tool = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Test-PackageVersions.ps1'))
$engine = Join-Path $PSHOME 'powershell.exe'
if (-not [IO.File]::Exists($engine)) { $engine = Join-Path $PSHOME 'pwsh.exe' }
$utf8 = New-Object Text.UTF8Encoding($false)
$fixtureBase = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-package-path-' + [Guid]::NewGuid().ToString('N'))))
$links = New-Object 'Collections.Generic.List[string]'

function Set-FixtureFile {
    <# 只寫本案工作樹固定相對路徑；每次寫入即關閉檔案，不保留來源 writer。 #>
    param([string]$Relative, [string]$Text)
    $path = Join-Path $script:caseRepo $Relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $Text, $script:utf8)
}

function Set-TrackedJunction {
    <# 先加入索引使 Git 必然回報該路徑，再替換空父目錄；不倚賴 Git 是否走訪未追蹤 junction。 #>
    param([string]$RelativeDirectory)
    Set-FixtureFile ($RelativeDirectory + '/Foreign.csproj') '<Project />'
    & git -C $script:caseRepo add -- ($RelativeDirectory + '/Foreign.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Fixture git add failed.' }
    $link = [IO.Path]::GetFullPath((Join-Path $script:caseRepo $RelativeDirectory))
    if (-not $link.StartsWith($script:caseRepo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture link path.' }
    Remove-Item -LiteralPath (Join-Path $link 'Foreign.csproj')
    [IO.Directory]::Delete($link)
    [void](New-Item -ItemType Junction -Path $link -Target $script:external)
    $script:links.Add($link)
}

function Invoke-PackageGuard {
    <# 子程序擁有工具的所有 Git／XML 資源，等候完成後只回傳代碼與固定診斷。 #>
    $output = & $script:engine -NoProfile -File $script:tool -RepositoryPath $script:caseRepo 2>&1
    return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
}

function Assert-Result {
    <# 斷言指定診斷，避免任意環境失敗冒充安全拒絕；不輸出假來源或工具原始訊息。 #>
    param($Result, [int]$Exit, [string]$Pattern)
    if ($Result.ExitCode -ne $Exit -or $Result.Output -notmatch $Pattern) { throw 'Package path contract failed.' }
    if ($Result.Output.Contains('SYNTHETIC_EXTERNAL_XML')) { throw 'Source content leaked.' }
}

$cases = [ordered]@{
    SafeProjects_Pass = { Assert-Result (Invoke-PackageGuard) 0 'failures=0' }
    TrackedProjectJunction_RejectedBeforeXml = {
        Set-TrackedJunction 'Linked'
        Assert-Result (Invoke-PackageGuard) 1 'UnsafePath'
    }
    ExcludedWorktreeJunction_DoesNotReadOutside = {
        Set-TrackedJunction '.worktrees'
        Assert-Result (Invoke-PackageGuard) 0 'failures=0'
    }
    NestedBuildArtifacts_AreExcluded = {
        Set-FixtureFile 'A/bin/Bad.csproj' '<Project><ItemGroup><PackageReference Include="Fake" Version="1" /></ItemGroup></Project>'
        Assert-Result (Invoke-PackageGuard) 0 'failures=0'
    }
    DocsLikePrefix_IsStillChecked = {
        Set-FixtureFile 'docs-like/Bad.csproj' '<Project><ItemGroup><PackageReference Include="Fake" Version="1" /></ItemGroup></Project>'
        Assert-Result (Invoke-PackageGuard) 1 'InlineVersion'
    }
}
$failed = 0
try {
    foreach ($case in $cases.GetEnumerator()) {
        $caseDirectory = Join-Path $fixtureBase ([Guid]::NewGuid().ToString('N'))
        $script:caseRepo = Join-Path $caseDirectory 'repo'
        $script:external = Join-Path $caseDirectory 'external'
        [void][IO.Directory]::CreateDirectory($script:caseRepo)
        [void][IO.Directory]::CreateDirectory($script:external)
        $outsideFile = Join-Path $script:external 'Foreign.csproj'
        [IO.File]::WriteAllText($outsideFile, 'SYNTHETIC_EXTERNAL_XML', $utf8)
        $before = (Get-FileHash -LiteralPath $outsideFile -Algorithm SHA256).Hash
        & git -C $script:caseRepo init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Fixture git init failed.' }
        Set-FixtureFile 'Directory.Packages.props' '<Project><ItemGroup><PackageVersion Include="System.Security.Cryptography.Xml" Version="10.0.14" /><PackageVersion Include="System.Security.Cryptography.Pkcs" Version="10.0.14" /><PackageVersion Include="System.Text.Json" Version="6.0.11" /></ItemGroup></Project>'
        Set-FixtureFile 'PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj' '<Project><ItemGroup><PackageReference Include="System.Security.Cryptography.Xml" /><PackageReference Include="System.Security.Cryptography.Pkcs" /></ItemGroup></Project>'
        foreach ($project in @('SpeechMessage.Dynamics.Plugins','SpeechMessage.Dynamics.Plugins.Tests')) {
            Set-FixtureFile ($project + '/' + $project + '.csproj') '<Project><ItemGroup><PackageReference Include="System.Text.Json" PrivateAssets="all" /></ItemGroup></Project>'
        }
        Set-FixtureFile 'A/A.csproj' '<Project />'
        try {
            & $case.Value
            if ((Get-FileHash -LiteralPath $outsideFile -Algorithm SHA256).Hash -ne $before) { throw 'External fixture changed.' }
            $handle = [IO.File]::Open($outsideFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $handle.Dispose()
            Write-Output ('PASS ' + $case.Key)
        } catch {
            $failed++
            Write-Output ('FAIL ' + $case.Key)
        } finally {
            foreach ($link in $links) {
                if (-not [IO.Path]::GetFullPath($link).StartsWith($caseDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe junction cleanup path.' }
                if ([IO.Directory]::Exists($link)) { [IO.Directory]::Delete($link) }
            }
            $links.Clear()
        }
    }
} finally {
    $expected = [IO.Path]::GetFullPath($fixtureBase)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $expected.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($expected) -notlike 'speechmessage-package-path-*') { throw 'Unsafe fixture cleanup path.' }
    if ([IO.Directory]::Exists($expected)) { Remove-Item -LiteralPath $expected -Recurse -Force }
}
Write-Output ('Package path tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -gt 0) { exit 1 }
