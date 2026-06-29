Add-Type -AssemblyName System.Drawing

$outputPath = Join-Path $PSScriptRoot '完整金流呼叫流程_新版清晰版.png'

$width = 2400
$height = 3200
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$graphics.Clear([System.Drawing.Color]::White)

$fontFamily = 'Microsoft JhengHei'
$titleFont = New-Object System.Drawing.Font($fontFamily, 34, [System.Drawing.FontStyle]::Bold)
$groupFont = New-Object System.Drawing.Font($fontFamily, 21, [System.Drawing.FontStyle]::Bold)
$nodeFont = New-Object System.Drawing.Font($fontFamily, 17, [System.Drawing.FontStyle]::Regular)
$smallFont = New-Object System.Drawing.Font($fontFamily, 14, [System.Drawing.FontStyle]::Regular)

$black = [System.Drawing.Color]::FromArgb(17, 24, 39)
$lineColor = [System.Drawing.Color]::FromArgb(51, 65, 85)
$linePen = New-Object System.Drawing.Pen($lineColor, 3)
$linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
$borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(148, 163, 184), 2)

function New-Brush($hex) {
    return New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($hex))
}

$textBrush = New-Object System.Drawing.SolidBrush($black)
$captionBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))

function Draw-Group($x, $y, $w, $h, $title, $fillHex, $strokeHex) {
    $fill = New-Brush $fillHex
    $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml($strokeHex), 3)
    $rect = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
    $graphics.FillRectangle($fill, $rect)
    $graphics.DrawRectangle($pen, $rect)
    $graphics.DrawString($title, $groupFont, $textBrush, ($x + 18), ($y + 14))
    $fill.Dispose()
    $pen.Dispose()
}

function Draw-Node($key, $x, $y, $w, $h, $title, $body, $fillHex, $strokeHex) {
    $fill = New-Brush $fillHex
    $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml($strokeHex), 3)
    $rect = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
    $graphics.FillRectangle($fill, $rect)
    $graphics.DrawRectangle($pen, $rect)

    $titleRect = New-Object System.Drawing.RectangleF(($x + 16), ($y + 14), ($w - 32), 30)
    $bodyRect = New-Object System.Drawing.RectangleF(($x + 16), ($y + 50), ($w - 32), ($h - 62))
    $titleFontLocal = New-Object System.Drawing.Font($fontFamily, 18, [System.Drawing.FontStyle]::Bold)
    $graphics.DrawString($title, $titleFontLocal, $textBrush, $titleRect)
    $graphics.DrawString($body, $nodeFont, $textBrush, $bodyRect)

    $script:nodes[$key] = [pscustomobject]@{ X = $x; Y = $y; W = $w; H = $h }
    $titleFontLocal.Dispose()
    $fill.Dispose()
    $pen.Dispose()
}

function Center($key) {
    $n = $script:nodes[$key]
    return [pscustomobject]@{ X = $n.X + ($n.W / 2); Y = $n.Y + ($n.H / 2) }
}

function EdgePoint($key, $side) {
    $n = $script:nodes[$key]
    switch ($side) {
        'T' { return [pscustomobject]@{ X = $n.X + ($n.W / 2); Y = $n.Y } }
        'B' { return [pscustomobject]@{ X = $n.X + ($n.W / 2); Y = $n.Y + $n.H } }
        'L' { return [pscustomobject]@{ X = $n.X; Y = $n.Y + ($n.H / 2) } }
        'R' { return [pscustomobject]@{ X = $n.X + $n.W; Y = $n.Y + ($n.H / 2) } }
    }
}

function Draw-Line($fromKey, $fromSide, $toKey, $toSide, $label = '') {
    $from = EdgePoint $fromKey $fromSide
    $to = EdgePoint $toKey $toSide
    $graphics.DrawLine($linePen, [int]$from.X, [int]$from.Y, [int]$to.X, [int]$to.Y)
    if ($label -ne '') {
        $lx = [int](($from.X + $to.X) / 2) + 8
        $ly = [int](($from.Y + $to.Y) / 2) - 22
        $graphics.DrawString($label, $smallFont, $captionBrush, $lx, $ly)
    }
}

