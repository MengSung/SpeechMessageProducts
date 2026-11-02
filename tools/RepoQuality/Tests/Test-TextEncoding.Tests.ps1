<#
.SYNOPSIS
以實際 Git 差異、位元組樣本與子程序驗證編碼守門及無損轉換契約。
.DESCRIPTION
每例擁有獨立暫存 Git 目錄；注入不合法位元組後，斷言結束碼與具體診斷，避免任何失敗都算通過。
轉換案例獨立比對預期 UTF-8 位元組、原始雜湊與 manifest，不能只相信工具宣告成功。
測試只使用合成中文字串，不含使用者、教會、憑證或外部網路資料；清理前驗證目錄邊界。
#>

Describe 'Repository text encoding and lossless conversion contracts' {
    BeforeAll {
        $script:repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
        $script:fixtureBase = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Fixtures/.runtime'))
        $script:engine = Join-Path $PSHOME 'powershell.exe'
        if (-not (Test-Path -LiteralPath $script:engine)) { $script:engine = Join-Path $PSHOME 'pwsh.exe' }
        $script:checker = Join-Path $script:repoRoot 'tools/RepoQuality/Test-TextEncoding.ps1'
        $script:converter = Join-Path $script:repoRoot 'tools/RepoQuality/Convert-TextEncoding.ps1'
        $script:utf8 = New-Object Text.UTF8Encoding($false, $true)
        $script:cp950 = [Text.Encoding]::GetEncoding(950)
        $script:chinese = [string][char]0x4E2D + [char]0x6587
        $script:validSource = 'internal class Fixture { internal string Value = "' + $script:chinese + '"; }' + "`r`n"

        function Invoke-EncodingCheck {
            <# 檢查真實工作樹差異，回傳結束碼與診斷；子程序結束後才讀取結果。 #>
            param([string]$Scope = 'Changed')
            $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script:checker, '-RepositoryPath', $script:caseRepo, '-Scope', $Scope)
            if ($Scope -eq 'Changed') { $arguments += @('-Base', 'HEAD') }
            else { $arguments += @('-ReportPath', (Join-Path $script:caseDirectory 'report.md')) }
            $output = & $script:engine @arguments 2>&1
            return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
        }

        function Invoke-EncodingConversion {
            <# 以公開命令介面轉換單一合成樣本，manifest 位於來源 Git 目錄外，避免干擾掃描。 #>
            param([switch]$Apply, [switch]$DryRun)
            $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script:converter, '-Path', $script:sample, '-ManifestPath', $script:manifest)
            if ($Apply) { $arguments += '-Apply' }
            if ($DryRun) { $arguments += '-DryRun' }
            $output = & $script:engine @arguments 2>&1
            return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
        }

        function Invoke-EncodingArrayConversion {
            <# 以編碼命令保留 Unicode 路徑及陣列參數；每個路徑先做單引號引用，不經字串展開執行。 #>
            param([string[]]$SourcePaths, [string]$AuditPath)
            $quotedPaths = @($SourcePaths | ForEach-Object { "'" + $_.Replace("'", "''") + "'" })
            $command = "& '" + $script:converter.Replace("'", "''") + "' -Path @(" + ($quotedPaths -join ',') + ") -ManifestPath '" + $AuditPath.Replace("'", "''") + "' -Apply"
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
            $output = & $script:engine -NoProfile -EncodedCommand $encoded 2>&1
            return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
        }
    }

    BeforeEach {
        $script:caseDirectory = Join-Path $script:fixtureBase ([guid]::NewGuid().ToString('N'))
        $script:caseRepo = Join-Path $script:caseDirectory 'repo'
        [void][IO.Directory]::CreateDirectory($script:caseRepo)
        $script:sample = Join-Path $script:caseRepo 'sample.cs'
        $script:manifest = Join-Path $script:caseDirectory 'manifest.json'
        [IO.File]::WriteAllText($script:sample, "initial`r`n", $script:utf8)
        & git -c core.longpaths=true init -q $script:caseRepo
        & git -c core.longpaths=true -C $script:caseRepo config core.autocrlf false
        & git -c core.longpaths=true -C $script:caseRepo add sample.cs
        & git -c core.longpaths=true -C $script:caseRepo -c user.name=EncodingTest -c user.email=encoding-test@invalid.example commit -q -m 'fixture baseline'
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated Git fixture.' }
    }

    AfterEach {
        $resolved = [IO.Path]::GetFullPath($script:caseDirectory)
        $prefix = $script:fixtureBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup target.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }

    # 正常樣本與每一個故障各自比對具體診斷，不能把解析錯誤或找不到工具當成成功拒絕。
    It 'Utf8NoBomCrlf_ChangedFile_Passes' {
        [IO.File]::WriteAllText($script:sample, $script:validSource, $script:utf8)
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'checked=1'
    }
    It 'Utf8Bom_ChangedFile_FailsWithBom' {
        [IO.File]::WriteAllBytes($script:sample, ([byte[]]@(0xEF,0xBB,0xBF) + $script:utf8.GetBytes($script:validSource)))
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'Bom'
    }
    It 'LfOnly_ChangedFile_FailsWithLineEndings' {
        [IO.File]::WriteAllText($script:sample, $script:validSource.Replace("`r`n", "`n"), $script:utf8)
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'NonCrlfLineEndings'
    }
    It 'Cp950_ChangedFile_FailsWithInvalidUtf8' {
        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'InvalidUtf8'
    }
    It 'MissingFinalNewline_ChangedFile_Fails' {
        [IO.File]::WriteAllText($script:sample, $script:validSource.TrimEnd(), $script:utf8)
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'MissingFinalCrlf'
    }
    It 'ReplacementCharacter_ChangedFile_Fails' {
        [IO.File]::WriteAllText($script:sample, ([string][char]0xFFFD + "`r`n"), $script:utf8)
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'ReplacementCharacter'
    }
    It 'PowerShell_ChineseOnlyInComment_Passes' {
        $psPath = Join-Path $script:caseRepo 'sample.ps1'
        [IO.File]::WriteAllText($psPath, ('# ' + $script:chinese + "`r`nWrite-Output 'ok'`r`n"), $script:utf8)
        & git -c core.longpaths=true -C $script:caseRepo add sample.ps1
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 0
    }
    It 'PowerShell_ChineseInString_Fails' {
        $psPath = Join-Path $script:caseRepo 'sample.ps1'
        [IO.File]::WriteAllText($psPath, ("Write-Output '" + $script:chinese + "'`r`n"), $script:utf8)
        & git -c core.longpaths=true -C $script:caseRepo add sample.ps1
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'NonAsciiExecutablePowerShell'
    }
    It 'AllScope_InvalidFile_ReportsWithoutFailingBuild' {
        [IO.File]::WriteAllText($script:sample, "bad`n", $script:utf8)
        $result = Invoke-EncodingCheck -Scope All
        $result.ExitCode | Should Be 0
        [IO.File]::ReadAllText((Join-Path $script:caseDirectory 'report.md')) | Should Match 'NonCrlfLineEndings'
    }
    It 'AllScope_DoesNotReadOutsideRepository' {
        $outside = Join-Path $script:fixtureBase ('outside-' + [guid]::NewGuid().ToString('N') + '.cs')
        try {
            [IO.File]::WriteAllText($outside, "outside`n", $script:utf8)
            [IO.File]::WriteAllText($script:sample, "inside`n", $script:utf8)
            $result = Invoke-EncodingCheck -Scope All
            $result.ExitCode | Should Be 0
            $report = [IO.File]::ReadAllText((Join-Path $script:caseDirectory 'report.md'))
            $report | Should Match 'sample.cs'
            $report | Should Not Match ([IO.Path]::GetFileName($outside))
        }
        finally {
            if ([IO.File]::Exists($outside)) { [IO.File]::Delete($outside) }
        }
    }
    It 'AllScope_TrackedAndExcludedPaths_OnlyScansAllowedTrackedSource' {
        [IO.File]::WriteAllText((Join-Path $script:caseRepo 'untracked.cs'), "untracked`n", $script:utf8)
        $referenceName = '.' + [string][char]0x5176 + [char]0x4ED6 + [char]0x89E3 + [char]0x6C7A + [char]0x65B9 + [char]0x6848
        foreach ($directory in @('bin','obj','.worktrees',$referenceName)) {
            $folder = Join-Path $script:caseRepo $directory
            [void][IO.Directory]::CreateDirectory($folder)
            [IO.File]::WriteAllText((Join-Path $folder 'excluded.cs'), "excluded`n", $script:utf8)
            & git -c core.longpaths=true -C $script:caseRepo add -f -- ($directory + '/excluded.cs')
        }
        $result = Invoke-EncodingCheck -Scope All
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'checked=1 '
        $report = [IO.File]::ReadAllText((Join-Path $script:caseDirectory 'report.md'))
        $report | Should Not Match 'excluded.cs|untracked.cs'
    }
    It 'AllScope_TrackedPathCrossesJunction_RejectsBeforeReadingExternalContent' {
        $linked = Join-Path $script:caseRepo 'linked'
        $external = Join-Path $script:caseDirectory 'outside'
        [void][IO.Directory]::CreateDirectory($linked)
        [void][IO.Directory]::CreateDirectory($external)
        [IO.File]::WriteAllText((Join-Path $linked 'foreign.cs'), "tracked`r`n", $script:utf8)
        [IO.File]::WriteAllText((Join-Path $external 'foreign.cs'), "outside`n", $script:utf8)
        & git -c core.longpaths=true -C $script:caseRepo add linked/foreign.cs
        [IO.File]::Delete((Join-Path $linked 'foreign.cs'))
        [IO.Directory]::Delete($linked)
        try {
            [void](New-Item -ItemType Junction -Path $linked -Target $external)
            $result = Invoke-EncodingCheck -Scope All
            $result.ExitCode | Should Be 0
            $report = [IO.File]::ReadAllText((Join-Path $script:caseDirectory 'report.md'))
            $report | Should Match 'UnsafePath'
            $report | Should Not Match 'NonCrlfLineEndings'
        }
        finally {
            # 只移除已確認屬於本案例的 junction 本身，不遞迴處理它指向的目錄。
            $expected = [IO.Path]::GetFullPath((Join-Path $script:caseRepo 'linked'))
            if ([IO.Path]::GetFullPath($linked) -ne $expected) { throw 'Unsafe junction cleanup target.' }
            if ([IO.Directory]::Exists($linked)) { [IO.Directory]::Delete($linked) }
        }
    }
    It 'ChangedScope_UnchangedBadFile_IsNotScanned' {
        [IO.File]::WriteAllText($script:sample, "bad`n", $script:utf8)
        & git -c core.longpaths=true -C $script:caseRepo add sample.cs
        & git -c core.longpaths=true -C $script:caseRepo -c user.name=EncodingTest -c user.email=encoding-test@invalid.example commit -q -m 'existing invalid fixture'
        $result = Invoke-EncodingCheck
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'checked=0'
    }
    # 轉換必須比對真正輸出位元組；無損與乾跑的斷言都不能只依賴 manifest 的宣告。
    It 'ConvertCp950_Apply_PreservesChineseAndWritesUtf8' {
        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash.ToLowerInvariant()
        $result = Invoke-EncodingConversion -Apply
        $result.ExitCode | Should Be 0
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($script:sample)) | Should Be ([Convert]::ToBase64String($script:utf8.GetBytes($script:validSource)))
        $manifest = Get-Content -LiteralPath $script:manifest -Raw -Encoding UTF8 | ConvertFrom-Json
        $manifest.files[0].originalEncoding | Should Be 'cp950'
        $manifest.files[0].beforeSha256 | Should Be $before
        $manifest.files[0].afterSha256 | Should Be ((Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash.ToLowerInvariant())
        $manifest.files[0].chineseStringLiteralsUnchanged | Should Be $true
    }
    It 'Convert_DefaultDryRun_DoesNotWriteSource' {
        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
        $result = Invoke-EncodingConversion
        $result.ExitCode | Should Be 0
        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
        $manifest = Get-Content -LiteralPath $script:manifest -Raw -Encoding UTF8 | ConvertFrom-Json
        $manifest.dryRun | Should Be $true
        $manifest.files[0].status | Should Be 'dry-run'
    }
    It 'Convert_ExplicitDryRun_DoesNotWriteSource' {
        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
        $result = Invoke-EncodingConversion -DryRun
        $result.ExitCode | Should Be 0
        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
    }
    It 'Convert_InvalidEncoding_LeavesOriginalUntouched' {
        [IO.File]::WriteAllBytes($script:sample, [byte[]]@(0x81))
        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
        $result = Invoke-EncodingConversion -Apply
        $result.ExitCode | Should Be 1
        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
        $manifest = Get-Content -LiteralPath $script:manifest -Raw -Encoding UTF8 | ConvertFrom-Json
        $manifest.files[0].status | Should Be 'failed'
        $manifest.files[0].error | Should Match 'InvalidEncoding'
    }
    It 'Convert_MultilineChineseLiteralWouldChange_RefusesWrite' {
        $source = 'internal class Fixture { internal string Value = @"' + $script:chinese + "`n" + $script:chinese + '"; }' + "`n"
        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($source))
        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
        $result = Invoke-EncodingConversion -Apply
        $result.ExitCode | Should Be 1
        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
        $manifest = Get-Content -LiteralPath $script:manifest -Raw -Encoding UTF8 | ConvertFrom-Json
        $manifest.files[0].chineseStringLiteralsUnchanged | Should Be $false
        $manifest.files[0].error | Should Match 'StringLiteralChanged'
    }
    It 'Convert_Utf16Bom_ProducesExpectedUtf8Bytes' {
        $encoding = New-Object Text.UnicodeEncoding($false, $true, $true)
        [IO.File]::WriteAllBytes($script:sample, ($encoding.GetPreamble() + $encoding.GetBytes($script:validSource)))
        $result = Invoke-EncodingConversion -Apply
        $result.ExitCode | Should Be 0
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($script:sample)) | Should Be ([Convert]::ToBase64String($script:utf8.GetBytes($script:validSource)))
    }
    It 'Convert_ManifestMatchesLaterInput_RejectsBeforeAnyWrite' {
        $second = Join-Path $script:caseRepo 'second.cs'
        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
        [IO.File]::WriteAllBytes($second, $script:cp950.GetBytes($script:validSource))
        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
        $result = Invoke-EncodingArrayConversion -SourcePaths @($script:sample, $second) -AuditPath $second
        $result.ExitCode | Should Be 1
        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
    }
    It 'Convert_DuplicateStablePath_RejectsBeforeAnyWrite' {
        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
        $result = Invoke-EncodingArrayConversion -SourcePaths @($script:sample, $script:sample) -AuditPath $script:manifest
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'DuplicateSourcePath'
        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
    }
}
