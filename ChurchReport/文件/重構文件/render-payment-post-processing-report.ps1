$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path -LiteralPath (Join-Path $scriptDir '..\..\..')
$sourceMarkdownPath = Join-Path $scriptDir 'payment-post-processing-refactor-report.md'
$diagramDataPath = Join-Path $scriptDir 'payment-post-processing-refactor-report-diagrams.json'
$outputDocxPath = Join-Path $scriptDir 'payment-post-processing-refactor-report.docx'
$architecturePngPath = Join-Path $scriptDir 'payment-post-processing-architecture.png'
$flowPngPath = Join-Path $scriptDir 'payment-post-processing-flow.png'

if (-not (Test-Path -LiteralPath $sourceMarkdownPath)) {
    throw "Report markdown not found: $sourceMarkdownPath"
}

if (-not (Test-Path -LiteralPath $diagramDataPath)) {
    throw "Diagram data not found: $diagramDataPath"
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-Font {
    param(
        [float] $Size,
        [System.Drawing.FontStyle] $Style = [System.Drawing.FontStyle]::Regular
    )

    $families = @('Microsoft JhengHei UI', 'Microsoft JhengHei', 'Arial Unicode MS', 'Arial')
    foreach ($family in $families) {
        try {
            return [System.Drawing.Font]::new($family, $Size, $Style, [System.Drawing.GraphicsUnit]::Pixel)
        }
        catch {
        }
    }

    return [System.Drawing.Font]::new([System.Drawing.FontFamily]::GenericSansSerif, $Size, $Style, [System.Drawing.GraphicsUnit]::Pixel)
}

function New-Pen {
    param(
        [string] $Color,
        [float] $Width = 3
    )

    return [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml($Color), $Width)
}

function New-Brush {
    param([string] $Color)

    return [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($Color))
}

function Draw-CenteredText {
    param(
        [System.Drawing.Graphics] $Graphics,
        [string] $Text,
        [System.Drawing.Font] $Font,
        [System.Drawing.Brush] $Brush,
        [System.Drawing.RectangleF] $Rect
    )

    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $format.Trimming = [System.Drawing.StringTrimming]::Word
    $format.FormatFlags = [System.Drawing.StringFormatFlags]::LineLimit
    $Graphics.DrawString($Text, $Font, $Brush, $Rect, $format)
    $format.Dispose()
}

function Draw-Box {
    param(
        [System.Drawing.Graphics] $Graphics,
        [object] $Box,
        [System.Drawing.Font] $TitleFont,
        [System.Drawing.Font] $BodyFont
    )

    $rect = [System.Drawing.RectangleF]::new([float]$Box.x, [float]$Box.y, [float]$Box.w, [float]$Box.h)
    $fillBrush = New-Brush $Box.fill
    $borderPen = New-Pen $Box.stroke 3
    $textBrush = New-Brush '#111827'

    $Graphics.FillRectangle($fillBrush, $rect)
    $Graphics.DrawRectangle($borderPen, $rect.X, $rect.Y, $rect.Width, $rect.Height)

    $titleRect = [System.Drawing.RectangleF]::new($rect.X + 14, $rect.Y + 12, $rect.Width - 28, 34)
    Draw-CenteredText $Graphics $Box.title $TitleFont $textBrush $titleRect

    $lineTop = $rect.Y + 56
    foreach ($line in $Box.lines) {
        $lineRect = [System.Drawing.RectangleF]::new($rect.X + 16, $lineTop, $rect.Width - 32, 28)
        Draw-CenteredText $Graphics $line $BodyFont $textBrush $lineRect
        $lineTop += 30
    }

    $fillBrush.Dispose()
    $borderPen.Dispose()
    $textBrush.Dispose()
}

function Get-BoxCenter {
    param([object] $Box)

    return [System.Drawing.PointF]::new([float]($Box.x + ($Box.w / 2)), [float]($Box.y + ($Box.h / 2)))
}

function Draw-Arrow {
    param(
        [System.Drawing.Graphics] $Graphics,
        [object] $FromBox,
        [object] $ToBox,
        [string] $Label,
        [System.Drawing.Font] $LabelFont
    )

    $from = Get-BoxCenter $FromBox
    $to = Get-BoxCenter $ToBox
    $pen = New-Pen '#334155' 3
    $cap = [System.Drawing.Drawing2D.AdjustableArrowCap]::new(6, 8)
    $pen.CustomEndCap = $cap

    $dx = $to.X - $from.X
    $dy = $to.Y - $from.Y
    $length = [Math]::Sqrt(($dx * $dx) + ($dy * $dy))
    if ($length -lt 1) { return }

    $start = [System.Drawing.PointF]::new([float]($from.X + ($dx / $length) * 115), [float]($from.Y + ($dy / $length) * 55))
    $end = [System.Drawing.PointF]::new([float]($to.X - ($dx / $length) * 130), [float]($to.Y - ($dy / $length) * 60))
    $Graphics.DrawLine($pen, $start, $end)

    if (-not [string]::IsNullOrWhiteSpace($Label)) {
        $labelBrush = New-Brush '#334155'
        $bgBrush = New-Brush '#ffffff'
        $midX = ($start.X + $end.X) / 2
        $midY = ($start.Y + $end.Y) / 2
        $labelRect = [System.Drawing.RectangleF]::new([float]($midX - 70), [float]($midY - 15), 140, 30)
        $Graphics.FillRectangle($bgBrush, $labelRect)
        Draw-CenteredText $Graphics $Label $LabelFont $labelBrush $labelRect
        $labelBrush.Dispose()
        $bgBrush.Dispose()
    }

    $cap.Dispose()
    $pen.Dispose()
}

function Render-ArchitectureDiagram {
    param(
        [object] $Diagram,
        [string] $OutputPath
    )

    $bitmap = [System.Drawing.Bitmap]::new(1320, 760)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $background = New-Brush '#ffffff'
    $titleBrush = New-Brush '#0f172a'
    $graphics.FillRectangle($background, 0, 0, $bitmap.Width, $bitmap.Height)

    $titleFont = New-Font 30 ([System.Drawing.FontStyle]::Bold)
    $boxTitleFont = New-Font 19 ([System.Drawing.FontStyle]::Bold)
    $bodyFont = New-Font 16
    $labelFont = New-Font 14

    Draw-CenteredText $graphics $Diagram.title $titleFont $titleBrush ([System.Drawing.RectangleF]::new(0, 18, $bitmap.Width, 44))

    $boxesById = @{}
    foreach ($box in $Diagram.boxes) {
        $boxesById[$box.id] = $box
    }

    foreach ($arrow in $Diagram.arrows) {
        Draw-Arrow $graphics $boxesById[$arrow.from] $boxesById[$arrow.to] $arrow.label $labelFont
    }

    foreach ($box in $Diagram.boxes) {
        Draw-Box $graphics $box $boxTitleFont $bodyFont
    }

    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $graphics.Dispose()
    $bitmap.Dispose()
    $background.Dispose()
    $titleBrush.Dispose()
    $titleFont.Dispose()
    $boxTitleFont.Dispose()
    $bodyFont.Dispose()
    $labelFont.Dispose()
}

function Render-FlowDiagram {
    param(
        [object] $Diagram,
        [string] $OutputPath
    )

    $bitmap = [System.Drawing.Bitmap]::new(1320, 980)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $background = New-Brush '#ffffff'
    $titleBrush = New-Brush '#0f172a'
    $graphics.FillRectangle($background, 0, 0, $bitmap.Width, $bitmap.Height)

    $titleFont = New-Font 30 ([System.Drawing.FontStyle]::Bold)
    $boxTitleFont = New-Font 18 ([System.Drawing.FontStyle]::Bold)
    $bodyFont = New-Font 15
    $labelFont = New-Font 14

    Draw-CenteredText $graphics $Diagram.title $titleFont $titleBrush ([System.Drawing.RectangleF]::new(0, 18, $bitmap.Width, 44))

    $steps = @($Diagram.steps)
    $positions = @(
        @{ x = 90; y = 95 }, @{ x = 410; y = 95 }, @{ x = 730; y = 95 },
        @{ x = 730; y = 300 }, @{ x = 410; y = 300 }, @{ x = 90; y = 300 },
        @{ x = 90; y = 520 }, @{ x = 410; y = 520 }, @{ x = 730; y = 520 }
    )

    $boxes = @()
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $step = $steps[$i]
        $position = $positions[$i]
        $boxes += [pscustomobject]@{
            id = $step.id
            x = $position.x
            y = $position.y
            w = 250
            h = 135
            title = $step.title
            lines = $step.lines
            fill = $step.fill
            stroke = $step.stroke
        }
    }

    for ($i = 0; $i -lt ($boxes.Count - 1); $i++) {
        Draw-Arrow $graphics $boxes[$i] $boxes[$i + 1] '' $labelFont
    }

    foreach ($box in $boxes) {
        Draw-Box $graphics $box $boxTitleFont $bodyFont
    }

    $noteBrush = New-Brush '#475569'
    Draw-CenteredText $graphics $Diagram.note $bodyFont $noteBrush ([System.Drawing.RectangleF]::new(100, 820, 1120, 48))

    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $graphics.Dispose()
    $bitmap.Dispose()
    $background.Dispose()
    $titleBrush.Dispose()
    $titleFont.Dispose()
    $boxTitleFont.Dispose()
    $bodyFont.Dispose()
    $labelFont.Dispose()
    $noteBrush.Dispose()
}

