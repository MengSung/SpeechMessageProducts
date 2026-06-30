Add-Type -AssemblyName System.Drawing

if (-not ('PaymentFlowPngWriterV9' -as [type])) {
Add-Type -ReferencedAssemblies 'System.Drawing','System.IO.Compression' -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class PaymentFlowPngWriterV9
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

$outputPath = [System.IO.Path]::Combine($scriptDir, 'payment-flow-complete-v9.png')
$width = 2900
$height = 5200

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$graphics.Clear([System.Drawing.Color]::White)

$fontFamily = 'Microsoft JhengHei'
$titleFont = New-Object System.Drawing.Font($fontFamily, 40, [System.Drawing.FontStyle]::Bold)
$subtitleFont = New-Object System.Drawing.Font($fontFamily, 20, [System.Drawing.FontStyle]::Regular)
$sectionFont = New-Object System.Drawing.Font($fontFamily, 24, [System.Drawing.FontStyle]::Bold)
$nodeTitleFont = New-Object System.Drawing.Font($fontFamily, 22, [System.Drawing.FontStyle]::Bold)
$nodeBodyFont = New-Object System.Drawing.Font($fontFamily, 18, [System.Drawing.FontStyle]::Regular)
$tagFont = New-Object System.Drawing.Font($fontFamily, 17, [System.Drawing.FontStyle]::Bold)
$smallFont = New-Object System.Drawing.Font($fontFamily, 15, [System.Drawing.FontStyle]::Regular)

$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(17, 24, 39))
$mutedBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))
$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$lineColor = [System.Drawing.Color]::FromArgb(51, 65, 85)
$arrowPen = New-Object System.Drawing.Pen($lineColor, 4)
$arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor

$format = New-Object System.Drawing.StringFormat
$format.Trimming = [System.Drawing.StringTrimming]::Word
$format.LineAlignment = [System.Drawing.StringAlignment]::Near
$format.Alignment = [System.Drawing.StringAlignment]::Near

$centerFormat = New-Object System.Drawing.StringFormat
$centerFormat.Alignment = [System.Drawing.StringAlignment]::Center
$centerFormat.LineAlignment = [System.Drawing.StringAlignment]::Center

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

function Draw-Text($text, $font, $brush, $x, $y, $w, $h, $fmt = $format) {
    $rect = New-Object System.Drawing.RectangleF($x, $y, $w, $h)
    $graphics.DrawString($text, $font, $brush, $rect, $fmt)
}

function Draw-Section($title, $y, $fillHex, $strokeHex) {
    Draw-Rect 90 $y 2420 62 $fillHex $strokeHex 3
    Draw-Text $title $sectionFont $textBrush 118 ($y + 14) 2360 40
}

function Draw-LegendItem($x, $y, $label, $fillHex, $strokeHex) {
    Draw-Rect $x $y 350 54 $fillHex $strokeHex 3
    Draw-Text $label $smallFont $textBrush ($x + 14) ($y + 14) 322 30
}

function Draw-Step($key, $y, $tag, $title, $body, $fillHex, $strokeHex, $rightNote = '') {
    $script:steps[$key] = [pscustomobject]@{ Y = $y; X = 650; W = 1350; H = 150 }

    Draw-Rect 130 ($y + 28) 430 92 $fillHex $strokeHex 3
    Draw-Text $tag $tagFont $textBrush 148 ($y + 44) 394 60 $centerFormat

    Draw-Rect 650 $y 1350 150 $fillHex $strokeHex 3
    Draw-Text $title $nodeTitleFont $textBrush 676 ($y + 18) 1298 36
    Draw-Text $body $nodeBodyFont $textBrush 676 ($y + 62) 1298 76

    if (-not [string]::IsNullOrWhiteSpace($rightNote)) {
        Draw-Rect 2110 ($y + 10) 620 130 '#f8fafc' '#cbd5e1' 2
        Draw-Text $rightNote $smallFont $mutedBrush 2130 ($y + 24) 580 96
    }
}

function Draw-Down($fromKey, $toKey) {
    $from = $script:steps[$fromKey]
    $to = $script:steps[$toKey]
    $x = [int]($from.X + ($from.W / 2))
    $graphics.DrawLine($arrowPen, $x, [int]($from.Y + $from.H), $x, [int]$to.Y)
}

function Draw-BoundaryCard($x, $y, $title, $body, $fillHex, $strokeHex) {
    Draw-Rect $x $y 720 170 $fillHex $strokeHex 3
    Draw-Text $title $nodeTitleFont $textBrush ($x + 22) ($y + 18) 676 38
    Draw-Text $body $nodeBodyFont $textBrush ($x + 22) ($y + 66) 676 86
}

