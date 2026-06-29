Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing','System.IO.Compression' -TypeDefinition @'
using System.Drawing;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class PngWriter
{
    public static void Save(Bitmap bitmap, string outputPath)
    {
        using (var output = File.Create(outputPath))
        {
            WriteSignature(output);
            WriteChunk(output, "IHDR", CreateIhdr(bitmap.Width, bitmap.Height));
            WriteChunk(output, "IDAT", CreateIdat(bitmap));
            WriteChunk(output, "IEND", new byte[0]);
        }
    }

    private static void WriteSignature(Stream output)
    {
        byte[] signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        output.Write(signature, 0, signature.Length);
    }

    private static byte[] CreateIhdr(int width, int height)
    {
        using (var memory = new MemoryStream())
        {
            WriteUInt32(memory, (uint)width);
            WriteUInt32(memory, (uint)height);
            memory.WriteByte(8); // bit depth
            memory.WriteByte(2); // truecolor RGB
            memory.WriteByte(0); // compression
            memory.WriteByte(0); // filter
            memory.WriteByte(0); // interlace
            return memory.ToArray();
        }
    }

    private static byte[] CreateIdat(Bitmap bitmap)
    {
        using (var raw = new MemoryStream())
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                raw.WriteByte(0); // filter type: None
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    raw.WriteByte(color.R);
                    raw.WriteByte(color.G);
                    raw.WriteByte(color.B);
                }
            }

            raw.Position = 0;
            using (var compressed = new MemoryStream())
            {
                using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                {
                    raw.CopyTo(deflate);
                }
                return compressed.ToArray();
            }
        }
    }

    private static void WriteChunk(Stream output, string chunkType, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(chunkType);
        WriteUInt32(output, (uint)data.Length);
        output.Write(typeBytes, 0, typeBytes.Length);
        output.Write(data, 0, data.Length);

        byte[] crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        WriteUInt32(output, Crc32(crcInput));
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        output.WriteByte((byte)((value >> 24) & 255));
        output.WriteByte((byte)((value >> 16) & 255));
        output.WriteByte((byte)((value >> 8) & 255));
        output.WriteByte((byte)(value & 255));
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xffffffff;
        for (int i = 0; i < bytes.Length; i++)
        {
            crc ^= bytes[i];
            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xedb88320 & mask);
            }
        }
        return ~crc;
    }
}
'@

# PowerShell 5.1 在不同主控台編碼下可能會把中文檔名解讀成亂碼，
# 甚至產生 Bitmap.Save() 無法接受的不合法路徑字元。
# 因此 PNG 輸出固定使用 ASCII 檔名，避免使用者在 VS/PowerShell 內執行時失敗。
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$outputPath = [System.IO.Path]::Combine($scriptDir, 'payment-flow-clear.png')
$width = 2400
$height = 3000

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$graphics.Clear([System.Drawing.Color]::White)

$fontFamily = 'Microsoft JhengHei'
$titleFont = New-Object System.Drawing.Font($fontFamily, 34, [System.Drawing.FontStyle]::Bold)
$groupFont = New-Object System.Drawing.Font($fontFamily, 21, [System.Drawing.FontStyle]::Bold)
$nodeTitleFont = New-Object System.Drawing.Font($fontFamily, 18, [System.Drawing.FontStyle]::Bold)
$nodeFont = New-Object System.Drawing.Font($fontFamily, 16, [System.Drawing.FontStyle]::Regular)
$smallFont = New-Object System.Drawing.Font($fontFamily, 13, [System.Drawing.FontStyle]::Regular)

$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(17, 24, 39))
$captionBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))
$lineColor = [System.Drawing.Color]::FromArgb(51, 65, 85)
$arrowPen = New-Object System.Drawing.Pen($lineColor, 3)
$arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor

