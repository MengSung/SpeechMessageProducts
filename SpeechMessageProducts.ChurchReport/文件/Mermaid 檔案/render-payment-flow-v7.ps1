Add-Type -AssemblyName System.Drawing

if (-not ('PaymentFlowPngWriterV7' -as [type])) {
Add-Type -ReferencedAssemblies 'System.Drawing','System.IO.Compression' -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class PaymentFlowPngWriterV7
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

function T($base64) {
    return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($base64))
}

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$outputPath = [System.IO.Path]::Combine($scriptDir, 'payment-flow-complete-v7.png')
$width = 3600
$height = 3000

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$graphics.Clear([System.Drawing.Color]::White)

$fontFamily = 'Microsoft JhengHei'
$titleFont = New-Object System.Drawing.Font($fontFamily, 40, [System.Drawing.FontStyle]::Bold)
$subtitleFont = New-Object System.Drawing.Font($fontFamily, 20, [System.Drawing.FontStyle]::Regular)
$laneFont = New-Object System.Drawing.Font($fontFamily, 22, [System.Drawing.FontStyle]::Bold)
$sectionFont = New-Object System.Drawing.Font($fontFamily, 24, [System.Drawing.FontStyle]::Bold)
$nodeTitleFont = New-Object System.Drawing.Font($fontFamily, 19, [System.Drawing.FontStyle]::Bold)
$nodeBodyFont = New-Object System.Drawing.Font($fontFamily, 16, [System.Drawing.FontStyle]::Regular)
$smallFont = New-Object System.Drawing.Font($fontFamily, 14, [System.Drawing.FontStyle]::Regular)

$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(17, 24, 39))
$mutedBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))
$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$lineColor = [System.Drawing.Color]::FromArgb(51, 65, 85)
$arrowPen = New-Object System.Drawing.Pen($lineColor, 4)
$arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
$plainPen = New-Object System.Drawing.Pen($lineColor, 4)

$format = New-Object System.Drawing.StringFormat
$format.Trimming = [System.Drawing.StringTrimming]::Word
$format.LineAlignment = [System.Drawing.StringAlignment]::Near
$format.Alignment = [System.Drawing.StringAlignment]::Near

function New-Brush($hex) {
    return New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($hex))
}

function New-Pen($hex, $width = 3) {
    return New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml($hex), $width)
}

function Draw-Rect($x, $y, $w, $h, $fillHex, $strokeHex, $strokeWidth = 3) {
    $fill = New-Brush $fillHex
    $pen = New-Pen $strokeHex $strokeWidth
    $rect = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
    $graphics.FillRectangle($fill, $rect)
    $graphics.DrawRectangle($pen, $rect)
    $fill.Dispose()
    $pen.Dispose()
}

function Draw-Text($text, $font, $brush, $x, $y, $w, $h) {
    $rect = New-Object System.Drawing.RectangleF($x, $y, $w, $h)
    $graphics.DrawString($text, $font, $brush, $rect, $format)
}

function Draw-Lane($x, $y, $w, $h, $title, $fillHex, $strokeHex) {
    Draw-Rect $x $y $w $h $fillHex $strokeHex 3
    Draw-Text $title $laneFont $textBrush ($x + 18) ($y + 14) ($w - 36) 42
}

function Draw-SectionTitle($title, $x, $y, $w, $fillHex, $strokeHex) {
    Draw-Rect $x $y $w 58 $fillHex $strokeHex 3
    Draw-Text $title $sectionFont $textBrush ($x + 18) ($y + 12) ($w - 36) 42
}

function Draw-Node($key, $x, $y, $w, $h, $title, $body, $fillHex, $strokeHex) {
    Draw-Rect $x $y $w $h $fillHex $strokeHex 3
    Draw-Text $title $nodeTitleFont $textBrush ($x + 16) ($y + 12) ($w - 32) 34
    Draw-Text $body $nodeBodyFont $textBrush ($x + 16) ($y + 52) ($w - 32) ($h - 62)
    $script:nodes[$key] = [pscustomobject]@{ X = $x; Y = $y; W = $w; H = $h }
}