function Escape-Xml {
    param([AllowNull()][string] $Text)

    if ($null -eq $Text) { return '' }
    return [System.Security.SecurityElement]::Escape($Text)
}

function New-ParagraphXml {
    param(
        [AllowNull()][string] $Text,
        [string] $Style = $null,
        [bool] $Bold = $false,
        [bool] $Code = $false
    )

    $styleXml = ''
    if (-not [string]::IsNullOrWhiteSpace($Style)) {
        $styleXml = "<w:pPr><w:pStyle w:val=`"$Style`"/></w:pPr>"
    }

    $runPr = ''
    if ($Bold -or $Code) {
        $parts = @()
        if ($Bold) { $parts += '<w:b/>' }
        if ($Code) { $parts += '<w:rFonts w:ascii="Consolas" w:hAnsi="Consolas" w:eastAsia="Microsoft JhengHei"/><w:sz w:val="20"/>' }
        $runPr = '<w:rPr>' + ($parts -join '') + '</w:rPr>'
    }

    $escaped = Escape-Xml $Text
    return "<w:p>$styleXml<w:r>$runPr<w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:p>"
}

function New-BulletXml {
    param([string] $Text)

    $escaped = Escape-Xml "- $Text"
    return "<w:p><w:pPr><w:pStyle w:val=`"ListParagraph`"/></w:pPr><w:r><w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:p>"
}

function New-ImageParagraphXml {
    param(
        [string] $RelationshipId,
        [string] $Name,
        [int64] $Cx,
        [int64] $Cy
    )

    $escapedName = Escape-Xml $Name
    $docPrId = 1
    if ($RelationshipId -eq 'rIdFlow') {
        $docPrId = 2
    }

    return @"
<w:p>
  <w:r>
    <w:drawing>
        <wp:inline distT="0" distB="0" distL="0" distR="0">
        <wp:extent cx="$Cx" cy="$Cy"/>
        <wp:effectExtent l="0" t="0" r="0" b="0"/>
        <wp:docPr id="$docPrId" name="$escapedName"/>
        <wp:cNvGraphicFramePr/>
        <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
            <pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <pic:nvPicPr>
                <pic:cNvPr id="0" name="$escapedName"/>
                <pic:cNvPicPr/>
              </pic:nvPicPr>
              <pic:blipFill>
                <a:blip r:embed="$RelationshipId"/>
                <a:stretch><a:fillRect/></a:stretch>
              </pic:blipFill>
              <pic:spPr>
                <a:xfrm>
                  <a:off x="0" y="0"/>
                  <a:ext cx="$Cx" cy="$Cy"/>
                </a:xfrm>
                <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              </pic:spPr>
            </pic:pic>
          </a:graphicData>
        </a:graphic>
      </wp:inline>
    </w:drawing>
  </w:r>
</w:p>
"@
}

function New-TableXml {
    param([string[][]] $Rows)

    $xml = '<w:tbl><w:tblPr><w:tblStyle w:val="TableGrid"/><w:tblW w:w="0" w:type="auto"/></w:tblPr>'
    foreach ($row in $Rows) {
        $xml += '<w:tr>'
        foreach ($cell in $row) {
            $xml += '<w:tc><w:tcPr><w:tcW w:w="4800" w:type="dxa"/></w:tcPr>'
            $xml += (New-ParagraphXml $cell)
            $xml += '</w:tc>'
        }
        $xml += '</w:tr>'
    }
    $xml += '</w:tbl>'
    return $xml
}

function Add-ZipEntryText {
    param(
        [System.IO.Compression.ZipArchive] $Zip,
        [string] $Name,
        [string] $Content
    )

    $entry = $Zip.CreateEntry($Name)
    $stream = $entry.Open()
    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false))
    $writer.Write($Content)
    $writer.Dispose()
    $stream.Dispose()
}

