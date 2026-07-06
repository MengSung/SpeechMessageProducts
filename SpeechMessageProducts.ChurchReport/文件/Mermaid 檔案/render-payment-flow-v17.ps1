Add-Type -AssemblyName System.Drawing

if (-not ('PaymentFlowPngWriterV15' -as [type])) {
Add-Type -ReferencedAssemblies 'System.Drawing','System.IO.Compression' -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class PaymentFlowPngWriterV15
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

$mermaidPath = [System.IO.Path]::Combine($scriptDir, 'payment-flow-complete-v17-learning.mmd')
$outputPath = [System.IO.Path]::Combine($scriptDir, 'payment-flow-complete-v17-learning.png')

function Decode-Label([string] $text) {
    $decoded = $text.Replace('<br/>', "`n").Replace('<br>', "`n")
    return [System.Net.WebUtility]::HtmlDecode($decoded)
}

function Read-Labels($path) {
    $result = @{}
    $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    foreach ($line in $lines) {
        if ($line -match '^\s*([A-Z][0-9]+)\["(.+)"\]\s*$') {
            $result[$matches[1]] = Decode-Label $matches[2]
        }
    }
    return $result
}

function New-Brush($hex) {
    return New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($hex))
}

function New-Pen($hex, $width = 3) {
    return New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml($hex), $width)
}

function Draw-Group($x, $y, $w, $h, $title, $fillHex, $strokeHex) {
    $fill = New-Brush $fillHex
    $pen = New-Pen $strokeHex 3
    $rect = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
    $script:g.FillRectangle($fill, $rect)
    $script:g.DrawRectangle($pen, $rect)
    $script:g.DrawString($title, $script:groupFont, $script:captionBrush, ($x + 22), ($y + 18))
    $fill.Dispose()
    $pen.Dispose()
}

function Draw-Node($id, $x, $y, $w, $h, $fillHex, $strokeHex) {
    $label = $script:labels[$id]
    if ([string]::IsNullOrWhiteSpace($label)) {
        throw "Missing node label: $id"
    }

    $fill = New-Brush $fillHex
    $pen = New-Pen $strokeHex 3
    $rect = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
    $script:g.FillRectangle($fill, $rect)
    $script:g.DrawRectangle($pen, $rect)

    $lines = $label -split "`n"
    $title = $lines[0]
    $body = ''
    if ($lines.Count -gt 1) {
        $body = $lines[1..($lines.Count - 1)] -join "`n"
    }

    $titleRect = New-Object System.Drawing.RectangleF(($x + 18), ($y + 14), ($w - 36), 38)
    $bodyRect = New-Object System.Drawing.RectangleF(($x + 18), ($y + 56), ($w - 36), ($h - 66))
    $script:g.DrawString($title, $script:nodeTitleFont, $script:textBrush, $titleRect, $script:stringFormat)
    if (-not [string]::IsNullOrWhiteSpace($body)) {
        $script:g.DrawString($body, $script:nodeFont, $script:textBrush, $bodyRect, $script:stringFormat)
    }

    $script:nodes[$id] = [pscustomobject]@{ X = $x; Y = $y; W = $w; H = $h }
    $fill.Dispose()
    $pen.Dispose()
}

function EdgePoint($id, $side) {
    $n = $script:nodes[$id]
    switch ($side) {
        'T' { return [pscustomobject]@{ X = $n.X + ($n.W / 2); Y = $n.Y } }
        'B' { return [pscustomobject]@{ X = $n.X + ($n.W / 2); Y = $n.Y + $n.H } }
        'L' { return [pscustomobject]@{ X = $n.X; Y = $n.Y + ($n.H / 2) } }
        'R' { return [pscustomobject]@{ X = $n.X + $n.W; Y = $n.Y + ($n.H / 2) } }
    }
}