function New-Brush($hex) {
    return New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($hex))
}

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

    $titleRect = New-Object System.Drawing.RectangleF(($x + 14), ($y + 12), ($w - 28), 30)
    $bodyRect = New-Object System.Drawing.RectangleF(($x + 14), ($y + 48), ($w - 28), ($h - 56))
    $graphics.DrawString($title, $nodeTitleFont, $textBrush, $titleRect)
    $graphics.DrawString($body, $nodeFont, $textBrush, $bodyRect)

    $script:nodes[$key] = [pscustomobject]@{ X = $x; Y = $y; W = $w; H = $h }
    $fill.Dispose()
    $pen.Dispose()
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
    $graphics.DrawLine($arrowPen, [int]$from.X, [int]$from.Y, [int]$to.X, [int]$to.Y)
    if ($label -ne '') {
        $graphics.DrawString($label, $smallFont, $captionBrush, [int](($from.X + $to.X) / 2 + 8), [int](($from.Y + $to.Y) / 2 - 20))
    }
}

function Draw-Polyline($points, $label = '') {
    for ($i = 0; $i -lt $points.Count - 2; $i++) {
        $plainPen = New-Object System.Drawing.Pen($lineColor, 3)
        $graphics.DrawLine($plainPen, [int]$points[$i].X, [int]$points[$i].Y, [int]$points[$i + 1].X, [int]$points[$i + 1].Y)
        $plainPen.Dispose()
    }
    $graphics.DrawLine($arrowPen, [int]$points[$points.Count - 2].X, [int]$points[$points.Count - 2].Y, [int]$points[$points.Count - 1].X, [int]$points[$points.Count - 1].Y)
    if ($label -ne '') {
        $mid = $points[[Math]::Floor($points.Count / 2)]
        $graphics.DrawString($label, $smallFont, $captionBrush, ([int]$mid.X + 8), ([int]$mid.Y - 24))
    }
}

$script:nodes = @{}

$graphics.DrawString('Complete Payment Call Flow - Clear Version', $titleFont, $textBrush, 660, 32)
$graphics.DrawString('Product workflow, ASP.NET Core host glue, reusable payment core, provider callback parsing, and post-payment handlers are separated.', $smallFont, $captionBrush, 500, 88)

Draw-Group 80 150 2240 340 '1. Product creates payment request' '#fff7ed' '#c2410c'
Draw-Group 80 535 2240 430 '2. SpeechMessage.Payments selects provider' '#eef2ff' '#4f46e5'
Draw-Group 80 1010 2240 285 '3. External provider payment page' '#f0fdf4' '#16a34a'
Draw-Group 80 1340 2240 410 '4. Callback returns to ChurchReport and core parser' '#ecfeff' '#0891b2'
Draw-Group 80 1795 2240 430 '5. Reusable host/workflow layer' '#f8fafc' '#64748b'
Draw-Group 80 2270 2240 455 '6. ChurchReport concrete product implementation' '#fff7ed' '#c2410c'

Draw-Node 'user' 140 235 260 105 'Donor/User' "Input donor data`namount, method" '#fef3c7' '#d97706'
Draw-Node 'controller' 500 235 320 105 'ChurchReport' "DedicationController`nQPay/MyPay/TSPG route" '#ffedd5' '#c2410c'
Draw-Node 'resolver' 920 235 320 105 'Profile Resolver' "PAY_PROVIDER ->`nPayment ProfileName" '#ffedd5' '#c2410c'
Draw-Node 'factory' 1340 235 360 105 'CreateRequestFactory' "Product data ->`nPaymentCreateRequest" '#cffafe' '#0891b2'
Draw-Node 'adapter' 1800 235 360 105 'Legacy Adapter' "Keeps old QPay callers`nusing neutral core" '#ffedd5' '#c2410c'

