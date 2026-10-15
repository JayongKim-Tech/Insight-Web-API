# Builds Resources\ToolCatalog.json from the In-Sight Vision Suite help pages.
# Each spreadsheet function page has an "Inputs" table; this flattens it into argument order.
# Default: every installed In-Sight version, newest first. A function found in a newer version wins;
# functions that only exist in older versions are kept (cameras may run older firmware, e.g. ViDiELSegment -> EdgeAISegment).
# Usage: powershell -File GenerateToolCatalog.ps1 [-HelpRoot <...\Help_InSight\Content\Topics\Spreadsheet>, ...]
param(
    [string[]]$HelpRoot,
    [string]$OutFile = (Join-Path $PSScriptRoot "..\..\Resources\ToolCatalog.json")
)

if (-not $HelpRoot) {
    $base = "C:\Program Files\Cognex\In-Sight\In-Sight Vision Suite Editors\In-Sight"
    $HelpRoot = Get-ChildItem $base -Directory -Filter "In-Sight *" |
        Sort-Object { [version]($_.Name -replace '^In-Sight\s+', '') } -Descending |
        ForEach-Object { Join-Path $_.FullName "Docs\Help_InSight\Content\Topics\Spreadsheet" } |
        Where-Object { Test-Path $_ }
}
"help roots:"; $HelpRoot | ForEach-Object { "  $_" }

Add-Type -Path (Join-Path $PSScriptRoot "HelpTable.cs"), (Join-Path $PSScriptRoot "CatalogBuilder.cs") -ReferencedAssemblies System.Core
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer

$functions = [ordered]@{}
foreach ($f in ($HelpRoot | ForEach-Object { Get-ChildItem $_ -Recurse -Filter *.htm })) {
    $html = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    $title = [HelpTable]::Title($html)
    if (-not $title -or $title -notmatch '^[A-Za-z][A-Za-z0-9_]*$') { continue }
    if ($functions.get_Keys() -contains $title) { continue }
    $rows = [HelpTable]::ParseInputs($html)
    if (-not $rows) { continue }
    $list = [CatalogBuilder]::Build($rows)
    if ($list.Count -eq 0) { continue }
    $functions[$title] = $list
}

# One function per line so diffs stay readable after a firmware/help update.
$lines = New-Object System.Collections.Generic.List[string]
foreach ($name in ($functions.get_Keys() | Sort-Object)) {
    $argList = @()
    foreach ($a in $functions[$name]) {
        $o = [ordered]@{ n = $a.name; k = $a.kind }
        if ($a.group) { $o.g = $a.group }
        if ($a.min -ne $null) { $o.min = $a.min }
        if ($a.max -ne $null) { $o.max = $a.max }
        if ($a.def) { $o.def = $a.def }
        if ($a.options) { $o.opt = @($a.options | ForEach-Object { ,@($_[0], $_[1]) }) }
        if ($a.desc) { $o.d = $a.desc }
        $argList += $o
    }
    $lines.Add('  "' + $name + '": ' + $ser.Serialize($argList))
}
$json = "{`r`n" + ($lines -join ",`r`n") + "`r`n}`r`n"
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutFile), $json, (New-Object System.Text.UTF8Encoding($false)))
"functions: $($functions.get_Count()) -> $([System.IO.Path]::GetFullPath($OutFile))"