function Draw-Polyline($points, $label = '') {
    for ($i = 0; $i -lt $points.Count - 2; $i++) {
        $plainPen = New-Object System.Drawing.Pen($lineColor, 3)
        $graphics.DrawLine($plainPen, [int]$points[$i].X, [int]$points[$i].Y, [int]$points[$i + 1].X, [int]$points[$i + 1].Y)
        $plainPen.Dispose()
    }
    $graphics.DrawLine($linePen, [int]$points[$points.Count - 2].X, [int]$points[$points.Count - 2].Y, [int]$points[$points.Count - 1].X, [int]$points[$points.Count - 1].Y)
    if ($label -ne '') {
        $mid = $points[[Math]::Floor($points.Count / 2)]
        $graphics.DrawString($label, $smallFont, $captionBrush, ([int]$mid.X + 8), ([int]$mid.Y - 24))
    }
}

$script:nodes = @{}

$graphics.DrawString('完整金流呼叫流程（新版清晰版）', $titleFont, $textBrush, 720, 32)
$graphics.DrawString('從 ChurchReport 建立付款，到外部金流 callback，再到 CRM 更新與 LINE 通知。核心金流、ASP.NET 轉接層、付款後流程抽象彼此分離。', $smallFont, $captionBrush, 510, 88)

Draw-Group 80 150 2240 360 '1. 使用者與 ChurchReport 建立付款' '#fff7ed' '#c2410c'
Draw-Group 80 560 2240 440 '2. SpeechMessage.Payments 純金流核心選擇供應商' '#eef2ff' '#4f46e5'
Draw-Group 80 1050 2240 300 '3. 外部金流頁面' '#f0fdf4' '#16a34a'
Draw-Group 80 1400 2240 420 '4. callback / return 回到 ChurchReport，再交給核心解析' '#ecfeff' '#0891b2'
Draw-Group 80 1870 2240 440 '5. 可重用付款後流程抽象' '#f8fafc' '#64748b'
Draw-Group 80 2360 2240 500 '6. ChurchReport 產品專屬實作' '#fff7ed' '#c2410c'

Draw-Node 'user' 140 240 260 110 '奉獻者' "輸入姓名、項目、金額`n選擇信用卡/ATM/高鉅/台新" '#fef3c7' '#d97706'
Draw-Node 'controller' 500 240 320 110 'ChurchReport Controller' "DedicationController`nQPayCard / MyPay / TSPG" '#ffedd5' '#c2410c'
Draw-Node 'resolver' 920 240 320 110 'Profile Resolver' "依 PAY_PROVIDER`n決定 Payment Profile" '#ffedd5' '#c2410c'
Draw-Node 'factory' 1340 240 360 110 'PaymentCreateRequestFactory' "把產品資料轉成`n中立 PaymentCreateRequest" '#cffafe' '#0891b2'
Draw-Node 'adapter' 1800 240 360 110 '舊介面相容 Adapter' "保留舊 QPay 呼叫路徑`n內部改走中立核心" '#ffedd5' '#c2410c'

Draw-Node 'gatewayCreate' 140 690 320 115 'IPaymentGateway' 'CreatePaymentAsync' '#e0e7ff' '#4f46e5'
Draw-Node 'providerSwitch' 560 690 280 115 'Provider Router' "依 ProfileName`n選擇供應商" '#fef9c3' '#ca8a04'
Draw-Node 'sinopac' 960 650 300 120 '永豐 Sinopac/QPay' "簽章、加密`n建立信用卡或 ATM 交易" '#e0e7ff' '#4f46e5'
Draw-Node 'mypay' 1320 650 300 120 '高鉅 MyPay' "組合請求`n建立高鉅交易" '#e0e7ff' '#4f46e5'
Draw-Node 'taishin' 1680 650 300 120 '台新 Taishin/TSPG' "hash 與參數映射`n建立台新交易" '#e0e7ff' '#4f46e5'
Draw-Node 'createResult' 990 845 620 100 'PaymentCreateResult' '回傳 PaymentPageUrl 與 ProviderOrderRef，ChurchReport 只負責 Redirect' '#e0e7ff' '#4f46e5'