$steps = @{}

Draw-Text (T '5a6M5pW06YeR5rWB5ZG85Y+r5rWB56iLIFY5IC0g5pyA57WC5riF5pmw54mI') $titleFont $textBrush 710 30 1480 60 $centerFormat
Draw-Text (T '5omA5pyJ566t6aCt5Y+q5rK/5Lit5aSu5b6A5LiL6LWw77yb5q+P5LiA5q2l5bem5YG05qiZ56S66LKs5Lu75bGk77yM5Y+z5YG06Kqq5piO5qC45b+D6YKK55WM6IiH5Y+v6YeN55So5L2N572u77yM6YG/5YWN57ea5qKd6YGu5L2P5paH5a2X44CC') $subtitleFont $mutedBrush 230 94 2440 62 $centerFormat

Draw-Text (T '5ZyW5L6L77ya6LKs5Lu75bGk6aGP6Imy') $sectionFont $textBrush 150 185 650 42
Draw-LegendItem 150 245 (T '5aWJ54276ICFIC8g5LuY5qy+6ICF') '#fffbeb' '#d97706'
Draw-LegendItem 535 245 (T 'Q2h1cmNoUmVwb3J0IOeUouWTgeWxpA==') '#fff7ed' '#c2410c'
Draw-LegendItem 920 245 (T 'QVNQLk5FVCDlhbHnlKjmqYvmjqXlsaQ=') '#ecfeff' '#0891b2'
Draw-LegendItem 1305 245 (T 'U3BlZWNoTWVzc2FnZS5QYXltZW50cyDntJTph5HmtYHmoLjlv4M=') '#eef2ff' '#4f46e5'
Draw-LegendItem 1690 245 (T '56ys5LiJ5pa56YeR5rWBIFByb3ZpZGVy') '#f0fdf4' '#16a34a'
Draw-LegendItem 2075 245 (T '55Si5ZOB5LuY5qy+5b6M5bel5L2c5rWB') '#f8fafc' '#64748b'