function Edge($key, $side) {
    $n = $script:nodes[$key]
    switch ($side) {
        'T' { return [pscustomobject]@{ X = $n.X + ($n.W / 2); Y = $n.Y } }
        'B' { return [pscustomobject]@{ X = $n.X + ($n.W / 2); Y = $n.Y + $n.H } }
        'L' { return [pscustomobject]@{ X = $n.X; Y = $n.Y + ($n.H / 2) } }
        'R' { return [pscustomobject]@{ X = $n.X + $n.W; Y = $n.Y + ($n.H / 2) } }
    }
}

function Draw-Label($label, $x, $y, $w = 260) {
    if ([string]::IsNullOrWhiteSpace($label)) {
        return
    }

    $rect = New-Object System.Drawing.RectangleF($x, $y, $w, 32)
    $graphics.FillRectangle($whiteBrush, $rect)
    $labelPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(226, 232, 240), 1)
    $graphics.DrawRectangle($labelPen, [int]$x, [int]$y, [int]$w, 32)
    $graphics.DrawString($label, $smallFont, $mutedBrush, $rect, $format)
    $labelPen.Dispose()
}

function Draw-Polyline($points, $label = '', $labelX = $null, $labelY = $null, $labelW = 260) {
    for ($i = 0; $i -lt $points.Count - 2; $i++) {
        $graphics.DrawLine($plainPen, [int]$points[$i].X, [int]$points[$i].Y, [int]$points[$i + 1].X, [int]$points[$i + 1].Y)
    }
    $graphics.DrawLine($arrowPen, [int]$points[$points.Count - 2].X, [int]$points[$points.Count - 2].Y, [int]$points[$points.Count - 1].X, [int]$points[$points.Count - 1].Y)

    if (-not [string]::IsNullOrWhiteSpace($label)) {
        if ($null -eq $labelX -or $null -eq $labelY) {
            $mid = $points[[Math]::Floor($points.Count / 2)]
            Draw-Label $label ([int]$mid.X + 10) ([int]$mid.Y - 38) $labelW
        } else {
            Draw-Label $label $labelX $labelY $labelW
        }
    }
}

function Draw-Arrow($fromKey, $fromSide, $toKey, $toSide, $label = '', $labelX = $null, $labelY = $null, $labelW = 260) {
    $from = Edge $fromKey $fromSide
    $to = Edge $toKey $toSide
    Draw-Polyline @($from, $to) $label $labelX $labelY $labelW
}

$script:nodes = @{}

$title = T '5a6M5pW06YeR5rWB5ZG85Y+r5rWB56iLIFY3IC0g5riF5pmw5rOz6YGT54mI'
$subtitle = T '5LiK5Y2K6YOo5piv5bu656uL5LuY5qy+6IiH5bCO5ZCR56ys5LiJ5pa577yb5Lit5q615piv56ys5LiJ5pa55Zue5ZG86IiH5qC45b+D6Kej5p6Q77yb5LiL5Y2K6YOo5piv55Si5ZOB6Ieq5bex55qEIENSTSDmm7TmlrDjgIFMSU5FIOmAmuefpeiIh+e1kOaenOmggeOAgg=='

Draw-Text $title $titleFont $textBrush 900 30 1800 60
Draw-Text $subtitle $subtitleFont $mutedBrush 360 92 2880 60

$laneTop = 190
$laneHeight = 2260
$laneGap = 35
$lane1 = @{ X = 90; W = 530 }
$lane2 = @{ X = 655; W = 650 }
$lane3 = @{ X = 1340; W = 600 }
$lane4 = @{ X = 1975; W = 690 }
$lane5 = @{ X = 2700; W = 810 }

