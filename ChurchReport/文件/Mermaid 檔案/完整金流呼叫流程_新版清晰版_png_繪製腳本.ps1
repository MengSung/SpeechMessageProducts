Add-Type -AssemblyName System.Drawing

if (-not ('PaymentFlowPngWriterV3' -as [type])) {
Add-Type -ReferencedAssemblies 'System.Drawing','System.IO.Compression' -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class PaymentFlowPngWriterV3
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
            memory.WriteByte(8);
            memory.WriteByte(2);
            memory.WriteByte(0);
            memory.WriteByte(0);
            memory.WriteByte(0);
            return memory.ToArray();
        }
    }

    private static byte[] CreateIdat(Bitmap bitmap)
    {
        using (var raw = new MemoryStream())
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                raw.WriteByte(0);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    raw.WriteByte(color.R);
                    raw.WriteByte(color.G);
                    raw.WriteByte(color.B);
                }
            }

            byte[] rawBytes = raw.ToArray();
            using (var zlib = new MemoryStream())
            {
                zlib.WriteByte(0x78);
                zlib.WriteByte(0x9C);
                using (var deflate = new DeflateStream(zlib, CompressionLevel.Optimal, true))
                {
                    deflate.Write(rawBytes, 0, rawBytes.Length);
                }
                WriteUInt32(zlib, Adler32(rawBytes));
                return zlib.ToArray();
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
        System.Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        System.Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
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

    private static uint Adler32(byte[] bytes)
    {
        const uint mod = 65521;
        uint a = 1;
        uint b = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            a = (a + bytes[i]) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }
}
'@
}

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$outputPath = [System.IO.Path]::Combine($scriptDir, 'payment-flow-clear-v3.png')
$width = 2600
$height = 3600

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

Draw-Group 80 150 2440 410 '1. Product creates payment request' '#fff7ed' '#c2410c'
Draw-Group 80 620 2440 540 '2. SpeechMessage.Payments selects provider' '#eef2ff' '#4f46e5'
Draw-Group 80 1220 2440 360 '3. External provider payment page' '#f0fdf4' '#16a34a'
Draw-Group 80 1640 2440 450 '4. Callback returns to ChurchReport and core parser' '#ecfeff' '#0891b2'
Draw-Group 80 2150 2440 500 '5. Reusable host/workflow layer' '#f8fafc' '#64748b'
Draw-Group 80 2710 2440 520 '6. ChurchReport concrete product implementation' '#fff7ed' '#c2410c'

Draw-Node 'user' 140 250 285 165 'Donor/User' "Input donor data`namount`npayment method" '#fef3c7' '#d97706'
Draw-Node 'controller' 540 250 350 165 'ChurchReport' "DedicationController`nQPay/MyPay/TSPG`nkeeps current routes" '#ffedd5' '#c2410c'
Draw-Node 'resolver' 1010 250 360 165 'Profile Resolver' "PAY_PROVIDER ->`nPayment ProfileName`nprovider-neutral choice" '#ffedd5' '#c2410c'
Draw-Node 'factory' 1490 250 390 165 'CreateRequestFactory' "Product data ->`nPaymentCreateRequest`nneutral DTO" '#cffafe' '#0891b2'
Draw-Node 'adapter' 2000 250 390 165 'Legacy Adapter' "Keeps old QPay callers`nwhile calling`nthe neutral core" '#ffedd5' '#c2410c'

Draw-Node 'gatewayCreate' 150 790 330 135 'IPaymentGateway' "CreatePaymentAsync`nentry point for all`npayment providers" '#e0e7ff' '#4f46e5'
Draw-Node 'providerSwitch' 610 790 315 135 'Provider Router' "Choose by profile`nand provider kind`nno product logic" '#fef9c3' '#ca8a04'
Draw-Node 'sinopac' 1030 690 330 155 'Sinopac/QPay' "sign / encrypt`ncard order`nATM order" '#e0e7ff' '#4f46e5'
Draw-Node 'mypay' 1440 690 330 155 'MyPay' "map request`ncreate MyPay order`nparse provider data" '#e0e7ff' '#4f46e5'
Draw-Node 'taishin' 1850 690 330 155 'Taishin/TSPG' "hash mapping`ncreate TSPG order`nparse provider data" '#e0e7ff' '#4f46e5'
Draw-Node 'createResult' 1050 980 790 120 'PaymentCreateResult' "PaymentPageUrl + ProviderOrderRef`nThe product host only redirects user to payment page." '#e0e7ff' '#4f46e5'

