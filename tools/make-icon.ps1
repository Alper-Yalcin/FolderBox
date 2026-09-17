# Generates Assets\FolderBox.ico (multi-size, PNG-compressed): the FolderBox "Flap" glyph in purple.
param([string]$Out = "$PSScriptRoot\..\src\FolderBox.App\Assets\FolderBox.ico")

Add-Type -AssemblyName System.Drawing

function New-FolderPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 48.0
    # Same palette as FolderBoxIcon: purple base (#8B5CF6), darker back plate, lighter front flap
    $back  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 108, 72, 192))
    $front = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 160, 122, 248))
    $paper = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(242, 255, 255, 255))
    $shade = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(51, 0, 0, 0))

    function RoundRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure(); return $p
    }

    # shadow
    $g.FillPath($shade, (RoundRect (5*$s) (15*$s) (40*$s) (29*$s) (4*$s)))
    # back plate with tab
    $tab = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tab.AddLine((7*$s), (9*$s), (19*$s), (9*$s)); $tab.AddLine((19*$s), (9*$s), (23*$s), (13*$s)); $tab.AddLine((23*$s), (13*$s), (7*$s), (13*$s)); $tab.CloseFigure()
    $g.FillPath($back, $tab)
    $g.FillPath($back, (RoundRect (4*$s) (11*$s) (40*$s) (31*$s) (3*$s)))
    # paper
    $g.FillPath($paper, (RoundRect (9*$s) (16*$s) (30*$s) (8*$s) (1.5*$s)))
    # front flap
    $g.FillPath($front, (RoundRect (4*$s) (22*$s) (40*$s) (20*$s) (3*$s)))
    # chevron
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, 255, 255, 255)), ([Math]::Max(1, 2.2*$s))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLine($pen, (19*$s), (30*$s), (24*$s), (35*$s))
    $g.DrawLine($pen, (24*$s), (35*$s), (29*$s), (30*$s))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,[byte[]]$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($sz in $sizes) { $pngs[$sz] = New-FolderPng $sz }

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    [byte[]]$data = $pngs[$sz]
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($sz in $sizes) { [byte[]]$d = $pngs[$sz]; $bw.Write($d, 0, $d.Length) }
$bw.Flush(); $fs.Close()
Write-Host "Wrote $Out"
if ($env:FOLDERBOX_ICON_PREVIEW) { [System.IO.File]::WriteAllBytes($env:FOLDERBOX_ICON_PREVIEW, [byte[]]$pngs[128]) }
