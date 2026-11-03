<#
.SYNOPSIS
以真實建置驗證禁用時間 API 與嚴格模式的載入時點。
.DESCRIPTION
預設探測分支故意違規，必須只出現 RS0030；文件與其他故障分支依各自的實際診斷判斷。
不以命令列強制啟用嚴格模式；同時評估新舊專案屬性，防止 props 載入順序造成假綠燈。
子程序與重新導向串流由本腳本每次呼叫擁有；停用建置伺服器重用，逾時終止程序樹。
輸出只存於呼叫者指定的證據目錄，預設放在被忽略的 artifacts，不保存使用者或教會狀態。
.PARAMETER RepositoryPath
本工作樹根目錄，預設為腳本上兩層。
.PARAMETER EvidenceDirectory
保存建置原始輸出及精簡結果的目錄；每次呼叫由呼叫者決定生命週期。
#>
[CmdletBinding()]
param([string]$RepositoryPath, [string]$EvidenceDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-AnalyzerDotNet {
    <#
    .SYNOPSIS
    執行單次有界的 dotnet 檢查並回傳原始結果；非零結束碼交由測試判斷。
    .DESCRIPTION
    同時讀取兩個輸出串流避免互相阻塞；最長三分鐘，逾時先終止子程序樹再釋放擁有者。
    #>
    param([string]$Arguments, [string]$LogName)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = 'dotnet'
    $start.Arguments = $Arguments
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $utf8
    $start.StandardErrorEncoding = $utf8
    $start.EnvironmentVariables['DOTNET_CLI_UI_LANGUAGE'] = 'en-US'
    $start.EnvironmentVariables['VSLANG'] = '1033'
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(180000)) {
            & taskkill.exe /PID $process.Id /T /F *> $null
            if (-not $process.WaitForExit(10000)) { $process.Kill() }
            throw 'Analyzer dotnet probe timed out.'
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [System.IO.File]::WriteAllText((Join-Path $EvidenceDirectory $LogName), $output, $utf8)
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output }
    }
    finally { $process.Dispose() }
}

