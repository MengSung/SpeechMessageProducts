Add-Type -AssemblyName System.Drawing

if (-not ('PaymentFlowPngWriterV5' -as [type])) {
Add-Type -ReferencedAssemblies 'System.Drawing','System.IO.Compression' -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class PaymentFlowPngWriterV5
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

$outputPath = [System.IO.Path]::Combine($scriptDir, 'payment-flow-clear-v5.png')
$width = 3200
$height = 4600

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$graphics.Clear([System.Drawing.Color]::White)

$fontFamily = 'Microsoft JhengHei'
$titleFont = New-Object System.Drawing.Font($fontFamily, 38, [System.Drawing.FontStyle]::Bold)
$subtitleFont = New-Object System.Drawing.Font($fontFamily, 18, [System.Drawing.FontStyle]::Regular)
$groupFont = New-Object System.Drawing.Font($fontFamily, 24, [System.Drawing.FontStyle]::Bold)
$nodeTitleFont = New-Object System.Drawing.Font($fontFamily, 20, [System.Drawing.FontStyle]::Bold)
$nodeFont = New-Object System.Drawing.Font($fontFamily, 17, [System.Drawing.FontStyle]::Regular)
$smallFont = New-Object System.Drawing.Font($fontFamily, 14, [System.Drawing.FontStyle]::Regular)

$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(17, 24, 39))
$captionBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))
$lineColor = [System.Drawing.Color]::FromArgb(51, 65, 85)
$arrowPen = New-Object System.Drawing.Pen($lineColor, 4)
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
    $graphics.DrawString($title, $groupFont, $textBrush, ($x + 24), ($y + 18))
    $fill.Dispose()
    $pen.Dispose()
}

function Draw-Node($key, $x, $y, $w, $h, $title, $body, $fillHex, $strokeHex) {
    $fill = New-Brush $fillHex
    $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml($strokeHex), 3)
    $rect = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
    $graphics.FillRectangle($fill, $rect)
    $graphics.DrawRectangle($pen, $rect)
    $titleRect = New-Object System.Drawing.RectangleF(($x + 18), ($y + 16), ($w - 36), 34)
    $bodyRect = New-Object System.Drawing.RectangleF(($x + 18), ($y + 58), ($w - 36), ($h - 70))
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
        $graphics.DrawString($label, $smallFont, $captionBrush, [int](($from.X + $to.X) / 2 + 10), [int](($from.Y + $to.Y) / 2 - 26))
    }
}

function Draw-Polyline($points, $label = '') {
    for ($i = 0; $i -lt $points.Count - 2; $i++) {
        $plainPen = New-Object System.Drawing.Pen($lineColor, 4)
        $graphics.DrawLine($plainPen, [int]$points[$i].X, [int]$points[$i].Y, [int]$points[$i + 1].X, [int]$points[$i + 1].Y)
        $plainPen.Dispose()
    }
    $graphics.DrawLine($arrowPen, [int]$points[$points.Count - 2].X, [int]$points[$points.Count - 2].Y, [int]$points[$points.Count - 1].X, [int]$points[$points.Count - 1].Y)
    if ($label -ne '') {
        $mid = $points[[Math]::Floor($points.Count / 2)]
        $graphics.DrawString($label, $smallFont, $captionBrush, ([int]$mid.X + 10), ([int]$mid.Y - 30))
    }
}

$script:nodes = @{}

$graphics.DrawString('Complete Payment Call Flow - Final Layout Candidate', $titleFont, $textBrush, 900, 35)
$graphics.DrawString('Main flow is vertical. Provider-specific and product-specific details stay on side branches so lines do not cover node text.', $subtitleFont, $captionBrush, 760, 92)

Draw-Group 120 170 1320 3680 'Main vertical payment flow' '#f8fafc' '#64748b'
Draw-Group 1540 170 1480 1150 'Provider-specific payment core' '#eef2ff' '#4f46e5'
Draw-Group 1540 1430 1480 930 'Provider callback parsing' '#ecfeff' '#0891b2'
Draw-Group 1540 2460 1480 1020 'Post-payment product implementation' '#fff7ed' '#c2410c'
Draw-Group 120 3920 2900 390 'Boundary summary' '#f8fafc' '#64748b'

