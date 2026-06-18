# 彙總 Trace.log 的 [Perf] 行：找最慢端點 / 疑似 N+1 / 盲區(gap)。
# 用法：powershell -File Tools\parse-perf-log.ps1 -Log Logs\Trace.log -Top 20
param([string]$Log = "Logs\Trace.log", [int]$Top = 20)

$rx = [regex]'\[Perf\] path=(?<path>\S+) total=(?<total>\d+)ms action=(?<action>\d+)ms crm\{n=(?<n>\d+),ms=(?<crm>\d+)\} gap=(?<gap>\d+)ms'
$rows = Get-Content -LiteralPath $Log | ForEach-Object {
    $m = $rx.Match($_)
    if ($m.Success) {
        [pscustomobject]@{
            Path   = $m.Groups['path'].Value
            Total  = [int]$m.Groups['total'].Value
            Action = [int]$m.Groups['action'].Value
            CrmN   = [int]$m.Groups['n'].Value
            CrmMs  = [int]$m.Groups['crm'].Value
            Gap    = [int]$m.Groups['gap'].Value
        }
    }
}

if (-not $rows) { Write-Host "找不到 [Perf] 行（確認已開 Profiling:Enabled 並以 Debug 跑過）"; return }

Write-Host "=== 依端點彙總（最慢在上）===" -ForegroundColor Cyan
$rows | Group-Object Path | ForEach-Object {
    [pscustomobject]@{
        Path     = $_.Name
        Hits     = $_.Count
        AvgTotal = [int]($_.Group | Measure-Object Total -Average).Average
        MaxTotal = ($_.Group | Measure-Object Total -Maximum).Maximum
        MaxCrmN  = ($_.Group | Measure-Object CrmN -Maximum).Maximum
        MaxGap   = ($_.Group | Measure-Object Gap -Maximum).Maximum
    }
} | Sort-Object MaxTotal -Descending | Select-Object -First $Top | Format-Table -AutoSize

Write-Host "=== 疑似 N+1（單次 crm.n 最高）===" -ForegroundColor Yellow
$rows | Sort-Object CrmN -Descending | Select-Object -First $Top Path, Total, CrmN, CrmMs, Gap | Format-Table -AutoSize

Write-Host "=== 盲區（gap 最大：proxy 路徑或 CPU）===" -ForegroundColor Magenta
$rows | Sort-Object Gap -Descending | Select-Object -First $Top Path, Total, Action, CrmMs, Gap | Format-Table -AutoSize
