# Summarize [Perf] lines in Trace.log: slow endpoints, suspected N+1, and gap hot spots.
# Usage: powershell -File Tools\parse-perf-log.ps1 -Log Logs\Trace.log -Top 20
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

if (-not $rows) { Write-Host "No [Perf] lines found. Check Profiling:Enabled=true and run the Debug build."; return }

Write-Host "=== Endpoint summary (slowest first) ===" -ForegroundColor Cyan
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

Write-Host "=== Suspected N+1 (highest crm.n) ===" -ForegroundColor Yellow
$rows | Sort-Object CrmN -Descending | Select-Object -First $Top Path, Total, CrmN, CrmMs, Gap | Format-Table -AutoSize

Write-Host "=== Gap hot spots (proxy path or CPU) ===" -ForegroundColor Magenta
$rows | Sort-Object Gap -Descending | Select-Object -First $Top Path, Total, Action, CrmMs, Gap | Format-Table -AutoSize
