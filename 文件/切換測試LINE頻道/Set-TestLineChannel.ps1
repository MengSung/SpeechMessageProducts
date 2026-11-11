<#
.SYNOPSIS
  切換 goodshepherdback 測試用的 LINE 頻道：把新的 Channel ID、Channel secret、Channel access token
  存進本機 User Secrets（不進 repo、不顯示在畫面上）。

.DESCRIPTION
  由使用者在本機（LENOVO-LEGION）的 PowerShell 執行，不需要系統管理員權限，但必須是平常登入的 Windows 帳號。
  流程：
    1. 選擇「填入新的測試頻道」或「還原上一次的備份」。
    2. 輸入 Channel ID（看得到）、Channel secret 與 Channel access token（輸入時不顯示）。
    3. 用這組 token 向 LINE 官方詢問「這是哪一個官方帳號」，把帳號名稱與 @ID 顯示出來讓使用者確認。
       確認不是預期的帳號就不寫入。
    4. 先備份原本的 secrets.json（放在同一個 User Secrets 資料夾），再以官方 dotnet user-secrets 工具
       經標準輸入寫入，秘密不會出現在命令列。
    5. 只顯示每個值的長度，並提示要貼給 Codex 的一句話。
  本腳本不呼叫 LINE 以外的任何網站，不修改 repo、CRM、IIS 或資料庫，不寫 log。
#>
$ErrorActionPreference = 'Stop'
$secretsId = 'SpeechMessage.Dynamics.Deployment.P0'
$prefix = 'Deployment:Organizations:goodshepherdback:'
$secretsDir = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId"
$secretsFile = Join-Path $secretsDir 'secrets.json'