function Add-ZipEntryFile {
    param(
        [System.IO.Compression.ZipArchive] $Zip,
        [string] $Name,
        [string] $SourcePath
    )

    $entry = $Zip.CreateEntry($Name)
    $entryStream = $entry.Open()
    $sourceStream = [System.IO.File]::OpenRead($SourcePath)
    $sourceStream.CopyTo($entryStream)
    $sourceStream.Dispose()
    $entryStream.Dispose()
}

function Convert-MarkdownToWordXml {
    param([string[]] $Lines)

    $body = New-Object System.Collections.Generic.List[string]
    $inCode = $false
    $codeBuffer = New-Object System.Collections.Generic.List[string]
    $inTable = $false
    $tableRows = New-Object System.Collections.Generic.List[string[]]

    foreach ($line in $Lines) {
        if ($line.StartsWith('```')) {
            if ($inCode) {
                foreach ($codeLine in $codeBuffer) {
                    $body.Add((New-ParagraphXml $codeLine -Code $true))
                }
                $codeBuffer.Clear()
                $inCode = $false
            }
            else {
                if ($inTable -and $tableRows.Count -gt 0) {
                    $body.Add((New-TableXml $tableRows.ToArray()))
                    $tableRows.Clear()
                    $inTable = $false
                }
                $inCode = $true
            }
            continue
        }

        if ($inCode) {
            $codeBuffer.Add($line)
            continue
        }

        if ($line -match '^\|(.+)\|$') {
            $cells = $line.Trim('|').Split('|') | ForEach-Object { $_.Trim() }
            if ($cells.Count -gt 0 -and (($cells -join '') -notmatch '^[-:\s]+$')) {
                $inTable = $true
                $tableRows.Add([string[]]$cells)
            }
            continue
        }

        if ($inTable -and $tableRows.Count -gt 0) {
            $body.Add((New-TableXml $tableRows.ToArray()))
            $tableRows.Clear()
            $inTable = $false
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            $body.Add((New-ParagraphXml ''))
        }
        elseif ($line.StartsWith('# ')) {
            $body.Add((New-ParagraphXml $line.Substring(2) 'Title'))
        }
        elseif ($line.StartsWith('## ')) {
            $body.Add((New-ParagraphXml $line.Substring(3) 'Heading1'))
        }
        elseif ($line.StartsWith('### ')) {
            $body.Add((New-ParagraphXml $line.Substring(4) 'Heading2'))
        }
        elseif ($line.StartsWith('- ')) {
            $body.Add((New-BulletXml $line.Substring(2)))
        }
        elseif ($line -match '^\d+\. ') {
            $body.Add((New-BulletXml $line.Substring($line.IndexOf(' ') + 1)))
        }
        else {
            $body.Add((New-ParagraphXml $line))
        }
    }

    if ($inTable -and $tableRows.Count -gt 0) {
        $body.Add((New-TableXml $tableRows.ToArray()))
        $tableRows.Clear()
    }

    return $body
}