Draw-Node 'gatewayCreate' 140 655 320 110 'IPaymentGateway' 'CreatePaymentAsync' '#e0e7ff' '#4f46e5'
Draw-Node 'providerSwitch' 560 655 280 110 'Provider Router' "Choose by profile`nand provider kind" '#fef9c3' '#ca8a04'
Draw-Node 'sinopac' 930 610 315 118 'Sinopac/QPay' "sign/encrypt`ncard or ATM order" '#e0e7ff' '#4f46e5'
Draw-Node 'mypay' 1300 610 315 118 'MyPay' "map request`ncreate MyPay order" '#e0e7ff' '#4f46e5'
Draw-Node 'taishin' 1670 610 315 118 'Taishin/TSPG' "hash mapping`ncreate TSPG order" '#e0e7ff' '#4f46e5'
Draw-Node 'createResult' 975 810 650 96 'PaymentCreateResult' 'PaymentPageUrl + ProviderOrderRef. Product host redirects user.' '#e0e7ff' '#4f46e5'

Draw-Node 'providerPage' 420 1110 380 100 'Provider page' "Sinopac / MyPay / Taishin`ncard input or ATM data" '#dcfce7' '#16a34a'
Draw-Node 'paymentDone' 1140 1110 380 100 'Payment result' "Provider creates callback`nor browser return" '#dcfce7' '#16a34a'
Draw-Node 'providerAck' 1740 1110 360 100 'Provider waits ack' "PlainText / JSON / Redirect`nfrom acknowledgement mapper" '#dcfce7' '#16a34a'

Draw-Node 'callbackController' 140 1450 390 110 'Callback Controller' "MyPayController / TSPGController`nQPayCardController" '#cffafe' '#0891b2'
Draw-Node 'httpMapper' 650 1450 350 110 'HttpRequestMapper' "HttpRequest ->`nPaymentCallbackRequest" '#cffafe' '#0891b2'
Draw-Node 'gatewayParse' 1120 1450 310 110 'IPaymentGateway' 'ParseCallbackAsync' '#e0e7ff' '#4f46e5'
Draw-Node 'parser' 1540 1450 360 110 'Provider Parser' "verify/decrypt/hash`nnormalize status" '#e0e7ff' '#4f46e5'
Draw-Node 'callbackResult' 1980 1450 280 110 'CallbackResult' "order, status, amount`nack, diagnostics" '#e0e7ff' '#4f46e5'

Draw-Node 'ackMapper' 160 1915 360 110 'Ack Result Mapper' "Payment ack -> IActionResult`nresponse to provider" '#cffafe' '#0891b2'
Draw-Node 'workflowMapper' 660 1915 330 110 'Workflow Mapper' "CallbackResult ->`npost-payment context" '#f1f5f9' '#64748b'
Draw-Node 'postWorkflow' 1120 1915 330 110 'PostPaymentWorkflow' "runs updater and notifier`nthrough interfaces" '#f1f5f9' '#64748b'
Draw-Node 'recordInterface' 1540 1860 330 100 'IPaymentRecordUpdater' 'abstract product record update' '#f1f5f9' '#64748b'
Draw-Node 'notifyInterface' 1540 1995 330 100 'IPaymentPayerNotifier' 'abstract payer notification' '#f1f5f9' '#64748b'

Draw-Node 'crm' 320 2395 420 115 'ChurchReportRecordUpdater' "updates CRM payment bill`nor donation record" '#ffedd5' '#c2410c'
Draw-Node 'line' 980 2395 420 115 'ChurchReportPayerNotifier' "sends LINE notification`nfuture products can replace it" '#ffedd5' '#c2410c'
Draw-Node 'resultPage' 1640 2395 420 115 'Result page / next step' "success, failed, pending`ndecided by product host" '#ffedd5' '#c2410c'

