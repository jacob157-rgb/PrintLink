Add-Type -AssemblyName System.Drawing
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# Background rounded squircle (#2563EB)
$bgBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(37, 99, 235))
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 54
$path.AddArc(12, 12, $r, $r, 180, 90)
$path.AddArc(244 - $r, 12, $r, $r, 270, 90)
$path.AddArc(244 - $r, 244 - $r, $r, $r, 0, 90)
$path.AddArc(12, 244 - $r, $r, $r, 90, 90)
$path.CloseFigure()
$g.FillPath($bgBrush, $path)

# White Printer shape
$whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$paperBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(238, 242, 255))
$accentBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(16, 185, 129))

# Paper feed tray (top)
$g.FillRectangle($paperBrush, 70, 42, 116, 64)

# Printer body
$g.FillRectangle($whiteBrush, 46, 96, 164, 78)

# Output slot
$slotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(15, 23, 42))
$g.FillRectangle($slotBrush, 64, 146, 128, 12)

# Printed paper extending out
$g.FillRectangle($paperBrush, 72, 156, 112, 52)

# Status LED dot (Emerald Green)
$g.FillEllipse($accentBrush, 182, 112, 16, 16)

# Text lines on printed paper
$linePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(79, 70, 229), 3)
$g.DrawLine($linePen, 86, 172, 150, 172)
$g.DrawLine($linePen, 86, 184, 170, 184)
$g.DrawLine($linePen, 86, 196, 130, 196)

$assetsDir = Join-Path $PSScriptRoot "..\src\PrintLink.Desktop\Assets"
if (!(Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
}

$pngPath = Join-Path $assetsDir "app.png"
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$icoPath = Join-Path $assetsDir "app.ico"
$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$fs = New-Object System.IO.FileStream $icoPath, ([System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()

Write-Output "ICONS_GENERATED_AT: $icoPath"