Draw-Lane $lane1.X $laneTop $lane1.W $laneHeight (T '5aWJ54276ICFIC8g5LuY5qy+6ICF') '#fff7ed' '#c2410c'
Draw-Lane $lane2.X $laneTop $lane2.W $laneHeight (T 'Q2h1cmNoUmVwb3J0IOeUouWTgeWxpA==') '#fff7ed' '#c2410c'
Draw-Lane $lane3.X $laneTop $lane3.W $laneHeight (T 'QVNQLk5FVCBIb3N0IEFkYXB0ZXI=') '#ecfeff' '#0891b2'
Draw-Lane $lane4.X $laneTop $lane4.W $laneHeight (T 'U3BlZWNoTWVzc2FnZS5QYXltZW50cyDmoLjlv4M=') '#eef2ff' '#4f46e5'
Draw-Lane $lane5.X $laneTop $lane5.W $laneHeight (T '56ys5LiJ5pa56YeR5rWB') '#f0fdf4' '#16a34a'

Draw-SectionTitle (T '5bu656uL5LuY5qy+6IiH5bCO5ZCR56ys5LiJ5pa5') 90 275 3420 '#fef3c7' '#d97706'
Draw-SectionTitle (T '5LuY5qy+5a6M5oiQ6IiH5Zue5ZG86Kej5p6Q') 90 1225 3420 '#cffafe' '#0891b2'
Draw-SectionTitle (T '5LuY5qy+5b6M55Si5ZOB5bel5L2c5rWB') 90 1905 3420 '#dcfce7' '#16a34a'

Draw-Node 'u1' 125 360 460 145 (T 'MS4g5aGr5a+r5aWJ54276LOH5paZ') (T '5aeT5ZCN44CB5aWJ54276aCF55uu44CB6YeR6aGN44CB5LuY5qy+5pa55byP44CC6YCZ5Lqb5LuN5pivIENodXJjaFJlcG9ydCDnmoTnlKLlk4Hos4fmlpnjgII=') '#fffbeb' '#c2410c'
Draw-Node 'cr1' 700 360 560 145 (T 'Mi4gQ29udHJvbGxlciDmjqXmlLboq4vmsYI=') (T '5L+d55WZ5pei5pyJ6aCB6Z2i6IiH6Lev55Sx77yM6LKg6LKs6amX6K2J55Wr6Z2i6Ly45YWl5Lim5ZWf5YuV5LuY5qy+5rWB56iL44CC') '#fffbeb' '#c2410c'
Draw-Node 'cr2' 700 575 560 165 (T 'My4g6YG45pOHIFBheW1lbnQgUHJvZmlsZQ==') (T '5L6dIGFwcHNldHRpbmdzLmpzb24g55qEIFBBWV9QUk9WSURFUiDlsI3mh4nliLDlj6/ph43nlKjph5HmtYHoqK3lrprmqpTjgII=') '#fffbeb' '#c2410c'
Draw-Node 'host1' 1380 575 520 165 (T 'NC4g5bu656uL5Lit56uL5LuY5qy+6KuL5rGC') (T '5oqK55Si5ZOB6LOH5paZ6L2J5oiQIFBheW1lbnRDcmVhdGVSZXF1ZXN077yM5LiN5pq06ZyyIFFQYXkg5bCI5bGsIERUT+OAgg==') '#cffafe' '#0891b2'
Draw-Node 'core1' 2020 575 600 165 (T 'NS4g5ZG85Y+r6YeR5rWB5qC45b+D') (T 'SVBheW1lbnRHYXRld2F5IOS+nSBwcm9maWxlIOi3r+eUseWIsCBTaW5vcGFjL1FQYXnjgIFNeVBheSDmiJYgVGFpc2hpbi9UU1BH44CC') '#e0e7ff' '#4f46e5'
Draw-Node 'provider1' 2750 575 710 165 (T 'UHJvdmlkZXIg5Y2U5a6a57Sw56+A') (T 'U2lub3BhYy9RUGF544CBTXlQYXnjgIFUYWlzaGluL1RTUEcg55qE57C956ug44CB5Yqg5a+G44CB56uv6bue6IiH5qyE5L2N5qC85byP6YO95bCB6KOd5Zyo5qC45b+D5YWn44CC') '#dcfce7' '#16a34a'
Draw-Node 'cr3' 700 850 560 145 (T 'Ni4g5bCO5ZCR56ys5LiJ5pa55LuY5qy+6aCB') (T '5qC45b+D5Zue5YKzIFBheW1lbnRQYWdlVXJs77ybQ2h1cmNoUmVwb3J0IOWPquiyoOiyrCByZWRpcmVjdO+8jOS4jeiZleeQhuewveeroOiIh+WKoOWvhue0sOevgOOAgg==') '#fffbeb' '#c2410c'
Draw-Node 'u2' 125 1045 460 145 (T 'Ny4g5L2/55So6ICF5Zyo6YeR5rWB6aCB5LuY5qy+') (T '5L+h55So5Y2h6Ly45YWl44CBQVRNL+WMr+asvuaIluesrOS4ieaWuemggemdoua1geeoi+mDveeZvOeUn+WcqOmHkea1geerr+OAgg==') '#fffbeb' '#c2410c'
Draw-Node 'provider2' 2750 1045 710 145 (T 'Ny4g5L2/55So6ICF5Zyo6YeR5rWB6aCB5LuY5qy+') (T '5L+h55So5Y2h6Ly45YWl44CBQVRNL+WMr+asvuaIluesrOS4ieaWuemggemdoua1geeoi+mDveeZvOeUn+WcqOmHkea1geerr+OAgg==') '#dcfce7' '#16a34a'

