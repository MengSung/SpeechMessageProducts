<#
檔案整體說明

目的與定位：這是 goodshepherdback 測試 LINE 頻道的本機互動設定工具，不屬於產品執行路徑；由使用者在自己的 Windows 登入工作階段手動啟動。
主要功能與流程：提示輸入頻道識別與機密、以 HTTPS 向 LINE 確認 Bot 身分、備份現有 User Secrets JSON、只合併指定測試鍵、重讀比對寫入結果，並提供還原最近備份的分支。
輸入與輸出：機密以 SecureString 提示輸入，轉成純文字僅供本次程序建立 HTTP Authorization 標頭與寫入本機 User Secrets；畫面只顯示帳號識別、欄位長度、備份檔名與不含秘密的續作指示。
適用範圍與不變條件：只操作目前使用者 APPDATA 下 `SpeechMessage.Dynamics.Deployment.P0` 的 goodshepherdback 測試設定；不修改 repo、CRM、IIS 或資料庫，不輸出秘密、不寫日誌，且保留未指定的現有 JSON 鍵。
失敗與驗證：輸入格式、JSON 結構、LINE Bot 身分及寫入後值均須通過核對；失敗即停止，不繼續切換。暫存 JSON 於 finally 刪除；為提供還原能力而保留的備份仍含機密，僅留在同一 User Secrets 目錄。
信任邊界與資源生命週期：操作者提供的值不得視為可記錄資料；SecureString BSTR 在 finally 以 ZeroFreeBSTR 釋放，HttpWebResponse 關閉且 StreamReader Dispose。純文字只在短命互動程序記憶體中使用，程序結束後由作業系統回收；保留備份是唯一刻意延長的機密副本。
維護與驗證：修改流程時須保留未知 JSON 鍵、失敗不覆寫、暫存檔必清及 HTTP 資源釋放契約；以 PowerShell Parser、UTF-8 無 BOM／CRLF 與 repo 編碼檢查驗證。

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
    4. 先備份原本的 secrets.json（放在同一個 User Secrets 資料夾），再直接合併指定鍵並透過暫存檔覆寫；
       寫入後重新讀取核對，秘密不會出現在命令列。
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
        if ($document.$name -is [pscustomobject]) { throw ([regex]::Unescape('secrets.json \u7684\u683C\u5F0F\u548C\u9810\u671F\u4E0D\u540C\uFF08\u4E0D\u662F\u55AE\u5C64\u9375\u503C\uFF09\uFF0C\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002\u8ACB\u628A\u9019\u500B\u756B\u9762\u8CBC\u7D66 Claude\u3002')) }
    }
    foreach ($key in $Values.Keys) {
        if ($document.PSObject.Properties[$key]) { $document.$key = [string]$Values[$key] }
        else { $document | Add-Member -NotePropertyName $key -NotePropertyValue ([string]$Values[$key]) }
    }
    foreach ($name in $before) {
        if (-not $document.PSObject.Properties[$name]) { throw ([regex]::Unescape('\u5408\u4F75\u5F8C\u7F3A\u5C11\u539F\u6709\u7684\u9375\uFF0C\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002\u8ACB\u628A\u9019\u500B\u756B\u9762\u8CBC\u7D66 Claude\u3002')) }
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
        if (-not $done) { throw ([regex]::Unescape('secrets.json \u6B63\u88AB\u5176\u4ED6\u7A0B\u5F0F\u4F7F\u7528\uFF0C\u91CD\u8A66\u5F8C\u4ECD\u7121\u6CD5\u5BEB\u5165\uFF0C\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002\u8ACB\u7A0D\u5F8C\u518D\u57F7\u884C\u4E00\u6B21\u3002')) }
    }
    finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
    $check = ([IO.File]::ReadAllText($secretsFile, [Text.Encoding]::UTF8)).TrimStart([char]0xFEFF) | ConvertFrom-Json
    foreach ($key in $Values.Keys) {
        if ([string]$check.$key -cne [string]$Values[$key]) { throw ([regex]::Unescape('\u5BEB\u5165\u5F8C\u6838\u5C0D\u4E0D\u4E00\u81F4\u3002\u8ACB\u9078 2 \u9084\u539F\u5099\u4EFD\uFF0C\u4E26\u628A\u9019\u500B\u756B\u9762\u8CBC\u7D66 Claude\u3002')) }
    }
}
Write-Host ''
Write-Host ([regex]::Unescape('=== \u5207\u63DB goodshepherdback \u6E2C\u8A66\u7528 LINE \u983B\u9053 ===')) -ForegroundColor Cyan
Write-Host ([regex]::Unescape('1 = \u586B\u5165\u65B0\u7684\u6E2C\u8A66\u983B\u9053'))
Write-Host ([regex]::Unescape('2 = \u9084\u539F\u4E0A\u4E00\u6B21\u7684\u5099\u4EFD\uFF08\u63DB\u56DE\u4E4B\u524D\u7684\u983B\u9053\uFF09'))
$choice = Read-Host ([regex]::Unescape('\u8ACB\u8F38\u5165 1 \u6216 2\uFF0C\u7136\u5F8C\u6309 Enter'))