Draw-Node 'providerPage' 420 1140 380 105 '外部金流付款頁' "永豐 / 高鉅 / 台新`n輸入卡號或取得 ATM 帳號" '#dcfce7' '#16a34a'
Draw-Node 'paymentDone' 1140 1140 380 105 '付款完成或失敗' "金流平台產生 callback`n或使用者 return" '#dcfce7' '#16a34a'
Draw-Node 'providerAck' 1740 1140 360 105 '平台等待回覆' "PlainText / JSON / Redirect`n由 Ack mapper 產生" '#dcfce7' '#16a34a'

Draw-Node 'callbackController' 140 1510 390 115 'Callback Controller' "MyPayController / TSPGController`nQPayCardController" '#cffafe' '#0891b2'
Draw-Node 'httpMapper' 650 1510 350 115 'PaymentHttpRequestMapper' "HttpRequest 轉成`nPaymentCallbackRequest" '#cffafe' '#0891b2'
Draw-Node 'gatewayParse' 1120 1510 310 115 'IPaymentGateway' 'ParseCallbackAsync' '#e0e7ff' '#4f46e5'
Draw-Node 'parser' 1540 1510 360 115 'Provider Callback Parser' "驗簽/解密/hash`n轉換成中立狀態" '#e0e7ff' '#4f46e5'
Draw-Node 'callbackResult' 1980 1510 280 115 'PaymentCallbackResult' "訂單、狀態、金額`nAck、診斷資料" '#e0e7ff' '#4f46e5'

Draw-Node 'ackMapper' 160 1995 360 115 'PaymentAcknowledgementResultMapper' "將 Ack 轉 IActionResult`n回覆金流平台" '#cffafe' '#0891b2'
Draw-Node 'workflowMapper' 660 1995 330 115 'PaymentWorkflowResultMapper' "callback 結果轉成`n付款後 context" '#f1f5f9' '#64748b'
Draw-Node 'postWorkflow' 1120 1995 330 115 'PaymentPostPaymentWorkflow' "依序執行`n紀錄更新與通知抽象" '#f1f5f9' '#64748b'
Draw-Node 'recordInterface' 1540 1940 330 105 'IPaymentRecordUpdater' '產品付款紀錄更新抽象' '#f1f5f9' '#64748b'
Draw-Node 'notifyInterface' 1540 2075 330 105 'IPaymentPayerNotifier' '付款者通知抽象' '#f1f5f9' '#64748b'

Draw-Node 'crm' 320 2490 420 120 'ChurchReportPaymentRecordUpdater' "更新 CRM 付費單`n或奉獻紀錄" '#ffedd5' '#c2410c'
Draw-Node 'line' 980 2490 420 120 'ChurchReportPaymentPayerNotifier' "發送 LINE 通知付款者`n將來可替換 Email/簡訊" '#ffedd5' '#c2410c'
Draw-Node 'resultPage' 1640 2490 420 120 '結果頁 / 後續流程' "成功、失敗、等待付款`n由產品自行決定畫面" '#ffedd5' '#c2410c'