function Draw-ArrowPath($points) {
    for ($i = 0; $i -lt $points.Count - 2; $i++) {
        $plain = New-Object System.Drawing.Pen($script:lineColor, 4)
        $script:g.DrawLine($plain, [int]$points[$i].X, [int]$points[$i].Y, [int]$points[$i + 1].X, [int]$points[$i + 1].Y)
        $plain.Dispose()
    }
    $script:g.DrawLine($script:arrowPen, [int]$points[$points.Count - 2].X, [int]$points[$points.Count - 2].Y, [int]$points[$points.Count - 1].X, [int]$points[$points.Count - 1].Y)
}

function Draw-Arrow($fromId, $fromSide, $toId, $toSide) {
    $from = EdgePoint $fromId $fromSide
    $to = EdgePoint $toId $toSide
    if ([Math]::Abs($from.X - $to.X) -lt 4 -or [Math]::Abs($from.Y - $to.Y) -lt 4) {
        Draw-ArrowPath @($from, $to)
        return
    }

    $midY = [int](($from.Y + $to.Y) / 2)
    Draw-ArrowPath @($from, [pscustomobject]@{ X = $from.X; Y = $midY }, [pscustomobject]@{ X = $to.X; Y = $midY }, $to)
}

function Draw-ArrowVia($fromId, $fromSide, $toId, $toSide, $via) {
    $points = @((EdgePoint $fromId $fromSide))
    foreach ($point in $via) {
        $points += $point
    }
    $points += (EdgePoint $toId $toSide)
    Draw-ArrowPath $points
}

$script:labels = Read-Labels $mermaidPath
$script:nodes = @{}

$width = 4200
$height = 6600
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$script:g = [System.Drawing.Graphics]::FromImage($bitmap)
$script:g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$script:g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$script:g.Clear([System.Drawing.Color]::White)

$fontFamily = 'Microsoft JhengHei'
$titleFont = New-Object System.Drawing.Font($fontFamily, 38, [System.Drawing.FontStyle]::Bold)
$subtitleFont = New-Object System.Drawing.Font($fontFamily, 17, [System.Drawing.FontStyle]::Regular)
$script:groupFont = New-Object System.Drawing.Font($fontFamily, 22, [System.Drawing.FontStyle]::Bold)
$script:nodeTitleFont = New-Object System.Drawing.Font($fontFamily, 19, [System.Drawing.FontStyle]::Bold)
$script:nodeFont = New-Object System.Drawing.Font($fontFamily, 15, [System.Drawing.FontStyle]::Regular)
$script:textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(17, 24, 39))
$script:captionBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))
$script:lineColor = [System.Drawing.Color]::FromArgb(51, 65, 85)
$script:arrowPen = New-Object System.Drawing.Pen($script:lineColor, 4)
$script:arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
$script:stringFormat = New-Object System.Drawing.StringFormat
$script:stringFormat.Trimming = [System.Drawing.StringTrimming]::EllipsisWord
$script:stringFormat.FormatFlags = 0

$script:g.DrawString('V17 Complete Payment Call Flow - Learning Layout', $titleFont, $script:textBrush, 920, 34)
$script:g.DrawString('Fixed-lane layout: product host, ASP.NET adapter, reusable core, providers, and product post-payment workflow. Cross-lane lines use gutters so they do not cover text.', $subtitleFont, $script:captionBrush, 500, 94)

Draw-Group 80 180 930 5740 'ChurchReport product host' '#fff7ed' '#c2410c'
Draw-Group 1065 180 930 5740 'ASP.NET host adapters' '#ecfeff' '#0891b2'
Draw-Group 2050 180 930 5740 'SpeechMessage.Payments core' '#eef2ff' '#4f46e5'
Draw-Group 3035 180 930 5740 'Provider implementations' '#f0fdf4' '#16a34a'
Draw-Group 80 5980 3885 420 'Reusable boundary summary' '#f8fafc' '#64748b'