if ($choice -eq '2') {
    $latest = Get-ChildItem -LiteralPath $secretsDir -Filter 'secrets.backup-*.json' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $latest) { Write-Host ([regex]::Unescape('\u627E\u4E0D\u5230\u4EFB\u4F55\u5099\u4EFD\uFF0C\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')) -ForegroundColor Yellow; return }
    Write-Host "$([regex]::Unescape('\u5C07\u9084\u539F\u9019\u4EFD\u5099\u4EFD\uFF1A'))$($latest.Name)"
    if ((Read-Host ([regex]::Unescape('\u78BA\u5B9A\u9084\u539F\u8ACB\u8F38\u5165 y'))) -ne 'y') { Write-Host ([regex]::Unescape('\u5DF2\u53D6\u6D88\uFF0C\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')); return }
    $current = Backup-Secrets
    Copy-Item -LiteralPath $latest.FullName -Destination $secretsFile -Force
    Write-Host ([regex]::Unescape('\u5DF2\u9084\u539F\u3002')) -ForegroundColor Green
    if ($current) { Write-Host "$([regex]::Unescape('\u9084\u539F\u524D\u7684\u5167\u5BB9\u53E6\u5B58\u70BA\uFF1A'))$(Split-Path -Leaf $current)" }
    Write-Host ([regex]::Unescape('\u8ACB\u544A\u8A34 Claude \u4F60\u5DF2\u9084\u539F\uFF0C\u7531\u5B83\u7D66\u4F60\u8981\u8CBC\u7D66 Codex \u7684\u8AAA\u660E\u3002')) -ForegroundColor Green
    return
}
if ($choice -ne '1') { Write-Host ([regex]::Unescape('\u6C92\u6709\u9078\u64C7 1 \u6216 2\uFF0C\u5DF2\u7D50\u675F\uFF0C\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')); return }

Write-Host ''
Write-Host ([regex]::Unescape('\u4EE5\u4E0B\u4E09\u500B\u503C\u90FD\u5728 LINE Developers Console\uFF08developers.line.biz\uFF09\u8A72\u983B\u9053\u7684\u9801\u9762\uFF1A')) -ForegroundColor Cyan
Write-Host ([regex]::Unescape('  Channel ID\u3001Channel secret\uFF1ABasic settings \u5206\u9801'))
Write-Host ([regex]::Unescape('  Channel access token (long-lived)\uFF1AMessaging API \u5206\u9801\u6700\u4E0B\u65B9\uFF0C\u6C92\u6709\u5C31\u6309 Issue'))
Write-Host ''

$channelId = (Read-Host ([regex]::Unescape('Channel ID\uFF0810 \u4F4D\u6578\u5B57\uFF0C\u770B\u5F97\u5230\uFF09'))).Trim()
if ($channelId -notmatch '^\d{8,12}$') { Write-Host ([regex]::Unescape('Channel ID \u61C9\u8A72\u662F 8\uFF5E12 \u4F4D\u6578\u5B57\u3002\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')) -ForegroundColor Red; return }

$channelSecret = (ConvertTo-Plain (Read-Host ([regex]::Unescape('Channel secret\uFF08\u8CBC\u4E0A\u5F8C\u6309 Enter\uFF0C\u756B\u9762\u4E0D\u6703\u986F\u793A\uFF09')) -AsSecureString)).Trim()
if ($channelSecret -notmatch '^[0-9a-fA-F]{32}$') { Write-Host ([regex]::Unescape('Channel secret \u61C9\u8A72\u662F 32 \u500B\u82F1\u6578\u5B57\uFF080-9\u3001a-f\uFF09\u3002\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')) -ForegroundColor Red; return }

$channelToken = (ConvertTo-Plain (Read-Host ([regex]::Unescape('Channel access token\uFF08\u8CBC\u4E0A\u5F8C\u6309 Enter\uFF0C\u756B\u9762\u4E0D\u6703\u986F\u793A\uFF09')) -AsSecureString)).Trim()
if ($channelToken.Length -lt 100 -or $channelToken -match '\s') { Write-Host ([regex]::Unescape('Channel access token \u770B\u8D77\u4F86\u4E0D\u5B8C\u6574\uFF08\u61C9\u8A72\u662F\u4E00\u9577\u4E32\u3001\u6C92\u6709\u7A7A\u767D\uFF09\u3002\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')) -ForegroundColor Red; return }