Draw-Node 'n1' 360 300 840 150 '1. User submits payment' "ChurchReport receives donor name, item, amount, payment method, and related product data." '#fff7ed' '#c2410c'
Draw-Node 'n2' 360 560 840 150 '2. Resolve payment profile' "ChurchReportPaymentProfileResolver maps PAY_PROVIDER to a named Payment Profile." '#fff7ed' '#c2410c'
Draw-Node 'n3' 360 820 840 150 '3. Build neutral request' "PaymentCreateRequestFactory converts product data into PaymentCreateRequest." '#ecfeff' '#0891b2'
Draw-Node 'n4' 360 1080 840 150 '4. Create payment in core' "IPaymentGateway.CreatePaymentAsync routes the request to the selected provider." '#eef2ff' '#4f46e5'
Draw-Node 'n5' 360 1340 840 150 '5. Return payment page URL' "PaymentCreateResult returns PaymentPageUrl and ProviderOrderRef to the product host." '#eef2ff' '#4f46e5'
Draw-Node 'n6' 360 1600 840 150 '6. Redirect to provider page' "The user completes card input, ATM payment, or provider-hosted checkout." '#f0fdf4' '#16a34a'
Draw-Node 'n7' 360 1860 840 150 '7. Receive callback / return' "Existing MyPay, TSPG, and QPay return URLs are preserved in ChurchReport controllers." '#fff7ed' '#c2410c'
Draw-Node 'n8' 360 2120 840 150 '8. Map HTTP to neutral callback' "PaymentHttpRequestMapper converts HttpRequest into PaymentCallbackRequest." '#ecfeff' '#0891b2'
Draw-Node 'n9' 360 2380 840 150 '9. Parse callback in core' "IPaymentGateway.ParseCallbackAsync verifies provider data and normalizes status." '#eef2ff' '#4f46e5'
Draw-Node 'n10' 360 2640 840 150 '10. Normalized callback result' "PaymentCallbackResult contains ProductOrderId, Status, Amount, Ack, and sanitized diagnostics." '#eef2ff' '#4f46e5'
Draw-Node 'n11' 360 2900 840 150 '11. Reply to provider' "PaymentAcknowledgementResultMapper turns Ack into PlainText, JSON, or Redirect response." '#ecfeff' '#0891b2'
Draw-Node 'n12' 360 3160 840 150 '12. Execute post-payment workflow' "PaymentWorkflowResultMapper and PaymentPostPaymentWorkflow call product-owned handlers." '#f8fafc' '#64748b'
Draw-Node 'n13' 360 3420 840 150 '13. Show result or continue' "ChurchReport displays success, failure, pending payment, or product-specific next step." '#fff7ed' '#c2410c'

Draw-Node 'p1' 1700 360 500 150 'Sinopac/QPay Provider' "Signs and encrypts Sinopac/QPay requests. Handles card and ATM creation." '#e0e7ff' '#4f46e5'
Draw-Node 'p2' 2300 360 500 150 'MyPay Provider' "Maps neutral payment request to MyPay protocol and endpoint." '#e0e7ff' '#4f46e5'
Draw-Node 'p3' 2000 670 500 150 'Taishin/TSPG Provider' "Maps neutral request to TSPG parameters and hash rules." '#e0e7ff' '#4f46e5'
Draw-Node 'p4' 1850 980 820 150 'Provider output' "All providers return the same PaymentCreateResult shape to ChurchReport." '#e0e7ff' '#4f46e5'

Draw-Node 'c1' 1700 1620 500 150 'Sinopac callback parser' "Verifies provider data, decrypts if required, and maps status." '#cffafe' '#0891b2'
Draw-Node 'c2' 2300 1620 500 150 'MyPay callback parser' "Validates MyPay payload and maps payment result." '#cffafe' '#0891b2'
Draw-Node 'c3' 2000 1930 500 150 'Taishin callback parser' "Verifies TSPG hash and maps ret_code/state into normalized status." '#cffafe' '#0891b2'

Draw-Node 'w1' 1700 2645 500 150 'IPaymentRecordUpdater' "Reusable abstraction for updating a product payment record." '#ffedd5' '#c2410c'
Draw-Node 'w2' 2300 2645 500 150 'IPaymentPayerNotifier' "Reusable abstraction for notifying the payer." '#ffedd5' '#c2410c'
Draw-Node 'w3' 1700 3045 500 170 'ChurchReport updater' "Updates CRM payment bill or donation record." '#ffedd5' '#c2410c'
Draw-Node 'w4' 2300 3045 500 170 'ChurchReport notifier' "Sends LINE notification. Future products can use email or SMS." '#ffedd5' '#c2410c'

Draw-Node 'b1' 230 4040 760 150 'Pure core boundary' "SpeechMessage.Payments has no ASP.NET, Controller, CRM, LINE, ToolUtility, or product workflow dependency." '#eef2ff' '#4f46e5'
Draw-Node 'b2' 1110 4040 760 150 'ASP.NET host boundary' "SpeechMessage.Payments.AspNetCore maps HttpRequest and acknowledgement responses only." '#ecfeff' '#0891b2'
Draw-Node 'b3' 1990 4040 760 150 'Product boundary' "ChurchReport owns CRM updates, LINE notification, pages, route compatibility, and product decisions." '#fff7ed' '#c2410c'

