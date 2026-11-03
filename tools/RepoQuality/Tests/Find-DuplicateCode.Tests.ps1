<#
.SYNOPSIS
以合成方案驗證跨專案副本、型別重名、基準棘輪及掃描邊界。
.DESCRIPTION
每案只擁有自己的暫存方案，不執行產品來源或連外；清理前確認絕對路徑。
拒絕案例必須比對具體診斷，不接受任意解析失敗冒充安全拒絕。
#>
Describe 'Solution duplicate code contracts' {
    $script:tool = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Find-DuplicateCode.ps1'))
    $script:fixtureBase = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Fixtures/.runtime'))
    $script:utf8 = New-Object Text.UTF8Encoding($false, $true)
    $script:engine = Join-Path $PSHOME 'powershell.exe'
    if (-not [IO.File]::Exists($script:engine)) { $script:engine = Join-Path $PSHOME 'pwsh.exe' }
    function Set-Source {
        <# 寫入當案固定合成來源；不借用被測工具計算期望內容。 #>
        param([string]$Relative, [string]$Content)
        $path = Join-Path $script:caseRepo $Relative
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        [IO.File]::WriteAllText($path, $Content + "`r`n", $script:utf8)
    }
    function Set-Solution {
        <# 只列指定的假專案，未列入的資料夾不得被走訪。 #>
        param([string[]]$Projects = @('A','B'))
        $lines = @($Projects | ForEach-Object { 'Project("{FAKE}") = "' + $_ + '", "' + $_ + '/' + $_ + '.csproj", "{' + $_ + '}"' })
        [IO.File]::WriteAllText((Join-Path $script:caseRepo 'SpeechMessageProducts.sln'), ($lines -join "`r`n") + "`r`n", $script:utf8)
        foreach ($project in $Projects) { Set-Source ($project + '/' + $project + '.csproj') '<Project Sdk="Microsoft.NET.Sdk" />' }
    }
    function Invoke-DuplicateScan {
        <# 使用一次性子程序；等候完成後才查結果，不保留程序或串流。 #>
        param([switch]$WriteBaseline)
        $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$script:tool,'-RepositoryPath',$script:caseRepo,'-Baseline',$script:baseline)
        if ($WriteBaseline) { $arguments += '-WriteBaseline' }
        $output = & $script:engine @arguments 2>&1
        return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
    }
    BeforeEach {
        $script:caseDirectory = Join-Path $script:fixtureBase ([guid]::NewGuid().ToString('N'))
        $script:caseRepo = Join-Path $script:caseDirectory 'repo'
        $script:baseline = Join-Path $script:caseRepo 'baseline.json'
        [void][IO.Directory]::CreateDirectory($script:caseRepo)
        Set-Solution
        Set-Source 'A/A.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
        Set-Source 'B/B.cs' 'namespace N { internal class Beta { internal int Value = 2; } }'
    }
    AfterEach {
        $actual = [IO.Path]::GetFullPath($script:caseDirectory)
        $prefix = $script:fixtureBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $actual.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
        Remove-Item -LiteralPath $actual -Recurse -Force
    }
    It 'SameContent_DifferentProjects_IsRecorded' {
        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 0
        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        @($baseline.entries | Where-Object { $_.kind -eq 'content' }).Count | Should Be 1
        [IO.File]::ReadAllText($script:baseline) | Should Not Match 'internal int Value'
    }
    It 'CommentsAndWhitespace_DifferentFormatting_IsRecorded' {
        Set-Source 'A/A.cs' ("// explanation`r`nnamespace N {`r`n internal class Alpha { }`r`n}")
        Set-Source 'B/B.cs' ("/// docs`r`nnamespace N {`r`n    internal class Alpha { }   `r`n}")
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 0
        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        @($baseline.entries | Where-Object { $_.kind -eq 'content' }).Count | Should Be 1
    }
    It 'ExistingBaseline_Duplicate_DoesNotFail' {
        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
        (Invoke-DuplicateScan).ExitCode | Should Be 0
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'new=0'
    }
    It 'NewDuplicate_OutsideBaseline_Fails' {
        (Invoke-DuplicateScan).ExitCode | Should Be 0
        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'NewDuplicate'
    }
    It 'ExistingGroup_AdditionalProject_Fails' {
        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
        (Invoke-DuplicateScan).ExitCode | Should Be 0
        Set-Solution @('A','B','C')
        Set-Source 'C/C.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'NewDuplicate'
    }
    It 'ResolvedDuplicate_RemindsWithoutFailure' {
        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
        (Invoke-DuplicateScan).ExitCode | Should Be 0
        Set-Source 'B/B.cs' 'namespace N { internal class Beta { } }'
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'BaselineStale'
    }
    It 'SameNamespaceType_DifferentContent_IsRecorded' {
        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 9; } }'
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 0
        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        @($baseline.entries | Where-Object { $_.kind -eq 'type' -and $_.key -eq 'N.Alpha' }).Count | Should Be 1
        @($baseline.entries | Where-Object { $_.kind -eq 'content' }).Count | Should Be 0
    }
    It 'SameProjectOnly_Duplicates_AreNotRecorded' {
        Set-Source 'A/Other.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 0
        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        @($baseline.entries).Count | Should Be 0
    }
    # 不同外層型別的同名巢狀型別有不同身分，不能以 N.Inner 誤併為跨專案重複。
    It 'NestedTypes_DifferentOwners_AreSeparate' {
        Set-Source 'A/A.cs' 'namespace N { class OuterA { class Inner { int A = 1; } } }'
        Set-Source 'B/B.cs' 'namespace N { class OuterB { class Inner { int B = 2; } } }'
        (Invoke-DuplicateScan).ExitCode | Should Be 0
        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        @($baseline.entries).Count | Should Be 0
    }
    # 真正相同外層與巢狀身分仍須記錄，不能因新增 owner 追蹤而漏掉重複。
    It 'NestedTypes_SameOwner_DifferentContent_AreRecorded' {
        Set-Source 'A/A.cs' 'namespace N { class Outer { class Inner { int A = 1; } } }'
        Set-Source 'B/B.cs' 'namespace N { class Outer { class Inner { int B = 2; } } }'
        (Invoke-DuplicateScan).ExitCode | Should Be 0
        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        @($baseline.entries | Where-Object { $_.kind -eq 'type' -and $_.key -eq 'N.Outer.Inner' }).Count | Should Be 1
        @($baseline.entries | Where-Object { $_.key -eq 'N.Inner' }).Count | Should Be 0
    }
    # 關閉巢狀作用域後必須還原 owner，不讓前一個兄弟型別或方法大括號汙染後續鍵。
    It 'NestedTypes_SiblingScopes_RestoreOwner' {
        Set-Source 'A/A.cs' 'namespace N { class OuterA { class First { class Inner { int A = 1; } } class Second { class Inner { int A = 2; } } } }'
        Set-Source 'B/B.cs' 'namespace N { class OuterB { class Inner { int B = 3; } } }'
        (Invoke-DuplicateScan).ExitCode | Should Be 0
        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        @($baseline.entries).Count | Should Be 0
    }
    # 無本體 record 的分號不能把下一個型別錯當成其巢狀成員。
    It 'NestedTypes_RecordWithoutBody_DoesNotOwnFollowingType' {
        Set-Source 'A/A.cs' 'namespace N; record Marker; class OuterA { class Inner { int A = 1; } }'
        Set-Source 'B/B.cs' 'namespace N; class OuterB { class Inner { int B = 2; } }'
        (Invoke-DuplicateScan).ExitCode | Should Be 0
        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        @($baseline.entries).Count | Should Be 0
    }
    It 'Scope_OutsideProjectsGeneratedAndExcludedPaths_AreNotRead' {
        foreach ($file in @('Outside/old.cs','A/bin/old.cs','A/obj/old.cs','A/.worktrees/old.cs','A/docs/old.cs','A/.ccg/tasks/old.cs','A/old.g.cs','A/old.Designer.cs')) {
            Set-Source $file 'namespace N { internal class Alpha { internal int Value = 1; } }'
        }
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'files=2 '
    }
    It 'Scope_ProjectOutsideRepository_IsRejected' {
        [IO.File]::WriteAllText((Join-Path $script:caseRepo 'SpeechMessageProducts.sln'), 'Project("{FAKE}") = "Other", "../other.csproj", "{OTHER}"', $script:utf8)
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'UnsafePath'
    }
    It 'Scope_Junction_IsNotTraversed' {
        $external = Join-Path $script:caseDirectory 'external'
        [void][IO.Directory]::CreateDirectory($external)
        [IO.File]::WriteAllText((Join-Path $external 'foreign.cs'), 'namespace N { internal class Alpha { internal int Value = 1; } }', $script:utf8)
        $link = Join-Path $script:caseRepo 'A/linked'
        try {
            [void](New-Item -ItemType Junction -Path $link -Target $external)
            $result = Invoke-DuplicateScan
            $result.ExitCode | Should Be 0
            $result.Output | Should Match 'files=2 '
        }
        finally {
            if ([IO.Path]::GetFullPath($link) -ne [IO.Path]::GetFullPath((Join-Path $script:caseRepo 'A/linked'))) { throw 'Unsafe junction cleanup path.' }
            if ([IO.Directory]::Exists($link)) { [IO.Directory]::Delete($link) }
        }
    }
    It 'Scope_EmptySolution_FailsInsteadOfFalseGreen' {
        [IO.File]::WriteAllText((Join-Path $script:caseRepo 'SpeechMessageProducts.sln'), '', $script:utf8)
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'NoProjects'
    }
    # 基準本身也是具穩定識別的集合；重複群組或位置不可被字典覆寫後默默接受。
    It 'Baseline_DuplicateGroupIdentity_IsRejected' {
        $json = '{"schemaVersion":1,"entries":[{"kind":"type","key":"N.Alpha","locations":["A|A.cs"]},{"kind":"type","key":"N.Alpha","locations":["B|B.cs"]}]}'
        [IO.File]::WriteAllText($script:baseline, $json, $script:utf8)
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'DuplicateBaselineIdentity'
    }
    It 'Baseline_DuplicateLocation_IsRejected' {
        $json = '{"schemaVersion":1,"entries":[{"kind":"type","key":"N.Alpha","locations":["A|A.cs","A|A.cs"]}]}'
        [IO.File]::WriteAllText($script:baseline, $json, $script:utf8)
        $result = Invoke-DuplicateScan
        $result.ExitCode | Should Be 1
        $result.Output | Should Match 'DuplicateBaselineLocation'
    }
}