Write-Host ''
Write-Host ([regex]::Unescape('\u6B63\u5728\u5411 LINE \u78BA\u8A8D\u9019\u7D44 token \u5C6C\u65BC\u54EA\u500B\u5B98\u65B9\u5E33\u865F\u2026\u2026')) -ForegroundColor Cyan
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
    Write-Host ([regex]::Unescape('LINE \u4E0D\u63A5\u53D7\u9019\u7D44 Channel access token\uFF08\u53EF\u80FD\u8CBC\u932F\u6216\u5DF2\u5931\u6548\uFF09\u3002\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')) -ForegroundColor Red
    return
}
Write-Host ("  $([regex]::Unescape('\u5B98\u65B9\u5E33\u865F\u540D\u7A31\uFF1A')){0}" -f $bot.displayName) -ForegroundColor Yellow
Write-Host ("  $([regex]::Unescape('\u5B98\u65B9\u5E33\u865F')) ID $([regex]::Unescape('\uFF1A')){0}" -f $bot.basicId) -ForegroundColor Yellow
if ((Read-Host ([regex]::Unescape('\u9019\u662F\u4F60\u8981\u7528\u4F86\u6E2C\u8A66\u7684\u5E33\u865F\u55CE\uFF1F\u662F\u8ACB\u8F38\u5165 y'))) -ne 'y') { Write-Host ([regex]::Unescape('\u5DF2\u53D6\u6D88\uFF0C\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')); return }

Write-Host ''
Write-Host ([regex]::Unescape('\u6E2C\u8A66\u7528\u7684 LINE \u4F7F\u7528\u8005 ID\uFF08\u4F60\u81EA\u5DF1\u7684\u624B\u6A5F\u5E33\u865F\u5728\u9019\u500B\u983B\u9053\u7684 ID\uFF0CU \u958B\u982D\u5171 33 \u5B57\uFF09\u3002')) -ForegroundColor Cyan
Write-Host ([regex]::Unescape('  \u65B0\u820A\u983B\u9053\u5C6C\u65BC\u540C\u4E00\u500B Provider \u6642\u4E0D\u7528\u6539\uFF0C\u76F4\u63A5\u6309 Enter \u7565\u904E\u3002'))
Write-Host ([regex]::Unescape('  \u4E0D\u540C Provider \u6642\uFF0C\u5230 LINE Developers Console \u8A72\u983B\u9053 Basic settings \u6700\u4E0B\u65B9\u7684 Your user ID \u8907\u88FD\u3002'))
$userId = (ConvertTo-Plain (Read-Host ([regex]::Unescape('TestLineUserId\uFF08\u4E0D\u6539\u5C31\u76F4\u63A5\u6309 Enter\uFF0C\u756B\u9762\u4E0D\u6703\u986F\u793A\uFF09')) -AsSecureString)).Trim()
if ($userId -and $userId -notmatch '^U[0-9a-f]{32}$') { Write-Host ([regex]::Unescape('\u4F7F\u7528\u8005 ID \u61C9\u8A72\u662F U \u958B\u982D\u52A0 32 \u500B\u82F1\u6578\u5B57\u3002\u6C92\u6709\u505A\u4EFB\u4F55\u8B8A\u66F4\u3002')) -ForegroundColor Red; return }

$backup = Backup-Secrets
$values = @{}
$values[$prefix + 'TestChannelId'] = $channelId
$values[$prefix + 'TestChannelSecret'] = $channelSecret.ToLowerInvariant()
$values[$prefix + 'TestChannelToken'] = $channelToken
if ($userId) { $values[$prefix + 'TestLineUserId'] = $userId }
Set-SecretsDirect $values

