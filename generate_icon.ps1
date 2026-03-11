# generate_icon.ps1 — Génère app.ico pour Transkript
# Logo : carré arrondi sombre (#1C1C1E) + "T" blanc gras + point rouge
Add-Type -AssemblyName System.Drawing

function New-TranskriptBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode       = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint   = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode   = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # ── Fond : carré arrondi #1C1C1E ──────────────────────────────────────────
    $r    = [int]($size * 0.20)
    $bg   = [System.Drawing.Color]::FromArgb(255, 28, 28, 30)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0,            0,            $r*2, $r*2, 180, 90)
    $path.AddArc($size-$r*2,   0,            $r*2, $r*2, 270, 90)
    $path.AddArc($size-$r*2,   $size-$r*2,   $r*2, $r*2, 0,   90)
    $path.AddArc(0,            $size-$r*2,   $r*2, $r*2, 90,  90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush($bg)), $path)

    # ── Lettre "T" blanche centrée ────────────────────────────────────────────
    $fontSize = [float]($size * 0.54)
    $font  = New-Object System.Drawing.Font("Segoe UI", $fontSize,
                [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $sf    = New-Object System.Drawing.StringFormat
    $sf.Alignment     = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString("T", $font, $white,
        (New-Object System.Drawing.RectangleF(0, 0, $size, $size)), $sf)

    # ── Point rouge (indicateur d'enregistrement) ─────────────────────────────
    $dot  = [int]($size * 0.20)
    $padX = [int]($size * 0.07)
    $padY = [int]($size * 0.07)
    $red  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 59, 48))
    $g.FillEllipse($red, $size - $dot - $padX, $size - $dot - $padY, $dot, $dot)

    $g.Dispose()
    return $bmp
}

function Save-MultiIco([System.Drawing.Bitmap[]]$bitmaps, [string]$outPath) {
    # Sérialise chaque bitmap en PNG (format accepté par les ICO Windows Vista+)
    $pngs = foreach ($b in $bitmaps) {
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        ,$ms.ToArray()
        $ms.Dispose()
    }

    $fs = [System.IO.File]::Create($outPath)
    $bw = New-Object System.IO.BinaryWriter($fs)

    # En-tête ICO
    $bw.Write([uint16]0)                    # réservé
    $bw.Write([uint16]1)                    # type ICO
    $bw.Write([uint16]$bitmaps.Count)       # nombre d'images

    # Répertoire des images
    $offset = [uint32](6 + 16 * $bitmaps.Count)
    for ($i = 0; $i -lt $bitmaps.Count; $i++) {
        $w = $bitmaps[$i].Width
        $bw.Write([byte]  $(if ($w -ge 256) { 0 } else { $w }))  # largeur (0 = 256)
        $bw.Write([byte]  $(if ($w -ge 256) { 0 } else { $w }))  # hauteur
        $bw.Write([byte]  0)                    # nb couleurs (0 = true color)
        $bw.Write([byte]  0)                    # réservé
        $bw.Write([uint16]1)                    # plans
        $bw.Write([uint16]32)                   # bits/pixel
        $bw.Write([uint32]$pngs[$i].Length)     # taille données
        $bw.Write([uint32]$offset)              # offset données
        $offset += [uint32]$pngs[$i].Length
    }

    # Données PNG
    foreach ($png in $pngs) { $bw.Write($png) }

    $bw.Dispose()
    Write-Host "  app.ico genere ($($bitmaps | ForEach-Object { $_.Width }) px)"
}

# ── Génération ────────────────────────────────────────────────────────────────
$sizes   = @(16, 32, 48, 256)
$bitmaps = $sizes | ForEach-Object { New-TranskriptBitmap $_ }
Save-MultiIco -bitmaps $bitmaps -outPath (Join-Path $PSScriptRoot "app.ico")
foreach ($b in $bitmaps) { $b.Dispose() }
