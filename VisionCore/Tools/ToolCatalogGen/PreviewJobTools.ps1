# Dev check: runs the VisionCore expression parser + ToolCatalog over every cell of a .jobx file
# and prints what the parameter editor would show.
# Usage: powershell -File PreviewJobTools.ps1 -JobPath <file.jobx> [-Exe ..\..\bin\Debug\VisionCore.exe]
param(
    [Parameter(Mandatory = $true)][string]$JobPath,
    [string]$Exe
)

if (-not $Exe) { $Exe = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..\bin\Debug\VisionCore.exe" }

# .jobx = tar archive; the sheet file is XOR-obfuscated JSON with a 4-byte key.
$tmp = Join-Path $env:TEMP ("jobx_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $tmp | Out-Null
tar -xf $JobPath -C $tmp
$key = [byte[]](0x72, 0x9B, 0x0F, 0x2E)
$asm = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::GetFullPath($Exe))
$catalog = [VisionCore.Models.JobEditor.ToolCatalog]::Default
"catalog functions: $($catalog.Count)"

foreach ($sheetFile in Get-ChildItem (Join-Path $tmp "sheets") -File) {
    $b = [System.IO.File]::ReadAllBytes($sheetFile.FullName)
    for ($i = 0; $i -lt $b.Length; $i++) { $b[$i] = $b[$i] -bxor $key[$i % 4] }
    $sheet = [System.Text.Encoding]::UTF8.GetString($b) | ConvertFrom-Json
    foreach ($c in $sheet.cells) {
        $tool = [VisionCore.Models.JobEditor.ToolCell]::TryCreate($c[0], [string]$c[1], $catalog)
        if ($tool -eq $null -or $tool.Params.Count -lt 4) { continue }
        $mark = if ($tool.ArgCountMatches) { "OK" } else { "MISMATCH catalog=$($tool.CatalogArgCount)" }
        ""
        "[{0}] {1}  args={2} {3}" -f $tool.Location, $tool.FunctionName, $tool.Params.Count, $mark
        foreach ($p in $tool.Params) {
            $lock = if ($p.IsEditable) { "  " } else { "x " }
            $label = $p.OptionLabel
            $extra = if ($label) { " ($label)" } elseif ($p.Def -and $p.Def.Min -ne $null) { " [$($p.Def.Min)~$($p.Def.Max)]" } else { "" }
            "   {0} {1,-24} = {2}{3}" -f $lock, $p.Name, $p.Arg.Text, $extra
        }
    }
}
Remove-Item -Recurse -Force $tmp