function ConvertTo-Plain([Security.SecureString]$Secure) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function Backup-Secrets {
    if (-not (Test-Path -LiteralPath $secretsFile)) { return $null }
    $backup = Join-Path $secretsDir ('secrets.backup-{0}.json' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Copy-Item -LiteralPath $secretsFile -Destination $backup
    return $backup
}

function Set-SecretsDirect([hashtable]$Values) {
    # 直接合併 secrets.json，不經過 dotnet user-secrets 的標準輸入（在某些主控台編碼下會被拒絕）。
    # 只更新指定的鍵，其他鍵原樣保留；先寫暫存檔再覆蓋，檔案被其他程式暫時占用時重試。
    if (-not (Test-Path -LiteralPath $secretsDir)) { New-Item -ItemType Directory -Path $secretsDir | Out-Null }
    $text = '{}'
    if (Test-Path -LiteralPath $secretsFile) { $text = [IO.File]::ReadAllText($secretsFile, [Text.Encoding]::UTF8) }
    $text = $text.TrimStart([char]0xFEFF)
    if ([string]::IsNullOrWhiteSpace($text)) { $text = '{}' }
    $document = $text | ConvertFrom-Json
    $before = @($document.PSObject.Properties.Name)
    foreach ($name in $before) {
        if ($document.$name -is [pscustomobject]) { throw 'secrets.json 的格式和預期不同（不是單層鍵值），沒有做任何變更。請把這個畫面貼給 Claude。' }
    }
    foreach ($key in $Values.Keys) {
        if ($document.PSObject.Properties[$key]) { $document.$key = [string]$Values[$key] }
        else { $document | Add-Member -NotePropertyName $key -NotePropertyValue ([string]$Values[$key]) }
    }
    foreach ($name in $before) {
        if (-not $document.PSObject.Properties[$name]) { throw '合併後缺少原有的鍵，沒有做任何變更。請把這個畫面貼給 Claude。' }
    }
    $json = $document | ConvertTo-Json -Depth 5
    $temp = Join-Path $secretsDir ('secrets.tmp-{0}.json' -f [Guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllText($temp, $json, (New-Object Text.UTF8Encoding($true)))
        $done = $false
        for ($attempt = 1; $attempt -le 6 -and -not $done; $attempt++) {
            try { [IO.File]::Copy($temp, $secretsFile, $true); $done = $true }
            catch [IO.IOException] { Start-Sleep -Milliseconds 700 }
        }
        if (-not $done) { throw 'secrets.json 正被其他程式使用，重試後仍無法寫入，沒有做任何變更。請稍後再執行一次。' }
    }
    finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
    $check = ([IO.File]::ReadAllText($secretsFile, [Text.Encoding]::UTF8)).TrimStart([char]0xFEFF) | ConvertFrom-Json
    foreach ($key in $Values.Keys) {
        if ([string]$check.$key -cne [string]$Values[$key]) { throw '寫入後核對不一致。請選 2 還原備份，並把這個畫面貼給 Claude。' }
    }
}
Write-Host ''
Write-Host '=== 切換 goodshepherdback 測試用 LINE 頻道 ===' -ForegroundColor Cyan
Write-Host '1 = 填入新的測試頻道'
Write-Host '2 = 還原上一次的備份（換回之前的頻道）'
$choice = Read-Host '請輸入 1 或 2，然後按 Enter'

if ($choice -eq '2') {
    $latest = Get-ChildItem -LiteralPath $secretsDir -Filter 'secrets.backup-*.json' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $latest) { Write-Host '找不到任何備份，沒有做任何變更。' -ForegroundColor Yellow; return }
    Write-Host "將還原這份備份：$($latest.Name)"
    if ((Read-Host '確定還原請輸入 y') -ne 'y') { Write-Host '已取消，沒有做任何變更。'; return }
    $current = Backup-Secrets
    Copy-Item -LiteralPath $latest.FullName -Destination $secretsFile -Force
    Write-Host '已還原。' -ForegroundColor Green
    if ($current) { Write-Host "還原前的內容另存為：$(Split-Path -Leaf $current)" }
    Write-Host '請告訴 Claude 你已還原，由它給你要貼給 Codex 的說明。' -ForegroundColor Green
    return
}
if ($choice -ne '1') { Write-Host '沒有選擇 1 或 2，已結束，沒有做任何變更。'; return }

Write-Host ''
Write-Host '以下三個值都在 LINE Developers Console（developers.line.biz）該頻道的頁面：' -ForegroundColor Cyan
Write-Host '  Channel ID、Channel secret：Basic settings 分頁'
Write-Host '  Channel access token (long-lived)：Messaging API 分頁最下方，沒有就按 Issue'
Write-Host ''

$channelId = (Read-Host 'Channel ID（10 位數字，看得到）').Trim()
if ($channelId -notmatch '^\d{8,12}$') { Write-Host 'Channel ID 應該是 8～12 位數字。沒有做任何變更。' -ForegroundColor Red; return }

$channelSecret = (ConvertTo-Plain (Read-Host 'Channel secret（貼上後按 Enter，畫面不會顯示）' -AsSecureString)).Trim()
if ($channelSecret -notmatch '^[0-9a-fA-F]{32}$') { Write-Host 'Channel secret 應該是 32 個英數字（0-9、a-f）。沒有做任何變更。' -ForegroundColor Red; return }

$channelToken = (ConvertTo-Plain (Read-Host 'Channel access token（貼上後按 Enter，畫面不會顯示）' -AsSecureString)).Trim()
if ($channelToken.Length -lt 100 -or $channelToken -match '\s') { Write-Host 'Channel access token 看起來不完整（應該是一長串、沒有空白）。沒有做任何變更。' -ForegroundColor Red; return }

Write-Host ''
Write-Host '正在向 LINE 確認這組 token 屬於哪個官方帳號……' -ForegroundColor Cyan
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
try {
    # Windows PowerShell 的 Invoke-RestMethod 會把沒有標示編碼的回應當成 Latin-1，中文帳號名稱會變亂碼；
    # 改為自行讀取位元組並以 UTF-8 解碼。
    $request = [Net.HttpWebRequest]::Create('https://api.line.me/v2/bot/info')
    $request.Method = 'GET'
    $request.Timeout = 30000
    $request.Headers.Add('Authorization', "Bearer $channelToken")
    $response = $request.GetResponse()
    try {
        $reader = New-Object IO.StreamReader($response.GetResponseStream(), (New-Object Text.UTF8Encoding($false)))
        try { $bot = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    }
    finally { $response.Close() }
}
catch {
    Write-Host 'LINE 不接受這組 Channel access token（可能貼錯或已失效）。沒有做任何變更。' -ForegroundColor Red
    return
}
Write-Host ("  官方帳號名稱：{0}" -f $bot.displayName) -ForegroundColor Yellow
Write-Host ("  官方帳號 ID ：{0}" -f $bot.basicId) -ForegroundColor Yellow
if ((Read-Host '這是你要用來測試的帳號嗎？是請輸入 y') -ne 'y') { Write-Host '已取消，沒有做任何變更。'; return }

Write-Host ''
Write-Host '測試用的 LINE 使用者 ID（你自己的手機帳號在這個頻道的 ID，U 開頭共 33 字）。' -ForegroundColor Cyan
Write-Host '  新舊頻道屬於同一個 Provider 時不用改，直接按 Enter 略過。'
Write-Host '  不同 Provider 時，到 LINE Developers Console 該頻道 Basic settings 最下方的 Your user ID 複製。'
$userId = (ConvertTo-Plain (Read-Host 'TestLineUserId（不改就直接按 Enter，畫面不會顯示）' -AsSecureString)).Trim()
if ($userId -and $userId -notmatch '^U[0-9a-f]{32}$') { Write-Host '使用者 ID 應該是 U 開頭加 32 個英數字。沒有做任何變更。' -ForegroundColor Red; return }

$backup = Backup-Secrets
$values = @{}
$values[$prefix + 'TestChannelId'] = $channelId
$values[$prefix + 'TestChannelSecret'] = $channelSecret.ToLowerInvariant()
$values[$prefix + 'TestChannelToken'] = $channelToken
if ($userId) { $values[$prefix + 'TestLineUserId'] = $userId }
Set-SecretsDirect $values

Write-Host ''
Write-Host '完成！已存進本機 User Secrets（不在 repo 裡）。' -ForegroundColor Green
Write-Host ("  Channel ID：{0}" -f $channelId)
Write-Host ("  Channel secret：已存（{0} 字）" -f $channelSecret.Length)
Write-Host ("  Channel access token：已存（{0} 字）" -f $channelToken.Length)
Write-Host ("  TestLineUserId：{0}" -f $(if ($userId) { '已更新' } else { '沿用原本的' }))
if ($backup) { Write-Host ("  原本的設定已備份：{0}" -f (Split-Path -Leaf $backup)) }
Write-Host ''
if ($channelId -eq '1646814154') {
    Write-Host '這是目前正在使用的頻道（Channel ID 沒有變），不需要通知 Codex 重做任何步驟。' -ForegroundColor Cyan
    Write-Host ''
    return
}
Write-Host '下一步：把下面這段貼給 Codex（不含任何秘密）：' -ForegroundColor Cyan
Write-Host ''
Write-Host ("使用者決定（{0}）：測試 LINE 頻道改用「{1}」（{2}），新的 Channel ID 是 {3}。Channel secret、Channel access token 已由我更新到 User Secrets 的 TestChannelSecret、TestChannelToken，Channel ID 另存在 TestChannelId{4}。原本的 Channel 1646814154 從現在起不要再使用，也不要對它設定 Webhook URL。請改用新的 Channel ID 重做：protect-token、assign-channel（測試教會A）、mapping 環境變數（LineWebhook:Channels:{3}）、Webhook 網址路徑（/api/line/webhook/{3}）、LINE Webhook 設定與驗收；已經用舊頻道做過的步驟請重做並在 evidence 註明。" -f (Get-Date -Format 'yyyy-MM-dd HH:mm'), $bot.displayName, $bot.basicId, $channelId, $(if ($userId) { '，TestLineUserId 也已更新' } else { '，TestLineUserId 沿用' }))
Write-Host ''