Draw-Section (T '6ZqO5q61IEHvvJrlu7rnq4vku5jmrL7oiIflsI7lkJHnrKzkuInmlrk=') 340 '#fef3c7' '#d97706'
Draw-Step 's1' 440 (T '5aWJ54276ICFIC8g5LuY5qy+6ICF') (T 'MS4g5aGr5a+r5aWJ5427IC8g5LuY5qy+6LOH5paZ') (T '5LuY5qy+6ICF6Ly45YWl5aeT5ZCN44CB5aWJ54276aCF55uu44CB6YeR6aGN6IiH5LuY5qy+5pa55byP44CC6YCZ5Lqb6YO95pivIENodXJjaFJlcG9ydCDnmoTnlKLlk4Hos4fmlpnvvIzkuI3lsazmlrzpgJrnlKjph5HmtYHmoLjlv4PjgII=') '#fffbeb' '#d97706' ''
Draw-Step 's2' 640 (T 'Q2h1cmNoUmVwb3J0IOeUouWTgeWxpA==') (T 'Mi4gQ29udHJvbGxlciDmjqXmlLbkuKbpqZforYnoq4vmsYI=') (T 'Q2h1cmNoUmVwb3J0IOS/neeVmeaXouaciemggemdouOAgei3r+eUseiIh+eVq+mdoumpl+itie+8jOiyoOiyrOaKiueUouWTgeaDheWig+a6luWCmeWlveOAgg==') '#fff7ed' '#c2410c' ''
Draw-Step 's3' 840 (T 'Q2h1cmNoUmVwb3J0IOeUouWTgeWxpA==') (T 'My4g5L6dIFBBWV9QUk9WSURFUiDpgbjmk4cgUGF5bWVudCBQcm9maWxl') (T 'Q2h1cmNoUmVwb3J0UGF5bWVudFByb2ZpbGVSZXNvbHZlciDmioogYXBwc2V0dGluZ3MuanNvbiDnmoQgUEFZX1BST1ZJREVSIOWwjeaHieWIsOWRveWQjemHkea1geioreWumuaqlOOAgg==') '#fff7ed' '#c2410c' ''
Draw-Step 's4' 1040 (T 'QVNQLk5FVCDlhbHnlKjmqYvmjqXlsaQ=') (T 'NC4g5bu656uL5Lit56uL5LuY5qy+6KuL5rGC') (T '5oqK55Si5ZOB6LOH5paZ6L2J5oiQIFBheW1lbnRDcmVhdGVSZXF1ZXN044CC5aSW6YOo55Si5ZOB5LiN6ZyA6KaB55+l6YGTIFFQYXnjgIFNeVBheSDmiJYgVFNQRyDnmoTlsIjlsazmrITkvY3jgII=') '#ecfeff' '#0891b2' (T 'SG9zdCDpgornlYw=')
Draw-Step 's5' 1240 (T 'U3BlZWNoTWVzc2FnZS5QYXltZW50cyDntJTph5HmtYHmoLjlv4M=') (T 'NS4g5ZG85Y+r6YCa55So6YeR5rWB5qC45b+D') (T 'SVBheW1lbnRHYXRld2F5LkNyZWF0ZVBheW1lbnRBc3luYyDlj6rmjqXmlLYgcHJvdmlkZXItbmV1dHJhbCBEVE/vvIzkvp0gcHJvZmlsZSDot6/nlLHliLDmraPnorogUHJvdmlkZXLjgII=') '#eef2ff' '#4f46e5' (T '5qC45b+D6YKK55WM')
Draw-Step 's6' 1440 (T '56ys5LiJ5pa56YeR5rWBIFByb3ZpZGVy') (T 'Ni4gUHJvdmlkZXIg5bCB6KOd5Y2U5a6a57Sw56+A') (T 'U2lub3BhYy9RUGF544CBTXlQYXnjgIFUYWlzaGluL1RTUEcg55qE57C956ug44CB5Yqg5a+G44CB56uv6bue44CB5qyE5L2N5qC85byP6YO955WZ5ZyoIFNwZWVjaE1lc3NhZ2UuUGF5bWVudHMg5YWn6YOo44CC') '#f0fdf4' '#16a34a' ''
Draw-Step 's7' 1640 (T 'U3BlZWNoTWVzc2FnZS5QYXltZW50cyDntJTph5HmtYHmoLjlv4M=') (T 'Ny4g5Zue5YKz5bu656uL5LuY5qy+57WQ5p6c') (T '5qC45b+D5Zue5YKzIFBheW1lbnRDcmVhdGVSZXN1bHTvvIzkvovlpoIgUGF5bWVudFBhZ2VVcmzjgIFQcm92aWRlck9yZGVyUmVm44CB54uA5oWL6IiH5bey5riF55CG55qE6Ki65pa36LOH6KiK44CC') '#eef2ff' '#4f46e5' ''
Draw-Step 's8' 1840 (T '5aWJ54276ICFIC8g5LuY5qy+6ICF') (T 'OC4g5bCO5ZCR56ys5LiJ5pa55LuY5qy+6aCB') (T 'Q2h1cmNoUmVwb3J0IOWPquiyoOiyrCBSZWRpcmVjdOOAguS7mOasvuiAheWcqOmHkea1gemggeWujOaIkOS/oeeUqOWNoei8uOWFpeOAgUFUTS/ljK/mrL7miJbnrKzkuInmlrnku5jmrL7jgII=') '#fffbeb' '#d97706' ''

