# Builds Resources\ToolDesc.ko.json = { "<English description from ToolCatalog.json>": "<Korean>" }
# Sources: ko\_en.tsv (id <TAB> function.param <TAB> English) + ko\NNN.tsv (id <TAB> Korean).
# After regenerating ToolCatalog.json, run with -ExportMissing to list descriptions that still need Korean
# (written to ko\_missing.tsv; translate into a new ko\NNN.tsv with ids continuing from the last one).
param(
    [switch]$ExportMissing,
    [string]$OutFile = (Join-Path $PSScriptRoot "..\..\Resources\ToolDesc.ko.json")
)

$dir = Join-Path $PSScriptRoot "ko"
$utf8 = New-Object System.Text.UTF8Encoding($false)

$en = @{}
foreach ($line in [IO.File]::ReadAllLines((Join-Path $dir "_en.tsv"), $utf8)) {
    $p = $line.Split("`t")
    if ($p.Length -ge 3) { $en[$p[0]] = $p[2] }
}

$map = [ordered]@{}
foreach ($f in Get-ChildItem $dir -Filter "*.tsv" | Where-Object { $_.Name -match '^\d+\.tsv$' } | Sort-Object Name) {
    foreach ($line in [IO.File]::ReadAllLines($f.FullName, $utf8)) {
        $p = $line.Split("`t", 2)
        if ($p.Length -lt 2 -or -not $en.ContainsKey($p[0])) { continue }
        $map[$en[$p[0]]] = $p[1].Trim()
    }
}

Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$ser.MaxJsonLength = 50MB
$lines = New-Object System.Collections.Generic.List[string]
foreach ($k in $map.Keys) { $lines.Add("  " + $ser.Serialize($k) + ": " + $ser.Serialize($map[$k])) }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutFile), "{`r`n" + ($lines -join ",`r`n") + "`r`n}`r`n", $utf8)
"translated: $($map.Count) -> $([IO.Path]::GetFullPath($OutFile))"

if ($ExportMissing) {
    $catalog = Get-Content (Join-Path $PSScriptRoot "..\..\Resources\ToolCatalog.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $next = ($en.Keys | ForEach-Object { [int]$_ } | Measure-Object -Maximum).Maximum + 1
    $missing = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    foreach ($fn in $catalog.PSObject.Properties.Name) {
        foreach ($a in $catalog.$fn) {
            if (-not $a.d -or $map.Contains($a.d) -or $seen.ContainsKey($a.d)) { continue }
            $seen[$a.d] = 1
            $name = if ($a.g) { "$($a.g).$($a.n)" } else { $a.n }
            $missing.Add("$next`t$fn.$name`t$($a.d)"); $next++
        }
    }
    [IO.File]::WriteAllLines((Join-Path $dir "_missing.tsv"), $missing, $utf8)
    "missing: $($missing.Count) -> ko\_missing.tsv (append these lines to ko\_en.tsv after translating)"
}