function Build-Docx {
    param(
        [string] $MarkdownPath,
        [string] $OutputPath,
        [string] $ArchitectureImagePath,
        [string] $FlowImagePath
    )

    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Force
    }

    $lines = [System.IO.File]::ReadAllLines($MarkdownPath, [System.Text.Encoding]::UTF8)
    $bodyParts = Convert-MarkdownToWordXml $lines

    $insertAt = -1
    for ($i = 0; $i -lt $bodyParts.Count; $i++) {
        if ($bodyParts[$i] -like '*架構圖*') {
            $insertAt = $i + 2
            break
        }
    }

    if ($insertAt -gt 0) {
        $bodyParts.Insert($insertAt, (New-ImageParagraphXml 'rIdArchitecture' 'payment-post-processing-architecture.png' 9144000 5260000))
    }

    $flowInsertAt = -1
    for ($i = 0; $i -lt $bodyParts.Count; $i++) {
        if ($bodyParts[$i] -like '*付款後流程圖*') {
            $flowInsertAt = $i + 2
            break
        }
    }

    if ($flowInsertAt -gt 0) {
        $bodyParts.Insert($flowInsertAt, (New-ImageParagraphXml 'rIdFlow' 'payment-post-processing-flow.png' 9144000 6780000))
    }

    $documentXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:wpc="http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas"
 xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
 xmlns:o="urn:schemas-microsoft-com:office:office"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
 xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"
 xmlns:v="urn:schemas-microsoft-com:vml"
 xmlns:wp14="http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing"
 xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
 xmlns:w10="urn:schemas-microsoft-com:office:word"
 xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
 xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
 xmlns:wpg="http://schemas.microsoft.com/office/word/2010/wordprocessingGroup"
 xmlns:wpi="http://schemas.microsoft.com/office/word/2010/wordprocessingInk"
 xmlns:wne="http://schemas.microsoft.com/office/word/2006/wordml"
 xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"
 mc:Ignorable="w14 wp14">
  <w:body>
    $($bodyParts -join "`n")
    <w:sectPr>
      <w:pgSz w:w="11906" w:h="16838"/>
      <w:pgMar w:top="1134" w:right="850" w:bottom="1134" w:left="850" w:header="708" w:footer="708" w:gutter="0"/>
    </w:sectPr>
  </w:body>