$productFill = '#fff7ed'
$productStroke = '#c2410c'
$adapterFill = '#ecfeff'
$adapterStroke = '#0891b2'
$coreFill = '#eef2ff'
$coreStroke = '#4f46e5'
$providerFill = '#f0fdf4'
$providerStroke = '#16a34a'
$summaryFill = '#f8fafc'
$summaryStroke = '#64748b'

$w = 820
$h = 148
$xP = 135
$xA = 1120
$xC = 2105
$xD = 3090

Draw-Node 'A1' $xP 300 $w $h $productFill $productStroke
Draw-Node 'A2' $xP 520 $w $h $productFill $productStroke
Draw-Node 'A3' $xP 740 $w $h $productFill $productStroke
Draw-Node 'A4' $xP 960 $w 164 $productFill $productStroke
Draw-Node 'A5' $xP 1200 $w 164 $productFill $productStroke
Draw-Node 'A6' $xP 1440 $w $h $productFill $productStroke
Draw-Node 'B1' $xA 1440 $w $h $adapterFill $adapterStroke
Draw-Node 'C1' $xC 1440 $w $h $coreFill $coreStroke
Draw-Node 'C2' $xC 1660 $w $h $coreFill $coreStroke
Draw-Node 'D1' $xD 1660 $w 164 $providerFill $providerStroke
Draw-Node 'D2' $xD 1900 $w $h $providerFill $providerStroke
Draw-Node 'D3' $xD 2120 $w 164 $providerFill $providerStroke
Draw-Node 'D4' $xD 2360 $w $h $providerFill $providerStroke
Draw-Node 'D5' $xD 2580 $w 164 $providerFill $providerStroke
Draw-Node 'D6' $xD 2820 $w $h $providerFill $providerStroke
Draw-Node 'D7' $xD 3040 $w 164 $providerFill $providerStroke
Draw-Node 'C3' $xC 3040 $w $h $coreFill $coreStroke
Draw-Node 'A7' $xP 3040 $w $h $productFill $productStroke
Draw-Node 'A8' $xP 3260 $w 164 $productFill $productStroke
Draw-Node 'A9' $xD 3260 $w $h $providerFill $providerStroke
Draw-Node 'A10' $xP 3500 $w 184 $productFill $productStroke
Draw-Node 'B2' $xA 3500 $w $h $adapterFill $adapterStroke
Draw-Node 'C4' $xC 3500 $w $h $coreFill $coreStroke
Draw-Node 'D8' $xD 3500 $w $h $providerFill $providerStroke
Draw-Node 'D9' $xD 3720 $w $h $providerFill $providerStroke
Draw-Node 'C5' $xC 3720 $w 164 $coreFill $coreStroke
Draw-Node 'B3' $xA 3940 $w $h $adapterFill $adapterStroke
Draw-Node 'C6' $xC 3940 $w $h $coreFill $coreStroke
Draw-Node 'E1' $xP 4160 $w $h $productFill $productStroke
Draw-Node 'E2' $xP 4380 $w $h $productFill $productStroke
Draw-Node 'C7' $xC 4380 $w $h $coreFill $coreStroke
Draw-Node 'E3' $xP 4660 $w $h $productFill $productStroke
Draw-Node 'E4' $xP 4880 $w $h $productFill $productStroke
Draw-Node 'E5' $xP 5180 $w $h $productFill $productStroke
Draw-Node 'E6' $xP 5400 $w $h $productFill $productStroke
Draw-Node 'E7' $xP 5620 $w $h $summaryFill $summaryStroke
Draw-Node 'F1' 180 6120 1120 160 $coreFill $coreStroke
Draw-Node 'F2' 1540 6120 1120 160 $adapterFill $adapterStroke
Draw-Node 'F3' 2900 6120 900 160 $productFill $productStroke