Draw-Node 'provider3' 2750 1325 710 145 (T 'OC4g6YeR5rWB5ZG85Y+r5pei5pyJIGNhbGxiYWNrIHJvdXRl') (T 'UVBheeOAgU15UGF544CBVFNQRyDljp/mnInlm57lkbzntrLlnYDkv53nlZnvvIzpgb/lhY3noLTlo57ml6LmnInoqK3lrprjgII=') '#dcfce7' '#16a34a'
Draw-Node 'cr4' 700 1325 560 145 (T 'OC4g6YeR5rWB5ZG85Y+r5pei5pyJIGNhbGxiYWNrIHJvdXRl') (T 'UVBheeOAgU15UGF544CBVFNQRyDljp/mnInlm57lkbzntrLlnYDkv53nlZnvvIzpgb/lhY3noLTlo57ml6LmnInoqK3lrprjgII=') '#fffbeb' '#c2410c'
Draw-Node 'host2' 1380 1545 520 165 (T 'OS4g5pig5bCEIEhUVFAg5Zue5ZG8') (T 'UGF5bWVudEh0dHBSZXF1ZXN0TWFwcGVyIOiugOWPliBxdWVyeeOAgWZvcm3jgIFoZWFkZXLjgIFib2R577yM6L2J5oiQIFBheW1lbnRDYWxsYmFja1JlcXVlc3TjgII=') '#cffafe' '#0891b2'
Draw-Node 'core2' 2020 1545 600 165 (T 'MTAuIOaguOW/g+mpl+itieiIh+ino+aekA==') (T 'UGFyc2VDYWxsYmFja0FzeW5jIOmpl+ewveOAgeino+WvhuOAgeino+aekOeLgOaFi++8jOS4pueUoueUnyBwcm92aWRlci1uZXV0cmFsIOe1kOaenOOAgg==') '#e0e7ff' '#4f46e5'
Draw-Node 'host3' 1380 1755 520 145 (T 'MTEuIOi9ieaIkCBBQ0sg5Zue5oeJ') (T 'UGF5bWVudEFja25vd2xlZGdlbWVudFJlc3VsdE1hcHBlciDovYnmiJDmloflrZfjgIFKU09OIOaIliBSZWRpcmVjdO+8jOWbnue1puesrOS4ieaWuemHkea1geOAgg==') '#cffafe' '#0891b2'
Draw-Node 'provider4' 2750 1755 710 145 (T 'MTIuIOWbnuimhuesrOS4ieaWuemHkea1gQ==') (T '6YCZ5LiA5q2l5Y+q56K66KqN5pS25Yiw5Zue5ZG877yM5LiN5Luj6KGo55Si5ZOB6LOH5paZ5bey55Sx5qC45b+D6Ieq6KGM5a+r5YWl44CC') '#dcfce7' '#16a34a'

