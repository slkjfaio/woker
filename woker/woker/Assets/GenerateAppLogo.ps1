Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [single]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-WorkbenchMark {
    param(
        [System.Drawing.Graphics]$Graphics,
        [single]$X,
        [single]$Y,
        [single]$Size
    )

    $blue = [System.Drawing.Color]::FromArgb(0, 120, 212)
    $paper = [System.Drawing.Color]::FromArgb(255, 255, 255)
    $ink = [System.Drawing.Color]::FromArgb(0, 94, 167)

    $tile = New-RoundedPath ([System.Drawing.RectangleF]::new($X, $Y, $Size, $Size)) ($Size * 0.22)
    $Graphics.FillPath(([System.Drawing.SolidBrush]::new($blue)), $tile)
    $tile.Dispose()

    $pageX = $X + ($Size * 0.27)
    $pageY = $Y + ($Size * 0.20)
    $pageW = $Size * 0.46
    $pageH = $Size * 0.60
    $page = New-RoundedPath ([System.Drawing.RectangleF]::new($pageX, $pageY, $pageW, $pageH)) ($Size * 0.07)
    $Graphics.FillPath(([System.Drawing.SolidBrush]::new($paper)), $page)
    $page.Dispose()

    $linePen = [System.Drawing.Pen]::new($ink, $Size * 0.055)
    $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $Graphics.DrawLine($linePen, $X + ($Size * 0.38), $Y + ($Size * 0.38), $X + ($Size * 0.62), $Y + ($Size * 0.38))
    $Graphics.DrawLine($linePen, $X + ($Size * 0.38), $Y + ($Size * 0.50), $X + ($Size * 0.55), $Y + ($Size * 0.50))

    $checkPen = [System.Drawing.Pen]::new($ink, $Size * 0.065)
    $checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $Graphics.DrawLines($checkPen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($X + ($Size * 0.39), $Y + ($Size * 0.64)),
        [System.Drawing.PointF]::new($X + ($Size * 0.47), $Y + ($Size * 0.72)),
        [System.Drawing.PointF]::new($X + ($Size * 0.62), $Y + ($Size * 0.58))
    ))

    $linePen.Dispose()
    $checkPen.Dispose()
}

function Save-SquareLogo {
    param(
        [int]$Size,
        [string]$Path
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $markSize = $Size * 0.82
    Draw-WorkbenchMark $graphics (($Size - $markSize) / 2) (($Size - $markSize) / 2) $markSize
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Get-SquareLogoPngBytes {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $markSize = $Size * 0.82
    Draw-WorkbenchMark $graphics (($Size - $markSize) / 2) (($Size - $markSize) / 2) $markSize
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    return ,$bytes
}

function Save-IcoLogo {
    param([string]$Path)

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $frames.Add((Get-SquareLogoPngBytes $size))
    }

    $stream = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { [byte]0 } else { [byte]$sizes[$index] }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frames[$index].Length)
        $writer.Write([UInt32]$offset)
        $offset += $frames[$index].Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame)
    }

    $writer.Dispose()
    $stream.Dispose()
}

function Save-WideLogo {
    param(
        [int]$Width,
        [int]$Height,
        [string]$Path
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $markSize = $Height * 0.58
    $markX = ($Width - $markSize) / 2
    Draw-WorkbenchMark $graphics $markX (($Height - $markSize) / 2) $markSize
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Save-SplashLogo {
    param(
        [int]$Width,
        [int]$Height,
        [string]$Path
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(18, 24, 38))
    $markSize = $Height * 0.26
    Draw-WorkbenchMark $graphics (($Width - $markSize) / 2) (($Height - $markSize) / 2 - 38) $markSize
    $font = [System.Drawing.Font]::new('Segoe UI Semibold', 32, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString('WorkBench', $font, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0, ($Height / 2) + 72, $Width, 48), $format)
    $format.Dispose()
    $font.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

$assetRoot = Split-Path -Parent $PSCommandPath
Save-SquareLogo 300 (Join-Path $assetRoot 'Square150x150Logo.scale-200.png')
Save-SquareLogo 88 (Join-Path $assetRoot 'Square44x44Logo.scale-200.png')
Save-SquareLogo 48 (Join-Path $assetRoot 'LockScreenLogo.scale-200.png')
Save-SquareLogo 24 (Join-Path $assetRoot 'Square44x44Logo.targetsize-24_altform-unplated.png')
Save-SquareLogo 50 (Join-Path $assetRoot 'StoreLogo.png')
Save-WideLogo 620 300 (Join-Path $assetRoot 'Wide310x150Logo.scale-200.png')
Save-SplashLogo 1240 600 (Join-Path $assetRoot 'SplashScreen.scale-200.png')
Save-IcoLogo (Join-Path $assetRoot 'WorkBench.ico')
