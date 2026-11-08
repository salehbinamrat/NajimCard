<#
.SYNOPSIS
Generates the native multiresolution NajmCard Windows .ico from vector primitives.
No online assets, secrets, third-party font files or Hadramout resources required.
Runs before WPF compilation so Portable NajmCard.exe embeds the icon.
#>
param(
  [string]$Output = (Join-Path $PSScriptRoot "..\src\NajmCard.Desktop\Assets\NajmCard.ico")
)
$ErrorActionPreference="Stop"
Add-Type -AssemblyName System.Drawing

function C([string]$hex) {
    return [System.Drawing.ColorTranslator]::FromHtml($hex)
}
function RoundPath([single]$x,[single]$y,[single]$w,[single]$h,[single]$radius) {
    $p=[System.Drawing.Drawing2D.GraphicsPath]::new()
    $d=$radius*2
    $p.AddArc($x,$y,$d,$d,180,90)
    $p.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90)
    $p.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $p.CloseFigure()
    return $p
}
function FillRound($g,[single]$x,[single]$y,[single]$w,[single]$h,
                   [single]$radius,[string]$color) {
    $p=RoundPath $x $y $w $h $radius
    $b=[System.Drawing.SolidBrush]::new((C $color))
    try {$g.FillPath($b,$p)} finally {$b.Dispose();$p.Dispose()}
}
$bitmap=[System.Drawing.Bitmap]::new(512,512,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g=[System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
try {
    $base=RoundPath 19 19 474 474 93
    $bg=[System.Drawing.Drawing2D.LinearGradientBrush]::new(
       ([System.Drawing.Rectangle]::new(19,19,474,474)),(C "#601932"),
       (C "#190F30"),[single]135)
    $g.FillPath($bg,$base)
    $outline=[System.Drawing.Pen]::new((C "#F5C975"),21)
    $g.DrawPath($outline,$base)
    $inner=[System.Drawing.Pen]::new((C "#BA824D"),4)
    $g.DrawPath($inner,$base)
    $bg.Dispose();$outline.Dispose();$inner.Dispose();$base.Dispose()

    # Large gold star mark, rendered without unreadable text at 16x16.
    $star=[System.Collections.Generic.List[System.Drawing.PointF]]::new()
    for($i=0;$i -lt 10;$i++) {
        $radius=if($i%2 -eq 0){96}else{40}
        $angle=([Math]::PI*$i/5)-([Math]::PI/2)
        $star.Add([System.Drawing.PointF]::new(
          [single](170+$radius*[Math]::Cos($angle)),
          [single](164+$radius*[Math]::Sin($angle))))
    }
    $gold=[System.Drawing.SolidBrush]::new((C "#F8CC70"))
    $g.FillPolygon($gold,$star.ToArray())
    $gold.Dispose()

    # Ticket/card stack and subtle router-like signal arch.
    FillRound $g 164 250 246 149 27 "#724563"
    FillRound $g 137 277 246 149 26 "#D0A2C0"
    FillRound $g 119 262 244 148 25 "#F8F5F2"
    FillRound $g 132 275 218 118 18 "#E9E9FC"
    FillRound $g 144 303 191 27 8 "#431832"
    FillRound $g 144 343 130 13 6 "#664578"
    FillRound $g 144 372 157 8 4 "#8F7CA0"

    $wave=[System.Drawing.Pen]::new((C "#58D6F2"),18)
    $wave.StartCap=[System.Drawing.Drawing2D.LineCap]::Round
    $wave.EndCap=[System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($wave,296,102,155,150,220,101)
    $g.DrawArc($wave,318,125,108,107,223,94)
    $g.DrawArc($wave,345,150,53,61,223,91)
    $dot=[System.Drawing.SolidBrush]::new((C "#58D6F2"))
    $g.FillEllipse($dot,363,184,20,20)
    $dot.Dispose();$wave.Dispose()

    # Short golden guide sweep tying star to the voucher motif.
    $sweep=[System.Drawing.Pen]::new((C "#F3C86D"),12)
    $sweep.StartCap=[System.Drawing.Drawing2D.LineCap]::Round
    $sweep.EndCap=[System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($sweep,80,91,330,290,160,89)
    $sweep.Dispose()
}
finally {$g.Dispose()}

# PNG-compressed entries are supported in contemporary Windows ICO files.
# Explicit 16/24/32/48/64/128/256 sizes avoid low quality taskbar and Explorer scaling.
$sizes=@(16,24,32,48,64,128,256)
$pngs=[System.Collections.Generic.List[byte[]]]::new()
try {
    foreach($size in $sizes) {
        $scaled=[System.Drawing.Bitmap]::new($size,$size,
          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[System.Drawing.Graphics]::FromImage($scaled)
        try {
            $graphics.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($bitmap,0,0,$size,$size)
        }
        finally {$graphics.Dispose()}
        $stream=[System.IO.MemoryStream]::new()
        try {
            $scaled.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
            $pngs.Add($stream.ToArray())
        }
        finally {$stream.Dispose();$scaled.Dispose()}
    }
}
finally {$bitmap.Dispose()}
$target=[System.IO.Path]::GetFullPath($Output)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target))|Out-Null
$file=[System.IO.FileStream]::new($target,[System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,[System.IO.FileShare]::None)
$writer=[System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0)        # ICO reserved
    $writer.Write([UInt16]1)        # ICO icon
    $writer.Write([UInt16]$sizes.Count)
    $offset=[UInt32](6+16*$sizes.Count)
    for($i=0;$i -lt $sizes.Count;$i++) {
        $size=$sizes[$i]
        $encodedSize=if($size -eq 256){[byte]0}else{[byte]$size}
        $writer.Write($encodedSize)
        $writer.Write($encodedSize)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$pngs[$i].Length)
        $writer.Write($offset)
        $offset+=[UInt32]$pngs[$i].Length
    }
    foreach($png in $pngs) {$writer.Write($png)}
}
finally {$writer.Dispose()}
if((Get-Item $target).Length -lt 2000) {throw "NajmCard.ico is unexpectedly small"}
Write-Host "Generated NajmCard application Icon: $target"