Write-Host ''
Write-Host ([regex]::Unescape('\u5B8C\u6210\uFF01\u5DF2\u5B58\u9032\u672C\u6A5F User Secrets\uFF08\u4E0D\u5728 repo \u88E1\uFF09\u3002')) -ForegroundColor Green
Write-Host ("  Channel ID$([regex]::Unescape('\uFF1A')){0}" -f $channelId)
Write-Host ("  Channel secret$([regex]::Unescape('\uFF1A\u5DF2\u5B58\uFF08')){0} $([regex]::Unescape('\u5B57\uFF09'))" -f $channelSecret.Length)
Write-Host ("  Channel access token$([regex]::Unescape('\uFF1A\u5DF2\u5B58\uFF08')){0} $([regex]::Unescape('\u5B57\uFF09'))" -f $channelToken.Length)
Write-Host ("  TestLineUserId$([regex]::Unescape('\uFF1A')){0}" -f $(if ($userId) { ([regex]::Unescape('\u5DF2\u66F4\u65B0')) } else { ([regex]::Unescape('\u6CBF\u7528\u539F\u672C\u7684')) }))
if ($backup) { Write-Host ("  $([regex]::Unescape('\u539F\u672C\u7684\u8A2D\u5B9A\u5DF2\u5099\u4EFD\uFF1A')){0}" -f (Split-Path -Leaf $backup)) }
Write-Host ''
if ($channelId -eq '1646814154') {
    Write-Host ([regex]::Unescape('\u9019\u662F\u76EE\u524D\u6B63\u5728\u4F7F\u7528\u7684\u983B\u9053\uFF08Channel ID \u6C92\u6709\u8B8A\uFF09\uFF0C\u4E0D\u9700\u8981\u901A\u77E5 Codex \u91CD\u505A\u4EFB\u4F55\u6B65\u9A5F\u3002')) -ForegroundColor Cyan
    Write-Host ''
    return
}
Write-Host ([regex]::Unescape('\u4E0B\u4E00\u6B65\uFF1A\u628A\u4E0B\u9762\u9019\u6BB5\u8CBC\u7D66 Codex\uFF08\u4E0D\u542B\u4EFB\u4F55\u79D8\u5BC6\uFF09\uFF1A')) -ForegroundColor Cyan
Write-Host ''
Write-Host ("$([regex]::Unescape('\u4F7F\u7528\u8005\u6C7A\u5B9A\uFF08')){0}$([regex]::Unescape('\uFF09\uFF1A\u6E2C\u8A66')) LINE $([regex]::Unescape('\u983B\u9053\u6539\u7528\u300C')){1}$([regex]::Unescape('\u300D\uFF08')){2}$([regex]::Unescape('\uFF09\uFF0C\u65B0\u7684')) Channel ID $([regex]::Unescape('\u662F')) {3}$([regex]::Unescape('\u3002'))Channel secret$([regex]::Unescape('\u3001'))Channel access token $([regex]::Unescape('\u5DF2\u7531\u6211\u66F4\u65B0\u5230')) User Secrets $([regex]::Unescape('\u7684')) TestChannelSecret$([regex]::Unescape('\u3001'))TestChannelToken$([regex]::Unescape('\uFF0C'))Channel ID $([regex]::Unescape('\u53E6\u5B58\u5728')) TestChannelId{4}$([regex]::Unescape('\u3002\u539F\u672C\u7684')) Channel 1646814154 $([regex]::Unescape('\u5F9E\u73FE\u5728\u8D77\u4E0D\u8981\u518D\u4F7F\u7528\uFF0C\u4E5F\u4E0D\u8981\u5C0D\u5B83\u8A2D\u5B9A')) Webhook URL$([regex]::Unescape('\u3002\u8ACB\u6539\u7528\u65B0\u7684')) Channel ID $([regex]::Unescape('\u91CD\u505A\uFF1A'))protect-token$([regex]::Unescape('\u3001'))assign-channel$([regex]::Unescape('\uFF08\u6E2C\u8A66\u6559\u6703'))A$([regex]::Unescape('\uFF09\u3001'))mapping $([regex]::Unescape('\u74B0\u5883\u8B8A\u6578\uFF08'))LineWebhook:Channels:{3}$([regex]::Unescape('\uFF09\u3001'))Webhook $([regex]::Unescape('\u7DB2\u5740\u8DEF\u5F91\uFF08'))/api/line/webhook/{3}$([regex]::Unescape('\uFF09\u3001'))LINE Webhook $([regex]::Unescape('\u8A2D\u5B9A\u8207\u9A57\u6536\uFF1B\u5DF2\u7D93\u7528\u820A\u983B\u9053\u505A\u904E\u7684\u6B65\u9A5F\u8ACB\u91CD\u505A\u4E26\u5728')) evidence $([regex]::Unescape('\u8A3B\u660E\u3002'))" -f (Get-Date -Format 'yyyy-MM-dd HH:mm'), $bot.displayName, $bot.basicId, $channelId, $(if ($userId) { ([regex]::Unescape('\uFF0CTestLineUserId \u4E5F\u5DF2\u66F4\u65B0')) } else { ([regex]::Unescape('\uFF0CTestLineUserId \u6CBF\u7528')) }))
Write-Host ''