Draw-Line 'user' 'R' 'controller' 'L'
Draw-Line 'controller' 'R' 'resolver' 'L'
Draw-Line 'resolver' 'R' 'factory' 'L'
Draw-Line 'factory' 'R' 'adapter' 'L'
Draw-Polyline @((EdgePoint 'adapter' 'B'), [pscustomobject]@{X=1980;Y=530}, [pscustomobject]@{X=300;Y=530}, (EdgePoint 'gatewayCreate' 'T')) '建立付款'
Draw-Line 'gatewayCreate' 'R' 'providerSwitch' 'L'
Draw-Line 'providerSwitch' 'R' 'sinopac' 'L' '永豐'
Draw-Line 'providerSwitch' 'R' 'mypay' 'L' '高鉅'
Draw-Line 'providerSwitch' 'R' 'taishin' 'L' '台新'
Draw-Polyline @((EdgePoint 'sinopac' 'B'), [pscustomobject]@{X=1110;Y=820}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'mypay' 'B'), [pscustomobject]@{X=1470;Y=820}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'taishin' 'B'), [pscustomobject]@{X=1830;Y=820}, [pscustomobject]@{X=1610;Y=820}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'createResult' 'B'), [pscustomobject]@{X=1300;Y=1030}, (EdgePoint 'providerPage' 'T')) 'Redirect'
Draw-Line 'providerPage' 'R' 'paymentDone' 'L'
Draw-Line 'paymentDone' 'R' 'providerAck' 'L'
Draw-Polyline @((EdgePoint 'paymentDone' 'B'), [pscustomobject]@{X=1330;Y=1375}, [pscustomobject]@{X=335;Y=1375}, (EdgePoint 'callbackController' 'T')) 'callback / return'
Draw-Line 'callbackController' 'R' 'httpMapper' 'L'
Draw-Line 'httpMapper' 'R' 'gatewayParse' 'L'
Draw-Line 'gatewayParse' 'R' 'parser' 'L'
Draw-Line 'parser' 'R' 'callbackResult' 'L'
Draw-Polyline @((EdgePoint 'callbackResult' 'B'), [pscustomobject]@{X=2120;Y=1860}, [pscustomobject]@{X=340;Y=1860}, (EdgePoint 'ackMapper' 'T')) '回覆金流'
Draw-Polyline @((EdgePoint 'ackMapper' 'T'), [pscustomobject]@{X=340;Y=1380}, [pscustomobject]@{X=1920;Y=1380}, (EdgePoint 'providerAck' 'B'))
Draw-Polyline @((EdgePoint 'callbackResult' 'B'), [pscustomobject]@{X=2120;Y=1920}, [pscustomobject]@{X=825;Y=1920}, (EdgePoint 'workflowMapper' 'T')) '付款後流程'
Draw-Line 'workflowMapper' 'R' 'postWorkflow' 'L'
Draw-Line 'postWorkflow' 'R' 'recordInterface' 'L'
Draw-Line 'postWorkflow' 'R' 'notifyInterface' 'L'
Draw-Polyline @((EdgePoint 'recordInterface' 'B'), [pscustomobject]@{X=1705;Y=2335}, [pscustomobject]@{X=530;Y=2335}, (EdgePoint 'crm' 'T'))
Draw-Polyline @((EdgePoint 'notifyInterface' 'B'), [pscustomobject]@{X=1705;Y=2350}, [pscustomobject]@{X=1190;Y=2350}, (EdgePoint 'line' 'T'))
Draw-Line 'crm' 'R' 'line' 'L'
Draw-Line 'line' 'R' 'resultPage' 'L'

$legendY = 2920
$graphics.DrawString('邊界原則：', $groupFont, $textBrush, 120, $legendY)
$graphics.DrawString('SpeechMessage.Payments 不依賴 ASP.NET、Controller、CRM、LINE；SpeechMessage.Payments.AspNetCore 只處理 HTTP/DI 轉接；Workflows 只定義付款後抽象；ChurchReport 擁有 CRM/LINE 具體實作。', $nodeFont, $textBrush, 260, ($legendY + 4))

$bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

$graphics.Dispose()
$bitmap.Dispose()
$linePen.Dispose()
$borderPen.Dispose()
$titleFont.Dispose()
$groupFont.Dispose()
$nodeFont.Dispose()
$smallFont.Dispose()
$textBrush.Dispose()
$captionBrush.Dispose()

Write-Output $outputPath