Draw-Line 'n1' 'B' 'n2' 'T'
Draw-Line 'n2' 'B' 'n3' 'T'
Draw-Line 'n3' 'B' 'n4' 'T'
Draw-Line 'n4' 'B' 'n5' 'T'
Draw-Line 'n5' 'B' 'n6' 'T'
Draw-Line 'n6' 'B' 'n7' 'T'
Draw-Line 'n7' 'B' 'n8' 'T'
Draw-Line 'n8' 'B' 'n9' 'T'
Draw-Line 'n9' 'B' 'n10' 'T'
Draw-Line 'n10' 'B' 'n11' 'T'
Draw-Line 'n10' 'B' 'n12' 'T'
Draw-Line 'n12' 'B' 'n13' 'T'

Draw-Polyline @((EdgePoint 'n4' 'R'), [pscustomobject]@{X=1510;Y=1155}, [pscustomobject]@{X=1510;Y=435}, (EdgePoint 'p1' 'L')) 'provider selection'
Draw-Line 'p1' 'R' 'p2' 'L'
Draw-Polyline @((EdgePoint 'p2' 'B'), [pscustomobject]@{X=2550;Y=610}, [pscustomobject]@{X=2250;Y=610}, (EdgePoint 'p3' 'T'))
Draw-Polyline @((EdgePoint 'p1' 'B'), [pscustomobject]@{X=1950;Y=930}, (EdgePoint 'p4' 'T'))
Draw-Line 'p2' 'B' 'p4' 'T'
Draw-Line 'p3' 'B' 'p4' 'T'
Draw-Polyline @((EdgePoint 'p4' 'L'), [pscustomobject]@{X=1510;Y=1055}, [pscustomobject]@{X=1510;Y=1415}, (EdgePoint 'n5' 'R')) 'create result'

Draw-Polyline @((EdgePoint 'n9' 'R'), [pscustomobject]@{X=1510;Y=2455}, [pscustomobject]@{X=1510;Y=1695}, (EdgePoint 'c1' 'L')) 'callback parser'
Draw-Line 'c1' 'R' 'c2' 'L'
Draw-Polyline @((EdgePoint 'c2' 'B'), [pscustomobject]@{X=2550;Y=1870}, [pscustomobject]@{X=2250;Y=1870}, (EdgePoint 'c3' 'T'))
Draw-Polyline @((EdgePoint 'c3' 'L'), [pscustomobject]@{X=1510;Y=2005}, [pscustomobject]@{X=1510;Y=2715}, (EdgePoint 'n10' 'R')) 'normalized result'

Draw-Polyline @((EdgePoint 'n12' 'R'), [pscustomobject]@{X=1510;Y=3235}, [pscustomobject]@{X=1510;Y=2745}, (EdgePoint 'w1' 'L')) 'abstract handlers'
Draw-Line 'w1' 'R' 'w2' 'L'
Draw-Polyline @((EdgePoint 'w1' 'B'), [pscustomobject]@{X=1950;Y=2965}, (EdgePoint 'w3' 'T'))
Draw-Polyline @((EdgePoint 'w2' 'B'), [pscustomobject]@{X=2550;Y=2965}, (EdgePoint 'w4' 'T'))
Draw-Polyline @((EdgePoint 'w3' 'B'), [pscustomobject]@{X=1950;Y=3260}, [pscustomobject]@{X=1510;Y=3260}, [pscustomobject]@{X=1510;Y=3495}, (EdgePoint 'n13' 'R'))
Draw-Polyline @((EdgePoint 'w4' 'B'), [pscustomobject]@{X=2550;Y=3260}, [pscustomobject]@{X=1510;Y=3260}, [pscustomobject]@{X=1510;Y=3495}, (EdgePoint 'n13' 'R'))

if ([System.IO.File]::Exists($outputPath)) {
    [System.IO.File]::Delete($outputPath)
}

[PaymentFlowPngWriterV5]::Save($bitmap, $outputPath)

$outputFile = Get-Item -LiteralPath $outputPath
if ($outputFile.Length -le 0) {
    throw "PNG render failed: output file is empty."
}

$graphics.Dispose()
$bitmap.Dispose()
$arrowPen.Dispose()
$titleFont.Dispose()
$subtitleFont.Dispose()
$groupFont.Dispose()
$nodeTitleFont.Dispose()
$nodeFont.Dispose()
$smallFont.Dispose()
$textBrush.Dispose()
$captionBrush.Dispose()

Write-Output $outputPath