</w:document>
"@

    $contentTypes = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Default Extension="png" ContentType="image/png"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>
'@

    $rootRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
'@

    $documentRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rIdArchitecture" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/payment-post-processing-architecture.png"/>
  <Relationship Id="rIdFlow" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/payment-post-processing-flow.png"/>
</Relationships>
'@

    $styles = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
    <w:name w:val="Normal"/>
    <w:rPr><w:rFonts w:ascii="Microsoft JhengHei" w:hAnsi="Microsoft JhengHei" w:eastAsia="Microsoft JhengHei"/><w:sz w:val="22"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Title">
    <w:name w:val="Title"/>
    <w:pPr><w:jc w:val="center"/><w:spacing w:after="240"/></w:pPr>
    <w:rPr><w:rFonts w:ascii="Microsoft JhengHei" w:hAnsi="Microsoft JhengHei" w:eastAsia="Microsoft JhengHei"/><w:b/><w:sz w:val="36"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="heading 1"/>
    <w:pPr><w:spacing w:before="360" w:after="160"/></w:pPr>
    <w:rPr><w:rFonts w:ascii="Microsoft JhengHei" w:hAnsi="Microsoft JhengHei" w:eastAsia="Microsoft JhengHei"/><w:b/><w:sz w:val="30"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="heading 2"/>
    <w:pPr><w:spacing w:before="240" w:after="120"/></w:pPr>
    <w:rPr><w:rFonts w:ascii="Microsoft JhengHei" w:hAnsi="Microsoft JhengHei" w:eastAsia="Microsoft JhengHei"/><w:b/><w:sz w:val="26"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="ListParagraph">
    <w:name w:val="List Paragraph"/>
    <w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr>
    <w:rPr><w:rFonts w:ascii="Microsoft JhengHei" w:hAnsi="Microsoft JhengHei" w:eastAsia="Microsoft JhengHei"/><w:sz w:val="22"/></w:rPr>
  </w:style>
  <w:style w:type="table" w:styleId="TableGrid">
    <w:name w:val="Table Grid"/>
    <w:tblPr><w:tblBorders><w:top w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:left w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:bottom w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:right w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:insideH w:val="single" w:sz="4" w:space="0" w:color="auto"/><w:insideV w:val="single" w:sz="4" w:space="0" w:color="auto"/></w:tblBorders></w:tblPr>
  </w:style>
</w:styles>
'@

    $zipStream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::CreateNew)
    $zip = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)

    Add-ZipEntryText $zip '[Content_Types].xml' $contentTypes
    Add-ZipEntryText $zip '_rels/.rels' $rootRels
    Add-ZipEntryText $zip 'word/document.xml' $documentXml
    Add-ZipEntryText $zip 'word/styles.xml' $styles
    Add-ZipEntryText $zip 'word/_rels/document.xml.rels' $documentRels
    Add-ZipEntryFile $zip 'word/media/payment-post-processing-architecture.png' $ArchitectureImagePath
    Add-ZipEntryFile $zip 'word/media/payment-post-processing-flow.png' $FlowImagePath

    $zip.Dispose()
    $zipStream.Dispose()
}

$diagramData = Get-Content -LiteralPath $diagramDataPath -Raw -Encoding UTF8 | ConvertFrom-Json
Render-ArchitectureDiagram $diagramData.architecture $architecturePngPath
Render-FlowDiagram $diagramData.flow $flowPngPath
Build-Docx $sourceMarkdownPath $outputDocxPath $architecturePngPath $flowPngPath

$docx = Get-Item -LiteralPath $outputDocxPath
$architecture = Get-Item -LiteralPath $architecturePngPath
$flow = Get-Item -LiteralPath $flowPngPath

if ($docx.Length -le 0 -or $architecture.Length -le 0 -or $flow.Length -le 0) {
    throw "Report render failed: one or more output files are empty."
}

Write-Host "Generated:"
Write-Host "  $($docx.FullName) ($($docx.Length) bytes)"
Write-Host "  $($architecture.FullName) ($($architecture.Length) bytes)"
Write-Host "  $($flow.FullName) ($($flow.Length) bytes)"