Draw-Section (T '6ZqO5q61IELvvJrku5jmrL7lrozmiJDjgIHlm57lkbzop6PmnpDoiIcgQUNL') 2060 '#cffafe' '#0891b2'
Draw-Step 's9' 2160 (T '56ys5LiJ5pa56YeR5rWBIFByb3ZpZGVy') (T 'OS4g56ys5LiJ5pa56YeR5rWB5Zue5ZG85pei5pyJIFJvdXRl') (T 'UVBheeOAgU15UGF544CBVFNQRyDljp/mnKzoqK3lrprnmoQgY2FsbGJhY2svcmV0dXJuIFVSTCDkv53nlZnvvIzpgb/lhY3noLTlo57ml6LmnInph5HmtYHlvozlj7DoqK3lrprjgII=') '#f0fdf4' '#16a34a' ''
Draw-Step 's10' 2360 (T 'QVNQLk5FVCDlhbHnlKjmqYvmjqXlsaQ=') (T 'MTAuIOaYoOWwhCBIVFRQIOWbnuWRvOeCuuS4reeri+iri+axgg==') (T 'UGF5bWVudEh0dHBSZXF1ZXN0TWFwcGVyIOiugOWPliBxdWVyeeOAgWZvcm3jgIFoZWFkZXLjgIFib2R577yM6L2J5oiQIFBheW1lbnRDYWxsYmFja1JlcXVlc3TjgII=') '#ecfeff' '#0891b2' (T 'SG9zdCDpgornlYw=')
Draw-Step 's11' 2560 (T 'U3BlZWNoTWVzc2FnZS5QYXltZW50cyDntJTph5HmtYHmoLjlv4M=') (T 'MTEuIOaguOW/g+mpl+itieOAgeino+WvhuiIh+ato+imj+WMlueLgOaFiw==') (T 'UGFyc2VDYWxsYmFja0FzeW5jIOeUseWQhCBQcm92aWRlciDpqZfnsL3jgIHop6Plr4bjgIHop6PmnpDku5jmrL7ni4DmhYvvvIzovLjlh7ogUGF5bWVudENhbGxiYWNrUmVzdWx044CC') '#eef2ff' '#4f46e5' (T '5qC45b+D6YKK55WM')
Draw-Step 's12' 2760 (T 'QVNQLk5FVCDlhbHnlKjmqYvmjqXlsaQ=') (T 'MTIuIOeUoueUn+S4puWbnuimhuesrOS4ieaWuSBBQ0s=') (T 'UGF5bWVudEFja25vd2xlZGdlbWVudFJlc3VsdE1hcHBlciDmiormoLjlv4MgQUNLIOi9ieaIkOaWh+Wtl+OAgUpTT04g5oiWIFJlZGlyZWN0IOWbnuaHie+8jOmAgeWbnuesrOS4ieaWuemHkea1geOAgg==') '#ecfeff' '#0891b2' ''
Draw-Step 's13' 2960 (T 'Q2h1cmNoUmVwb3J0IOeUouWTgeWxpA==') (T 'MTMuIENodXJjaFJlcG9ydCDmjqXmlLbkuK3nq4vlm57lkbzntZDmnpw=') (T '55Si5ZOB5bGk5ou/5Yiw55qE5pivIFByb2R1Y3RPcmRlcklk44CBQW1vdW5044CBU3RhdHVz44CBUHJvdmlkZXJPcmRlclJlZiDoiIflt7LmuIXnkIboqLrmlrfos4fmlpnjgII=') '#fff7ed' '#c2410c' ''

Draw-Section (T '6ZqO5q61IEPvvJrnlKLlk4Hoh6rlt7HnmoQgQ1JNIOabtOaWsOOAgUxJTkUg6YCa55+l6IiH57WQ5p6c6aCB') 3180 '#dcfce7' '#16a34a'
Draw-Step 's14' 3280 (T '55Si5ZOB5LuY5qy+5b6M5bel5L2c5rWB') (T 'MTQuIOWVn+WLleeUouWTgeS7mOasvuW+jOa1geeoiw==') (T 'UGF5bWVudFBvc3RQYXltZW50V29ya2Zsb3cg5Y+q6LKg6LKs5ZG85Y+r55Si5ZOB5a+m5L2c77yM5LiN5oqKIENSTeOAgUxJTkUg5oiW6LOH5paZ5bqr5L6d6LO05pS+6YCy6YeR5rWB5qC45b+D44CC') '#f8fafc' '#64748b' ''
Draw-Step 's15' 3480 (T 'Q2h1cmNoUmVwb3J0IOeUouWTgeWxpA==') (T 'MTUuIOabtOaWsCBDUk0gLyDlpYnnjbsgLyDku5josrvllq4=') (T 'Q2h1cmNoUmVwb3J0IOmAj+mBjiBJUGF5bWVudFJlY29yZFVwZGF0ZXIg55qE55Si5ZOB5a+m5L2c5pu05pawIENSTSDku5josrvllq7jgIHlpYnnjbvntIDpjITmiJbluLPllq7ni4DmhYvjgII=') '#fff7ed' '#c2410c' ''
Draw-Step 's16' 3680 (T 'Q2h1cmNoUmVwb3J0IOeUouWTgeWxpA==') (T 'MTYuIOmAmuefpeS7mOasvuiAhQ==') (T 'Q2h1cmNoUmVwb3J0IOmAj+mBjiBJUGF5bWVudFBheWVyTm90aWZpZXIg55m86YCBIExJTkUg6KiK5oGv77yb5pyq5L6G55Si5ZOB5Y+v5pS55oiQIEVtYWls44CBU01TIOaIluWFtuS7lumAmuefpeOAgg==') '#fff7ed' '#c2410c' ''
Draw-Step 's17' 3880 (T 'Q2h1cmNoUmVwb3J0IOeUouWTgeWxpA==') (T 'MTcuIOmhr+ekuue1kOaenOmggeaIluW+jOe6jOa1geeoiw==') (T 'Q2h1cmNoUmVwb3J0IOaxuuWumuaIkOWKn+OAgeWkseaVl+OAgeW+heS7mOasvuOAgemHjeaWsOS7mOasvuaIluS4i+S4gOWAi+eUouWTgemggemdouOAgumAmeS4jeaYr+mHkea1geaguOW/g+eahOiyrOS7u+OAgg==') '#fff7ed' '#c2410c' ''
Draw-Step 's18' 4080 (T '55Si5ZOB5LuY5qy+5b6M5bel5L2c5rWB') (T 'MTguIOacquS+hiBBU1AuTkVUIENvcmUg55Si5ZOB6YeN55So5pa55byP') (T '5bu66Kit5YWs5Y+457at5L+u57O757Wx44CB5Y2U5pyD5pyD5ZOh57O757Wx44CB55m856Wo5pS25qy+57O757Wx5Y+v6YeN55SoIFNwZWVjaE1lc3NhZ2UuUGF5bWVudHMg6IiHIEFzcE5ldENvcmUg5qmL5o6l5bGk77yM5YaN5a+m5L2c6Ieq5bex55qE6LOH5paZ5pu05paw6IiH6YCa55+l44CC') '#f8fafc' '#64748b' ''