try {
    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
    $root = (Resolve-Path -LiteralPath $RepositoryPath).Path
    if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $root 'artifacts/quality/analyzer-probe' }
    [void][System.IO.Directory]::CreateDirectory($EvidenceDirectory)
    $EvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $failures = New-Object 'System.Collections.Generic.List[string]'
    $probe = 'tools/RepoQuality/AnalyzerProbe/AnalyzerProbe.csproj'
    $build = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild') -LogName 'probe-build.log'
    $errorCodes = @([regex]::Matches($build.Output, '\berror ([A-Z]+[0-9]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if ($build.ExitCode -eq 0 -or $errorCodes.Count -ne 1 -or $errorCodes[0] -ne 'RS0030') {
        $failures.Add("BannedTimeApi_MustFailWithRS0030: exit=$($build.ExitCode) errors=$($errorCodes -join ',')")
    }
    $properties = 'SpeechMessageStrictCode,EnableNETAnalyzers,AnalysisMode,TreatWarningsAsErrors,Nullable,LangVersion,GenerateDocumentationFile,DocumentationFile,TargetDir,WarningsAsErrors'
    $strict = Invoke-AnalyzerDotNet -Arguments ('msbuild "' + $probe + '" -nologo -p:TargetFramework=net10.0 -p:Configuration=Release -getProperty:' + $properties) -LogName 'strict-properties.log'
    if ($strict.ExitCode -ne 0) { throw 'Strict project property evaluation failed.' }
    $settings = ($strict.Output | ConvertFrom-Json).Properties
    if ($settings.SpeechMessageStrictCode -ne 'true' -or $settings.EnableNETAnalyzers -ne 'true' -or
        $settings.AnalysisMode -ne 'Recommended' -or $settings.TreatWarningsAsErrors -ne 'true' -or
        $settings.Nullable -ne 'enable' -or $settings.LangVersion -ne 'latest' -or
        $settings.GenerateDocumentationFile -ne 'true' -or [string]::IsNullOrWhiteSpace($settings.DocumentationFile) -or
        $settings.WarningsAsErrors -notmatch 'CA1307' -or
        $settings.WarningsAsErrors -notmatch 'CA1309' -or $settings.WarningsAsErrors -notmatch 'CA1310') {
        $failures.Add('StrictOptIn_ProjectBody_EnablesAllRequiredSettings: effective properties do not match.')
    }
    $legacy = Invoke-AnalyzerDotNet -Arguments ('msbuild ToolUtility/ToolUtility.csproj -nologo -getProperty:' + $properties) -LogName 'legacy-properties.log'
    if ($legacy.ExitCode -ne 0) { throw 'Legacy project property evaluation failed.' }
    $old = ($legacy.Output | ConvertFrom-Json).Properties
    if ($old.EnableNETAnalyzers -ne 'false' -or $old.AnalysisMode -ne 'None' -or
        $old.TreatWarningsAsErrors -ne 'false' -or $old.Nullable -ne 'disable') {
        $failures.Add('LegacyProject_NoOptIn_PreservesExistingSettings: effective properties changed.')
    }
    $checks = 3
    foreach ($symbol in @('PROBE_TODAY', 'PROBE_LOCAL_TIME', 'PROBE_TIME_ZONE', 'PROBE_CULTURE', 'PROBE_UI_CULTURE')) {
        $result = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild -p:DefineConstants=' + $symbol) -LogName ($symbol + '.log')
        $checks++
        if ($result.ExitCode -eq 0 -or $result.Output -notmatch '\berror RS0030:') {
            $failures.Add("BannedApi_${symbol}_FailsWithRS0030: exit=$($result.ExitCode)")
        }
    }
    foreach ($case in @(@{ Symbol = 'PROBE_COMPARISON'; Codes = @('CA1309') }, @{ Symbol = 'PROBE_SEARCH'; Codes = @('CA1310') }, @{ Symbol = 'PROBE_ORDINAL_OVERLOAD'; Codes = @('CA1307') })) {
        $result = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild -p:DefineConstants=' + $case.Symbol) -LogName ($case.Symbol + '.log')
        $checks++
        foreach ($code in $case.Codes) {
            if ($result.ExitCode -eq 0 -or $result.Output -notmatch ("\berror " + $code + ':')) {
                $failures.Add("StringComparison_$($case.Symbol)_FailsWith${code}: exit=$($result.ExitCode)")
            }
        }
    }
    $compatibility = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net48 --nologo --disable-build-servers -t:Rebuild') -LogName 'net48.log'
    $checks++
    $compatibilityCodes = @([regex]::Matches($compatibility.Output, '\berror ([A-Z]+[0-9]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if ($compatibility.ExitCode -eq 0 -or $compatibilityCodes.Count -ne 1 -or $compatibilityCodes[0] -ne 'RS0030') {
        $failures.Add("Net48_PolyfillsCompile_OnlyBannedApiFails: exit=$($compatibility.ExitCode) errors=$($compatibilityCodes -join ',')")
    }
    # 用編譯器真正的缺漏診斷驗證文件產生，不能僅相信 GenerateDocumentationFile 開關值。
    $missingXml = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild -p:DefineConstants=PROBE_MISSING_XML') -LogName 'missing-xml.log'
    $checks++
    if ($missingXml.ExitCode -eq 0 -or $missingXml.Output -notmatch '\berror CS1591:') {
        $failures.Add("PublicMember_NoXmlComment_FailsWithCS1591: exit=$($missingXml.ExitCode)")
    }
    # 先移除舊產物，再用完全有文件且無禁用 API 的分支建置，證明本次真的複製 XML 到輸出目錄。
    $xmlOutput = Join-Path $settings.TargetDir 'AnalyzerProbe.xml'
    [System.IO.File]::Delete($xmlOutput)
    $documented = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild -p:DefineConstants=PROBE_DOCUMENTED') -LogName 'documented-output.log'
    $checks++
    if ($documented.ExitCode -ne 0 -or -not [System.IO.File]::Exists($xmlOutput)) {
        $failures.Add("DocumentedBuild_OutputContainsXml: exit=$($documented.ExitCode) exists=$([System.IO.File]::Exists($xmlOutput))")
    }
    else {
        $xml = New-Object System.Xml.XmlDocument
        $xml.XmlResolver = $null
        $xml.Load($xmlOutput)
        if (-not $xml.SelectSingleNode("/doc/members/member[@name='T:SpeechMessage.RepoQuality.AnalyzerProbe.ClockProbe']")) {
            $failures.Add('DocumentedBuild_OutputContainsXml: output does not document the probe type.')
        }
    }
    # 每次呼叫擁有獨立 Plugin 名稱的編譯樣本，測試命名空間禁令後才刪除；不與並行呼叫共用可變目錄。
    $fixtureBase = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts/quality/analyzer-probe-fixtures'))
    $fixtureDirectory = Join-Path $fixtureBase ([guid]::NewGuid().ToString('N'))
    [void][System.IO.Directory]::CreateDirectory($fixtureDirectory)
    try {
        $sourcePath = [System.Security.SecurityElement]::Escape((Join-Path $root 'tools/RepoQuality/AnalyzerProbe/ClockProbe.cs'))
        $fixtureProject = Join-Path $fixtureDirectory 'SpeechMessage.Dynamics.Plugins.csproj'
        $fixtureXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><SpeechMessageStrictCode>true</SpeechMessageStrictCode><GenerateDocumentationFile>true</GenerateDocumentationFile><DefineConstants>PROBE_DRAWING</DefineConstants></PropertyGroup><ItemGroup><Compile Include="' + $sourcePath + '" /></ItemGroup></Project>'
        [System.IO.File]::WriteAllText($fixtureProject, $fixtureXml + "`r`n", $utf8)
        $drawing = Invoke-AnalyzerDotNet -Arguments ('build "' + $fixtureProject + '" -c Release --nologo --disable-build-servers') -LogName 'plugin-drawing.log'
        $checks++
        if ($drawing.ExitCode -eq 0 -or $drawing.Output -notmatch '\berror RS0030:.*System.Drawing') {
            $failures.Add("Plugin_SystemDrawing_FailsWithRS0030: exit=$($drawing.ExitCode)")
        }
        $missingProject = Join-Path $fixtureDirectory 'StrictWithoutDocumentation.csproj'
        $missingProjectXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><SpeechMessageStrictCode>true</SpeechMessageStrictCode><DefineConstants>PROBE_DOCUMENTED</DefineConstants></PropertyGroup><ItemGroup><Compile Include="' + $sourcePath + '" /></ItemGroup></Project>'
        [System.IO.File]::WriteAllText($missingProject, $missingProjectXml + "`r`n", $utf8)
        $missingSetting = Invoke-AnalyzerDotNet -Arguments ('build "' + $missingProject + '" -c Release --nologo --disable-build-servers -t:Rebuild') -LogName 'missing-documentation-setting.log'
        $checks++
        if ($missingSetting.ExitCode -eq 0 -or $missingSetting.Output -notmatch '\berror SMQ0001:') {
            $failures.Add("StrictProject_NoDocumentationSetting_FailsBeforeCompile: exit=$($missingSetting.ExitCode)")
        }
        # 三項使用者指定案例加上「只有名稱後綴」的反例，證明 IsTestProject 也不可省略。
        # 每案獨佔來源輸出目錄，避免相同 obj 檔讓不同條件互相污染；命令結束後統一清理。
        $namingSource = [System.Security.SecurityElement]::Escape((Join-Path $root 'tools/RepoQuality/AnalyzerProbe/NamingProbe.cs'))
        $namingCases = @(
            @{ Name = 'NamingProduct'; IsTest = 'false'; Allowed = $false },
            @{ Name = 'NamingProbe.Tests'; IsTest = 'true'; Allowed = $true },
            @{ Name = 'PretendProduct'; IsTest = 'true'; Allowed = $false },
            @{ Name = 'SuffixOnly.Tests'; IsTest = 'false'; Allowed = $false }
        )
        foreach ($case in $namingCases) {
            $caseDirectory = Join-Path $fixtureDirectory $case.Name
            [void][System.IO.Directory]::CreateDirectory($caseDirectory)
            $caseProject = Join-Path $caseDirectory ($case.Name + '.csproj')
            $caseXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><SpeechMessageStrictCode>true</SpeechMessageStrictCode><IsTestProject>' + $case.IsTest + '</IsTestProject><GenerateDocumentationFile>true</GenerateDocumentationFile><DefineConstants>PROBE_NAMING</DefineConstants></PropertyGroup><ItemGroup><Compile Include="' + $namingSource + '" /></ItemGroup></Project>'
            [System.IO.File]::WriteAllText($caseProject, $caseXml + "`r`n", $utf8)
            $naming = Invoke-AnalyzerDotNet -Arguments ('build "' + $caseProject + '" -c Release --nologo --disable-build-servers -t:Rebuild') -LogName ($case.Name + '.log')
            $checks++
            $namingCodes = @([regex]::Matches($naming.Output, '\berror ([A-Z]+[0-9]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
            if ($case.Allowed) {
                if ($naming.ExitCode -ne 0) { $failures.Add("Naming_QualifiedTest_MustBuild: exit=$($naming.ExitCode) errors=$($namingCodes -join ',')") }
            }
            elseif ($naming.ExitCode -eq 0 -or $namingCodes.Count -ne 1 -or $namingCodes[0] -ne 'CA1707') {
                $failures.Add("Naming_$($case.Name)_MustFailOnlyCA1707: exit=$($naming.ExitCode) errors=$($namingCodes -join ',')")
            }
        }
    }
    finally {
        $resolvedFixture = [System.IO.Path]::GetFullPath($fixtureDirectory)
        $allowedPrefix = $fixtureBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedFixture.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to clean a fixture outside its owned directory.'
        }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
    foreach ($failure in $failures) { Write-Output "FAIL $failure" }
    Write-Output "Analyzer probe: checks=$checks failures=$($failures.Count) buildExit=$($build.ExitCode) errors=$($errorCodes -join ',')"
    if ($failures.Count -gt 0) { exit 1 }
    exit 0
}
catch { Write-Output "FAIL Analyzer probe: $($_.Exception.Message)"; exit 1 }