Draw-Line 'user' 'R' 'controller' 'L'
Draw-Line 'controller' 'R' 'resolver' 'L'
Draw-Line 'resolver' 'R' 'factory' 'L'
Draw-Line 'factory' 'R' 'adapter' 'L'
Draw-Polyline @((EdgePoint 'adapter' 'B'), [pscustomobject]@{X=1980;Y=515}, [pscustomobject]@{X=300;Y=515}, (EdgePoint 'gatewayCreate' 'T')) 'create payment'
Draw-Line 'gatewayCreate' 'R' 'providerSwitch' 'L'
Draw-Line 'providerSwitch' 'R' 'sinopac' 'L' 'Sinopac'
Draw-Line 'providerSwitch' 'R' 'mypay' 'L' 'MyPay'
Draw-Line 'providerSwitch' 'R' 'taishin' 'L' 'Taishin'
Draw-Polyline @((EdgePoint 'sinopac' 'B'), [pscustomobject]@{X=1088;Y=790}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'mypay' 'B'), [pscustomobject]@{X=1458;Y=790}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'taishin' 'B'), [pscustomobject]@{X=1828;Y=790}, [pscustomobject]@{X=1625;Y=790}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'createResult' 'B'), [pscustomobject]@{X=1300;Y=995}, (EdgePoint 'providerPage' 'T')) 'redirect'
Draw-Line 'providerPage' 'R' 'paymentDone' 'L'
Draw-Line 'paymentDone' 'R' 'providerAck' 'L'
Draw-Polyline @((EdgePoint 'paymentDone' 'B'), [pscustomobject]@{X=1330;Y=1320}, [pscustomobject]@{X=335;Y=1320}, (EdgePoint 'callbackController' 'T')) 'callback / return'
Draw-Line 'callbackController' 'R' 'httpMapper' 'L'
Draw-Line 'httpMapper' 'R' 'gatewayParse' 'L'
Draw-Line 'gatewayParse' 'R' 'parser' 'L'
Draw-Line 'parser' 'R' 'callbackResult' 'L'
Draw-Polyline @((EdgePoint 'callbackResult' 'B'), [pscustomobject]@{X=2120;Y=1780}, [pscustomobject]@{X=340;Y=1780}, (EdgePoint 'ackMapper' 'T')) 'ack path'
Draw-Polyline @((EdgePoint 'ackMapper' 'T'), [pscustomobject]@{X=340;Y=1325}, [pscustomobject]@{X=1920;Y=1325}, (EdgePoint 'providerAck' 'B'))
Draw-Polyline @((EdgePoint 'callbackResult' 'B'), [pscustomobject]@{X=2120;Y=1840}, [pscustomobject]@{X=825;Y=1840}, (EdgePoint 'workflowMapper' 'T')) 'post-payment path'
Draw-Line 'workflowMapper' 'R' 'postWorkflow' 'L'
Draw-Line 'postWorkflow' 'R' 'recordInterface' 'L'
Draw-Line 'postWorkflow' 'R' 'notifyInterface' 'L'
Draw-Polyline @((EdgePoint 'recordInterface' 'B'), [pscustomobject]@{X=1705;Y=2250}, [pscustomobject]@{X=530;Y=2250}, (EdgePoint 'crm' 'T'))
Draw-Polyline @((EdgePoint 'notifyInterface' 'B'), [pscustomobject]@{X=1705;Y=2265}, [pscustomobject]@{X=1190;Y=2265}, (EdgePoint 'line' 'T'))
Draw-Line 'crm' 'R' 'line' 'L'
Draw-Line 'line' 'R' 'resultPage' 'L'

$graphics.DrawString('Boundary rule: SpeechMessage.Payments has no ASP.NET/CRM/LINE dependency. AspNetCore maps HTTP. Workflows define abstractions. ChurchReport owns CRM and LINE implementations.', $nodeFont, $textBrush, 160, 2800)

if ([System.IO.File]::Exists($outputPath)) {
    [System.IO.File]::Delete($outputPath)
}

# 透過 C# helper 明確呼叫 Bitmap.Save(string, ImageFormat.Png)，避免
# Windows PowerShell 5.1 對 System.Drawing 多載解析錯誤而產生 0 byte 檔。
[PngWriter]::Save($bitmap, $outputPath)

$outputFile = Get-Item -LiteralPath $outputPath
if ($outputFile.Length -le 0) {
    throw "PNG render failed: output file is empty."
}

$graphics.Dispose()
$bitmap.Dispose()
$arrowPen.Dispose()
$titleFont.Dispose()
$groupFont.Dispose()
$nodeTitleFont.Dispose()
$nodeFont.Dispose()
$smallFont.Dispose()
$textBrush.Dispose()
$captionBrush.Dispose()

Write-Output $outputPath
