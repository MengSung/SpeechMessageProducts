param(
    [Parameter(Mandatory = $true)]
    [string] $MermaidFile,

    [Parameter(Mandatory = $true)]
    [string] $OutputFile,

    [string] $Title = ''
)

Add-Type -AssemblyName System.Drawing

if (-not ('PaymentFlowDeepPngWriter' -as [type])) {
Add-Type -ReferencedAssemblies 'System.Drawing','System.IO.Compression' -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class PaymentFlowDeepPngWriter
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

$mermaidPath = if ([System.IO.Path]::IsPathRooted($MermaidFile)) {
    $MermaidFile
} else {
    [System.IO.Path]::Combine($scriptDir, $MermaidFile)
}

$outputPath = if ([System.IO.Path]::IsPathRooted($OutputFile)) {
    $OutputFile
} else {
    [System.IO.Path]::Combine($scriptDir, $OutputFile)
}

function Decode-Label([string] $text) {
    $decoded = $text.Replace('<br/>', "`n").Replace('<br>', "`n")
    return [System.Net.WebUtility]::HtmlDecode($decoded)
}

function Get-NodeLabel([string] $line) {
    if ($line -match '^\s*([A-Za-z][A-Za-z0-9_]*)\s*(?:\{|\[|>)"(.+)"(?:\}|\]|$)') {
        return [pscustomobject]@{ Id = $matches[1]; Label = Decode-Label $matches[2] }
    }

    if ($line -match '^\s*([A-Za-z][A-Za-z0-9_]*)\s*(?:\{\s*)"(.+)"\s*\}') {
        return [pscustomobject]@{ Id = $matches[1]; Label = Decode-Label $matches[2] }
    }

    return $null
}

function Get-Edges([string] $line) {
    $clean = $line.Trim()
    if ($clean -notmatch '-->') {
        return @()
    }

    $clean = $clean -replace '\|[^|]*\|', ''
    $tokens = [regex]::Matches($clean, '[A-Za-z][A-Za-z0-9_]*')
    $ids = @()
    foreach ($token in $tokens) {
        $value = $token.Value
        if ($value -in @('flowchart', 'TD', 'LR', 'subgraph', 'end', 'classDef', 'class')) {
            continue
        }
        $ids += $value
    }

    $edges = @()
    for ($i = 0; $i -lt $ids.Count - 1; $i++) {
        $edges += [pscustomobject]@{ From = $ids[$i]; To = $ids[$i + 1] }
    }

    return $edges
}

function New-Brush($hex) {
    return New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($hex))
}

function New-Pen($hex, $width = 3) {
    return New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml($hex), $width)
}

function Measure-NodeHeight([string] $label) {
    $lineCount = ([regex]::Matches($label, "`n")).Count + 1
    return [Math]::Max(132, 48 + ($lineCount * 34))
}

function Draw-Text($graphics, $text, $font, $brush, $x, $y, $w, $h, $format) {
    $rect = New-Object System.Drawing.RectangleF($x, $y, $w, $h)
    $graphics.DrawString($text, $font, $brush, $rect, $format)
}

function Draw-Node($graphics, $node, $fontTitle, $fontBody, $textBrush, $format) {
    $fill = New-Brush $node.Fill
    $pen = New-Pen $node.Stroke 3
    $rect = New-Object System.Drawing.Rectangle($node.X, $node.Y, $node.W, $node.H)
    $graphics.FillRectangle($fill, $rect)
    $graphics.DrawRectangle($pen, $rect)

    $lines = $node.Label -split "`n"
    $title = $lines[0]
    $body = if ($lines.Count -gt 1) { ($lines[1..($lines.Count - 1)] -join "`n") } else { '' }
    Draw-Text $graphics $title $fontTitle $textBrush ($node.X + 22) ($node.Y + 18) ($node.W - 44) 38 $format
    if (-not [string]::IsNullOrWhiteSpace($body)) {
        Draw-Text $graphics $body $fontBody $textBrush ($node.X + 22) ($node.Y + 62) ($node.W - 44) ($node.H - 76) $format
    }

    $fill.Dispose()
    $pen.Dispose()
}

function Draw-Edge($graphics, $from, $to, $pen) {
    $x1 = [int]($from.X + ($from.W / 2))
    $y1 = [int]($from.Y + $from.H)
    $x2 = [int]($to.X + ($to.W / 2))
    $y2 = [int]$to.Y

    if ([Math]::Abs($x1 - $x2) -lt 8) {
        $graphics.DrawLine($pen, $x1, $y1, $x2, $y2)
        return
    }

    $midY = [int](($y1 + $y2) / 2)
    $plain = New-Object System.Drawing.Pen($pen.Color, $pen.Width)
    $graphics.DrawLine($plain, $x1, $y1, $x1, $midY)
    $graphics.DrawLine($plain, $x1, $midY, $x2, $midY)
    $graphics.DrawLine($pen, $x2, $midY, $x2, $y2)
    $plain.Dispose()
}

$lines = [System.IO.File]::ReadAllLines($mermaidPath, [System.Text.Encoding]::UTF8)
$nodes = [ordered]@{}
$edges = New-Object System.Collections.Generic.List[object]
$classMap = @{}

foreach ($line in $lines) {
    $node = Get-NodeLabel $line
    if ($node -ne $null -and -not $nodes.Contains($node.Id)) {
        $nodes[$node.Id] = [pscustomobject]@{
            Id = $node.Id
            Label = $node.Label
            Class = 'default'
            Fill = '#ffffff'
            Stroke = '#64748b'
            X = 0
            Y = 0
            W = 0
            H = 0
        }
    }

    foreach ($edge in (Get-Edges $line)) {
        $edges.Add($edge)
    }

    if ($line.Trim() -match '^class\s+(.+?)\s+([A-Za-z][A-Za-z0-9_]*)\s*$') {
        $ids = $matches[1].Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
        foreach ($id in $ids) {
            $classMap[$id] = $matches[2]
        }
    }
}

