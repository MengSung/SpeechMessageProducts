<#
檔案整體說明

目的與定位：編碼守門與無損轉換共用的唯讀位元組、雜湊及 Git 邊界工具。

主要功能與流程：
操作入口包含Invoke-TextQualityGit、Get-TextByteIssues、Get-TextSha256、Get-TextLiteralSegments；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
.SYNOPSIS
編碼守門與無損轉換共用的唯讀位元組、雜湊及 Git 邊界工具。
.DESCRIPTION
所有資料只活在單次腳本呼叫；串流與雜湊演算法都有明確釋放路徑，不保存來源文字到全域快取。
Git 程序最長三十秒，輸出以 UTF-8 及 NUL 路徑分隔處理，不走訪父工作樹或忽略產物。
#>

function Invoke-TextQualityGit {
    <# 執行不經 shell 的 Git 唯讀操作，正確引用每個 Windows 引數並在結束／逾時後釋放資源。 #>
    param([string]$Root, [string[]]$Arguments)
    $quoted = foreach ($value in (@('-c','core.longpaths=true','-c','core.quotepath=false') + $Arguments)) {
        '"' + [regex]::Replace([regex]::Replace($value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
    }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = 'git'
    $start.Arguments = $quoted -join ' '
    $start.WorkingDirectory = $Root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = New-Object Text.UTF8Encoding($false, $true)
    $start.StandardErrorEncoding = New-Object Text.UTF8Encoding($false, $true)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            $process.WaitForExit()
            throw 'Git text inventory timed out.'
        }
        $output = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Git text inventory failed: $errorText" }
        return $output
    }
    finally { $process.Dispose() }
}

function Get-TextByteIssues {
    <# 檢查原始位元組及 PS token；不可讓 BOM 自動剝除或解碼替代字元掩蓋錯誤。 #>
    param([byte[]]$Bytes, [string]$Extension)
    $issues = New-Object 'Collections.Generic.List[string]'
    if (($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) -or
        ($Bytes.Length -ge 2 -and (($Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) -or ($Bytes[0] -eq 0xFE -and $Bytes[1] -eq 0xFF))) -or
        ($Bytes.Length -ge 4 -and $Bytes[0] -eq 0 -and $Bytes[1] -eq 0 -and $Bytes[2] -eq 0xFE -and $Bytes[3] -eq 0xFF)) { $issues.Add('Bom') }
    $source = $null
    try { $source = (New-Object Text.UTF8Encoding($false, $true)).GetString($Bytes) }
    catch [Text.DecoderFallbackException] { $issues.Add('InvalidUtf8') }
    $lineText = $source
    if ($null -eq $lineText) { $lineText = [Text.Encoding]::ASCII.GetString($Bytes) }
    if ($lineText -match '(?<!\r)\n|\r(?!\n)') { $issues.Add('NonCrlfLineEndings') }
    if ($Bytes.Length -lt 2 -or $Bytes[$Bytes.Length - 2] -ne 13 -or $Bytes[$Bytes.Length - 1] -ne 10) { $issues.Add('MissingFinalCrlf') }
    if ($null -ne $source -and $source.Contains([string][char]0xFFFD)) { $issues.Add('ReplacementCharacter') }
    if ($Extension -eq '.ps1' -and $null -ne $source) {
        $tokens = $null
        $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) { $issues.Add('PowerShellParseError') }
        $executable = $source.ToCharArray()
        foreach ($token in $tokens) {
            if ($token.Kind -eq 'Comment') {
                for ($i = $token.Extent.StartOffset; $i -lt $token.Extent.EndOffset; $i++) { $executable[$i] = ' ' }
            }
        }
        if (($executable -join '') -match '[^\x00-\x7F]') { $issues.Add('NonAsciiExecutablePowerShell') }
    }
    return $issues.ToArray()
}

function Get-TextSha256 {
    <# 對原始或候選位元組計算雜湊；只回傳摘要，不把來源內容寫入 manifest。 #>
    param([byte[]]$Bytes)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Get-TextLiteralSegments {
    <# 保守擷取一般、逐字與 raw 字串；換行會改變常值內容時拒絕寫入，避免編碼轉換偷改行為。 #>
    param([string]$Source)
    $pattern = '(?s)(?<raw>"{3,})(?:(?!\k<raw>).)*\k<raw>|@"(?:""|[^"])*"|"(?:\\.|[^"\\\r\n])*"'
    $expression = New-Object Text.RegularExpressions.Regex($pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant, [TimeSpan]::FromSeconds(2))
    return @($expression.Matches($Source) | ForEach-Object { $_.Value })
}