Draw-Node 'cr5' 700 2005 560 145 (T 'MTMuIOWVn+WLleeUouWTgeW3peS9nOa1gQ==') (T 'Q2h1cmNoUmVwb3J0IOagueaTmiBQYXltZW50Q2FsbGJhY2tSZXN1bHQg5Z+36KGM6Ieq5bex55qE5LuY6LK75Zau6IiH5aWJ54276YKP6Lyv44CC') '#fffbeb' '#c2410c'
Draw-Node 'cr6' 690 2220 290 155 (T 'MTQuIOabtOaWsCBDUk0gLyDku5josrvllq4=') (T '6YCP6YGO55Si5ZOB5a+m5L2c55qEIElQYXltZW50UmVjb3JkVXBkYXRlciDmm7TmlrAgQ1JN44CB5aWJ54275oiW5biz5Zau54uA5oWL44CC') '#ffedd5' '#c2410c'
Draw-Node 'cr7' 1010 2220 290 155 (T 'MTUuIOmAmuefpeS7mOasvuiAhQ==') (T '6YCP6YGO55Si5ZOB5a+m5L2c55qEIElQYXltZW50UGF5ZXJOb3RpZmllciDnmbzpgIEgTElORe+8m+acquS+hueUouWTgeWPr+aUueeUqCBFbWFpbCDmiJYgU01T44CC') '#ffedd5' '#c2410c'
Draw-Node 'u3' 125 2220 460 155 (T 'MTYuIOmhr+ekuue1kOaenOmggSAvIOW+jOe6jOa1geeoiw==') (T 'Q2h1cmNoUmVwb3J0IOaxuuWumuaIkOWKn+OAgeWkseaVl+OAgeW+heS7mOasvuaIluS4i+S4gOatpeeVq+mdouOAgg==') '#fffbeb' '#c2410c'

Draw-Arrow 'u1' 'R' 'cr1' 'L' '' 0 0
Draw-Polyline @((Edge 'cr1' 'B'), [pscustomobject]@{X=980;Y=545}, (Edge 'cr2' 'T')) '' 0 0
Draw-Arrow 'cr2' 'R' 'host1' 'L' 'neutral request' 1285 635 190
Draw-Arrow 'host1' 'R' 'core1' 'L' 'gateway call' 1908 635 160
Draw-Arrow 'core1' 'R' 'provider1' 'L' 'provider protocol' 2630 635 220
Draw-Polyline @((Edge 'core1' 'B'), [pscustomobject]@{X=2320;Y=812}, [pscustomobject]@{X=1265;Y=812}, (Edge 'cr3' 'R')) 'PaymentCreateResult' 1525 770 260
Draw-Polyline @((Edge 'cr3' 'L'), [pscustomobject]@{X=620;Y=922}, [pscustomobject]@{X=620;Y=1118}, (Edge 'u2' 'R')) 'redirect browser' 430 980 210
Draw-Arrow 'u2' 'R' 'provider2' 'L' 'provider-hosted payment' 1380 1080 300
Draw-Polyline @((Edge 'provider2' 'B'), [pscustomobject]@{X=3105;Y=1240}, (Edge 'provider3' 'T')) '' 0 0
Draw-Arrow 'provider3' 'L' 'cr4' 'R' 'callback' 1815 1365 150
Draw-Polyline @((Edge 'cr4' 'R'), [pscustomobject]@{X=1320;Y=1398}, [pscustomobject]@{X=1320;Y=1628}, (Edge 'host2' 'L')) '' 0 0
Draw-Arrow 'host2' 'R' 'core2' 'L' 'parse callback' 1900 1605 190
Draw-Polyline @((Edge 'core2' 'L'), [pscustomobject]@{X=1950;Y=1818}, (Edge 'host3' 'R')) 'ack descriptor' 1715 1780 190
Draw-Arrow 'host3' 'R' 'provider4' 'L' 'ACK response' 1950 1795 190
Draw-Polyline @((Edge 'core2' 'B'), [pscustomobject]@{X=2320;Y=1958}, [pscustomobject]@{X=1265;Y=1958}, (Edge 'cr5' 'R')) 'normalized callback result' 1460 1918 330
Draw-Polyline @((Edge 'cr5' 'B'), [pscustomobject]@{X=845;Y=2188}, (Edge 'cr6' 'T')) '' 0 0
Draw-Polyline @((Edge 'cr5' 'B'), [pscustomobject]@{X=1155;Y=2188}, (Edge 'cr7' 'T')) '' 0 0
Draw-Arrow 'cr5' 'L' 'u3' 'R' 'result page' 525 2065 150