$palette = @{
    church = @{ Fill = '#fff7ed'; Stroke = '#c2410c' }
    host = @{ Fill = '#ecfeff'; Stroke = '#0891b2' }
    core = @{ Fill = '#eef2ff'; Stroke = '#4f46e5' }
    provider = @{ Fill = '#f0fdf4'; Stroke = '#16a34a' }
    workflow = @{ Fill = '#f8fafc'; Stroke = '#64748b' }
    result = @{ Fill = '#f8fafc'; Stroke = '#64748b' }
    ack = @{ Fill = '#f8fafc'; Stroke = '#64748b' }
    service = @{ Fill = '#ecfeff'; Stroke = '#0891b2' }
    sinopac = @{ Fill = '#ecfeff'; Stroke = '#0891b2' }
    mypay = @{ Fill = '#f0fdf4'; Stroke = '#16a34a' }
    taishin = @{ Fill = '#fff7ed'; Stroke = '#c2410c' }
    default = @{ Fill = '#ffffff'; Stroke = '#64748b' }
}

foreach ($id in $nodes.Keys) {
    if ($classMap.ContainsKey($id)) {
        $nodes[$id].Class = $classMap[$id]
    }
    $colors = $palette[$nodes[$id].Class]
    if ($null -eq $colors) {
        $colors = $palette.default
    }
    $nodes[$id].Fill = $colors.Fill
    $nodes[$id].Stroke = $colors.Stroke
}

$nodeList = @($nodes.Values)
$nodeW = 1180
$leftX = 170
$rightX = 1530
$topY = 210
$gapY = 54
$columns = if ($nodeList.Count -gt 32) { 3 } elseif ($nodeList.Count -gt 18) { 2 } else { 1 }
$nodeW = if ($columns -eq 3) { 900 } elseif ($columns -eq 2) { 1060 } else { 1360 }
$columnGap = 110
$currentX = $leftX
$currentY = $topY
$maxColumnHeight = 0
$perColumn = [Math]::Ceiling($nodeList.Count / $columns)

for ($i = 0; $i -lt $nodeList.Count; $i++) {
    if ($i -gt 0 -and ($i % $perColumn) -eq 0) {
        $currentX += $nodeW + $columnGap
        $currentY = $topY
    }

    $node = $nodeList[$i]
    $node.X = $currentX
    $node.Y = $currentY
    $node.W = $nodeW
    $node.H = Measure-NodeHeight $node.Label
    $currentY += $node.H + $gapY
    $maxColumnHeight = [Math]::Max($maxColumnHeight, $currentY)
}

$width = [Math]::Max(1800, $leftX + ($columns * $nodeW) + (($columns - 1) * $columnGap) + 170)
$height = [Math]::Max(1200, $maxColumnHeight + 120)

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$graphics.Clear([System.Drawing.Color]::White)

$fontFamily = 'Microsoft JhengHei'
$titleFont = New-Object System.Drawing.Font($fontFamily, 34, [System.Drawing.FontStyle]::Bold)
$nodeTitleFont = New-Object System.Drawing.Font($fontFamily, 20, [System.Drawing.FontStyle]::Bold)
$nodeBodyFont = New-Object System.Drawing.Font($fontFamily, 17, [System.Drawing.FontStyle]::Regular)
$legendFont = New-Object System.Drawing.Font($fontFamily, 15, [System.Drawing.FontStyle]::Regular)

$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(17, 24, 39))
$mutedBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))
$format = New-Object System.Drawing.StringFormat
$format.Trimming = [System.Drawing.StringTrimming]::Word
$format.LineAlignment = [System.Drawing.StringAlignment]::Near
$format.Alignment = [System.Drawing.StringAlignment]::Near

if (-not [string]::IsNullOrWhiteSpace($Title)) {
    Draw-Text $graphics $Title $titleFont $textBrush 170 35 ($width - 340) 54 $format
}

Draw-Text $graphics "Legend: orange=ChurchReport product layer, cyan=ASP.NET host adapter, purple=SpeechMessage.Payments core, green=provider, gray=result/workflow/summary." $legendFont $mutedBrush 170 92 ($width - 340) 36 $format

$edgePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(51, 65, 85), 4)
$edgePen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor

foreach ($edge in $edges) {
    if ($nodes.Contains($edge.From) -and $nodes.Contains($edge.To)) {
        Draw-Edge $graphics $nodes[$edge.From] $nodes[$edge.To] $edgePen
    }
}

foreach ($node in $nodeList) {
    Draw-Node $graphics $node $nodeTitleFont $nodeBodyFont $textBrush $format
}

if ([System.IO.File]::Exists($outputPath)) {
    [System.IO.File]::Delete($outputPath)
}

[PaymentFlowDeepPngWriter]::Save($bitmap, $outputPath)

$output = Get-Item -LiteralPath $outputPath
if ($output.Length -le 0) {
    throw "PNG render failed: output file is empty."
}

$graphics.Dispose()
$bitmap.Dispose()
$titleFont.Dispose()
$nodeTitleFont.Dispose()
$nodeBodyFont.Dispose()
$legendFont.Dispose()
$textBrush.Dispose()
$mutedBrush.Dispose()
$format.Dispose()
$edgePen.Dispose()

Write-Output $outputPath