Draw-Node 'providerPage' 420 1320 430 155 'Provider page' "Sinopac / MyPay / Taishin`ncard input`nor ATM payment data" '#dcfce7' '#16a34a'
Draw-Node 'paymentDone' 1110 1320 430 155 'Payment result' "Provider completes payment`nthen sends callback`nor browser return" '#dcfce7' '#16a34a'
Draw-Node 'providerAck' 1790 1320 430 155 'Provider waits ack' "PlainText / JSON / Redirect`nresponse produced by`nacknowledgement mapper" '#dcfce7' '#16a34a'

Draw-Node 'callbackController' 140 1770 420 165 'Callback Controller' "MyPayController`nTSPGController`nQPayCardController" '#cffafe' '#0891b2'
Draw-Node 'httpMapper' 680 1770 390 165 'HttpRequestMapper' "HttpRequest ->`nPaymentCallbackRequest`nASP.NET boundary only" '#cffafe' '#0891b2'
Draw-Node 'gatewayParse' 1190 1770 340 165 'IPaymentGateway' "ParseCallbackAsync`nprovider-neutral`ncallback entry" '#e0e7ff' '#4f46e5'
Draw-Node 'parser' 1640 1770 390 165 'Provider Parser' "verify / decrypt / hash`nnormalize provider status`nmask diagnostics" '#e0e7ff' '#4f46e5'
Draw-Node 'callbackResult' 2140 1770 320 165 'CallbackResult' "order id`nstatus / amount`nack / diagnostics" '#e0e7ff' '#4f46e5'

Draw-Node 'ackMapper' 160 2300 390 165 'Ack Result Mapper' "Payment ack -> IActionResult`nresponse to provider`nno product workflow" '#cffafe' '#0891b2'
Draw-Node 'workflowMapper' 710 2300 380 165 'Workflow Mapper' "CallbackResult ->`npost-payment context`nshared mapping" '#f1f5f9' '#64748b'
Draw-Node 'postWorkflow' 1230 2300 380 165 'PostPaymentWorkflow' "runs updater`nand notifier`nthrough interfaces" '#f1f5f9' '#64748b'
Draw-Node 'recordInterface' 1740 2250 365 120 'IPaymentRecordUpdater' "abstract product`npayment record update" '#f1f5f9' '#64748b'
Draw-Node 'notifyInterface' 1740 2415 365 120 'IPaymentPayerNotifier' "abstract payer`nnotification" '#f1f5f9' '#64748b'

Draw-Node 'crm' 330 2860 470 140 'ChurchReportRecordUpdater' "updates CRM payment bill`nor donation record`nChurchReport-specific" '#ffedd5' '#c2410c'
Draw-Node 'line' 1070 2845 470 170 'ChurchReportPayerNotifier' "sends LINE notification`nfuture products can replace`nwith Email or SMS" '#ffedd5' '#c2410c'
Draw-Node 'resultPage' 1810 2845 470 170 'Result page / next step' "success / failed / pending`nUI is decided by`nthe product host" '#ffedd5' '#c2410c'