Draw-Arrow 'A1' 'B' 'A2' 'T'
Draw-Arrow 'A2' 'B' 'A3' 'T'
Draw-Arrow 'A3' 'B' 'A4' 'T'
Draw-Arrow 'A4' 'B' 'A5' 'T'
Draw-Arrow 'A5' 'B' 'A6' 'T'
Draw-Arrow 'A6' 'R' 'B1' 'L'
Draw-Arrow 'B1' 'R' 'C1' 'L'
Draw-Arrow 'C1' 'B' 'C2' 'T'
Draw-Arrow 'C2' 'R' 'D1' 'L'
Draw-Arrow 'D1' 'B' 'D2' 'T'
Draw-Arrow 'D2' 'B' 'D3' 'T'
Draw-ArrowVia 'D1' 'B' 'D4' 'T' @([pscustomobject]@{ X = 3500; Y = 1855 }, [pscustomobject]@{ X = 3500; Y = 2310 })
Draw-Arrow 'D4' 'B' 'D5' 'T'
Draw-ArrowVia 'D1' 'B' 'D6' 'T' @([pscustomobject]@{ X = 3500; Y = 1855 }, [pscustomobject]@{ X = 3500; Y = 2770 })
Draw-Arrow 'D6' 'B' 'D7' 'T'
Draw-ArrowVia 'D3' 'L' 'C3' 'R' @([pscustomobject]@{ X = 3010; Y = 2202 }, [pscustomobject]@{ X = 3010; Y = 3114 })
Draw-ArrowVia 'D5' 'L' 'C3' 'R' @([pscustomobject]@{ X = 2990; Y = 2662 }, [pscustomobject]@{ X = 2990; Y = 3114 })
Draw-Arrow 'D7' 'L' 'C3' 'R'
Draw-Arrow 'C3' 'L' 'A7' 'R'
Draw-Arrow 'A7' 'B' 'A8' 'T'
Draw-Arrow 'A8' 'R' 'A9' 'L'
Draw-ArrowVia 'A9' 'B' 'A10' 'T' @([pscustomobject]@{ X = 3500; Y = 3460 }, [pscustomobject]@{ X = 545; Y = 3460 })
Draw-Arrow 'A10' 'R' 'B2' 'L'
Draw-Arrow 'B2' 'R' 'C4' 'L'
Draw-Arrow 'C4' 'R' 'D8' 'L'
Draw-Arrow 'D8' 'B' 'D9' 'T'
Draw-Arrow 'D9' 'L' 'C5' 'R'
Draw-Arrow 'C5' 'L' 'B3' 'R'
Draw-Arrow 'C5' 'B' 'C6' 'T'
Draw-Arrow 'C6' 'L' 'E1' 'R'
Draw-Arrow 'E1' 'B' 'E2' 'T'
Draw-Arrow 'E2' 'R' 'C7' 'L'
Draw-Arrow 'C7' 'L' 'E3' 'R'
Draw-Arrow 'E3' 'B' 'E4' 'T'
Draw-ArrowVia 'C7' 'B' 'E5' 'R' @([pscustomobject]@{ X = 2515; Y = 5100 }, [pscustomobject]@{ X = 980; Y = 5100 })
Draw-Arrow 'E5' 'B' 'E6' 'T'
Draw-Arrow 'E4' 'B' 'E7' 'T'
Draw-Arrow 'E6' 'B' 'E7' 'T'

if ([System.IO.File]::Exists($outputPath)) {
    [System.IO.File]::Delete($outputPath)
}

[PaymentFlowPngWriterV15]::Save($bitmap, $outputPath)
$outputFile = Get-Item -LiteralPath $outputPath
if ($outputFile.Length -le 0) {
    throw "PNG render failed: output file is empty."
}

$script:g.Dispose()
$bitmap.Dispose()
$titleFont.Dispose()
$subtitleFont.Dispose()
$script:groupFont.Dispose()
$script:nodeTitleFont.Dispose()
$script:nodeFont.Dispose()
$script:textBrush.Dispose()
$script:captionBrush.Dispose()
$script:arrowPen.Dispose()
$script:stringFormat.Dispose()

Write-Output $outputPath