$summaryTop = 2530
Draw-Rect 90 $summaryTop 3420 360 '#f8fafc' '#64748b' 3
Draw-Text (T '6YKK55WM6LKs5Lu75pGY6KaB') $sectionFont $textBrush 125 ($summaryTop + 20) 800 42
Draw-Node 'b1' 150 ($summaryTop + 95) 980 150 (T '57SU6YeR5rWB5qC45b+D') (T '5LiN5L6d6LO0IEFTUC5ORVQgQ29udHJvbGxlcuOAgUNSTeOAgUxJTkXjgIFEQuOAgVZpZXcg5oiWIENodXJjaFJlcG9ydOOAguWPquiZleeQhiBwcm92aWRlciBwcm90b2NvbOOAgeewveeroOOAgeWKoOWvhuOAgeeLgOaFi+ato+imj+WMluOAgg==') '#eef2ff' '#4f46e5'
Draw-Node 'b2' 1310 ($summaryTop + 95) 980 150 (T 'QVNQLk5FVCDlhbHnlKjmqYvmjqXlsaQ=') (T '5Y+q6JmV55CGIEh0dHBSZXF1ZXN0IOWwjeaHieiIhyBBQ0sg6L2JIElBY3Rpb25SZXN1bHTjgILlj6/ntablhbbku5YgQVNQLk5FVCBDb3JlIOeUouWTgemHjeeUqOOAgg==') '#ecfeff' '#0891b2'
Draw-Node 'b3' 2470 ($summaryTop + 95) 980 150 (T '55Si5ZOB5bGk') (T 'Q2h1cmNoUmVwb3J0IOiIh+acquS+hueUouWTgeWQhOiHquiyoOiyrOizh+aWmeW6q+OAgUNSTeOAgUxJTkUv6YCa55+l44CB6aCB6Z2i44CB5Y676YeN6IiH5ZWG5qWt5rWB56iL44CC') '#fff7ed' '#c2410c'
Draw-Text (T 'TGluZVBheSDku43mmK/njajnq4vlsIjmoYjvvIzmnKrntI3lhaXmnKzmrKEgU2lub3BhYy9NeVBheS9UU1BHIOaguOW/g+aKvembouevhOWcjeOAgg==') $smallFont $mutedBrush 150 ($summaryTop + 270) 1700 40

if ([System.IO.File]::Exists($outputPath)) {
    [System.IO.File]::Delete($outputPath)
}

[PaymentFlowPngWriterV7]::Save($bitmap, $outputPath)

$outputFile = Get-Item -LiteralPath $outputPath
if ($outputFile.Length -le 0) {
    throw "PNG render failed: output file is empty."
}

$graphics.Dispose()
$bitmap.Dispose()
$arrowPen.Dispose()
$plainPen.Dispose()
$titleFont.Dispose()
$subtitleFont.Dispose()
$laneFont.Dispose()
$sectionFont.Dispose()
$nodeTitleFont.Dispose()
$nodeBodyFont.Dispose()
$smallFont.Dispose()
$textBrush.Dispose()
$mutedBrush.Dispose()
$whiteBrush.Dispose()
$format.Dispose()

Write-Output $outputPath