Draw-Section (T '6ZqO5q61IETvvJrlj6/ph43nlKjpgornlYznuL3ntZA=') 4310 '#f8fafc' '#64748b'
Draw-BoundaryCard 150 4410 (T '5qC45b+D6YKK55WM') (T 'U3BlZWNoTWVzc2FnZS5QYXltZW50c++8mlByb3ZpZGVyIOWNlOWumuOAgeewveeroOOAgeWKoOWvhuOAgeW7uueri+S7mOasvuOAgeafpeipouOAgeWbnuWRvOino+aekOOAgeeLgOaFi+ato+imj+WMluOAgg==') '#eef2ff' '#4f46e5'
Draw-BoundaryCard 1090 4410 (T 'SG9zdCDpgornlYw=') (T 'U3BlZWNoTWVzc2FnZS5QYXltZW50cy5Bc3BOZXRDb3Jl77yaSHR0cFJlcXVlc3Qg5bCN5oeJ44CBQUNLIOi9iSBBU1AuTkVUIOWbnuaHieOAgURJIOi8lOWKqeOAgg==') '#ecfeff' '#0891b2'
Draw-BoundaryCard 2030 4410 (T '55Si5ZOB6YKK55WM') (T 'Q2h1cmNoUmVwb3J0IOiIh+acquS+hueUouWTge+8mkNvbnRyb2xsZXLjgIHpoIHpnaLjgIFDUk0vRELjgIFMSU5FL+mAmuefpeOAgeWOu+mHjeOAgeWVhualrea1geeoi+OAgg==') '#fff7ed' '#c2410c'
Draw-BoundaryCard 705 4630 (T 'TGluZVBheSDoqqrmmI4=') (T 'TGluZVBheSDnm67liY3ku43mmK/njajnq4vlsIjmoYjvvIzmspLmnInntI3lhaXmnKzmrKEgU2lub3BhYy9RUGF544CBTXlQYXnjgIFUYWlzaGluL1RTUEcg55qE5qC45b+D5oq96Zui44CC') '#f8fafc' '#64748b'

Draw-Down 's1' 's2'
Draw-Down 's2' 's3'
Draw-Down 's3' 's4'
Draw-Down 's4' 's5'
Draw-Down 's5' 's6'
Draw-Down 's6' 's7'
Draw-Down 's7' 's8'
Draw-Down 's8' 's9'
Draw-Down 's9' 's10'
Draw-Down 's10' 's11'
Draw-Down 's11' 's12'
Draw-Down 's12' 's13'
Draw-Down 's13' 's14'
Draw-Down 's14' 's15'
Draw-Down 's15' 's16'
Draw-Down 's16' 's17'
Draw-Down 's17' 's18'

if ([System.IO.File]::Exists($outputPath)) {
    [System.IO.File]::Delete($outputPath)
}

[PaymentFlowPngWriterV9]::Save($bitmap, $outputPath)

$outputFile = Get-Item -LiteralPath $outputPath
if ($outputFile.Length -le 0) {
    throw "PNG render failed: output file is empty."
}

$graphics.Dispose()
$bitmap.Dispose()
$arrowPen.Dispose()
$titleFont.Dispose()
$subtitleFont.Dispose()
$sectionFont.Dispose()
$nodeTitleFont.Dispose()
$nodeBodyFont.Dispose()
$tagFont.Dispose()
$smallFont.Dispose()
$textBrush.Dispose()
$mutedBrush.Dispose()
$whiteBrush.Dispose()
$format.Dispose()
$centerFormat.Dispose()

Write-Output $outputPath