Draw-Line 'user' 'R' 'controller' 'L'
Draw-Line 'controller' 'R' 'resolver' 'L'
Draw-Line 'resolver' 'R' 'factory' 'L'
Draw-Line 'factory' 'R' 'adapter' 'L'
Draw-Polyline @((EdgePoint 'adapter' 'B'), [pscustomobject]@{X=2195;Y=585}, [pscustomobject]@{X=315;Y=585}, (EdgePoint 'gatewayCreate' 'T')) 'create payment'
Draw-Line 'gatewayCreate' 'R' 'providerSwitch' 'L'
Draw-Line 'providerSwitch' 'R' 'sinopac' 'L' 'Sinopac'
Draw-Line 'providerSwitch' 'R' 'mypay' 'L' 'MyPay'
Draw-Line 'providerSwitch' 'R' 'taishin' 'L' 'Taishin'
Draw-Polyline @((EdgePoint 'sinopac' 'B'), [pscustomobject]@{X=1175;Y=940}, [pscustomobject]@{X=1220;Y=940}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'mypay' 'B'), [pscustomobject]@{X=1605;Y=940}, [pscustomobject]@{X=1445;Y=940}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'taishin' 'B'), [pscustomobject]@{X=2035;Y=940}, [pscustomobject]@{X=1670;Y=940}, (EdgePoint 'createResult' 'T'))
Draw-Polyline @((EdgePoint 'createResult' 'B'), [pscustomobject]@{X=1445;Y=1190}, [pscustomobject]@{X=635;Y=1190}, (EdgePoint 'providerPage' 'T')) 'redirect'
Draw-Line 'providerPage' 'R' 'paymentDone' 'L'
Draw-Line 'paymentDone' 'R' 'providerAck' 'L'
Draw-Polyline @((EdgePoint 'paymentDone' 'B'), [pscustomobject]@{X=1325;Y=1610}, [pscustomobject]@{X=350;Y=1610}, (EdgePoint 'callbackController' 'T')) 'callback / return'
Draw-Line 'callbackController' 'R' 'httpMapper' 'L'
Draw-Line 'httpMapper' 'R' 'gatewayParse' 'L'
Draw-Line 'gatewayParse' 'R' 'parser' 'L'
Draw-Line 'parser' 'R' 'callbackResult' 'L'
Draw-Polyline @((EdgePoint 'callbackResult' 'B'), [pscustomobject]@{X=2300;Y=2115}, [pscustomobject]@{X=355;Y=2115}, (EdgePoint 'ackMapper' 'T')) 'ack path'
Draw-Polyline @((EdgePoint 'ackMapper' 'T'), [pscustomobject]@{X=355;Y=1600}, [pscustomobject]@{X=2005;Y=1600}, (EdgePoint 'providerAck' 'B'))
Draw-Polyline @((EdgePoint 'callbackResult' 'B'), [pscustomobject]@{X=2300;Y=2185}, [pscustomobject]@{X=900;Y=2185}, (EdgePoint 'workflowMapper' 'T')) 'post-payment path'
Draw-Line 'workflowMapper' 'R' 'postWorkflow' 'L'
Draw-Line 'postWorkflow' 'R' 'recordInterface' 'L'
Draw-Line 'postWorkflow' 'R' 'notifyInterface' 'L'
Draw-Polyline @((EdgePoint 'recordInterface' 'B'), [pscustomobject]@{X=1922;Y=2685}, [pscustomobject]@{X=565;Y=2685}, (EdgePoint 'crm' 'T'))
Draw-Polyline @((EdgePoint 'notifyInterface' 'B'), [pscustomobject]@{X=1922;Y=2700}, [pscustomobject]@{X=1305;Y=2700}, (EdgePoint 'line' 'T'))
Draw-Line 'crm' 'R' 'line' 'L'
Draw-Line 'line' 'R' 'resultPage' 'L'

$graphics.DrawString('Boundary rule: SpeechMessage.Payments has no ASP.NET/CRM/LINE dependency. AspNetCore maps HTTP. Workflows define abstractions. ChurchReport owns CRM and LINE implementations.', $nodeFont, $textBrush, 160, 3370)

if ([System.IO.File]::Exists($outputPath)) {
    [System.IO.File]::Delete($outputPath)
}

[PaymentFlowPngWriterV3]::Save($bitmap, $outputPath)

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
