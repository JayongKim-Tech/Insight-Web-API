# Collects real spreadsheet function calls from the job snippets/templates embedded in In-Sight Vision Suite DLLs
# (ResourceData DLLs contain sample sheets as JSON: ["K5","FindLine($O$5,0,0,0,...)",...]).
# Output: function -> argument counts seen + one example. Used to verify ToolCatalog.json argument counts.
# Usage: powershell -File ExtractSamples.ps1 [-InstallDir <...\In-Sight 26.2.0>] [-OutFile samples.json]
param(
    [string[]]$InstallDir,
    [string]$OutFile = (Join-Path $PSScriptRoot "samples.json")
)

if (-not $InstallDir) {
    $base = "C:\Program Files\Cognex\In-Sight\In-Sight Vision Suite Editors\In-Sight"
    $InstallDir = Get-ChildItem $base -Directory -Filter "In-Sight *" | ForEach-Object FullName
}

$cellRe = [regex]'\["[A-Z]{1,2}\d{1,3}","((?:[^"\\]|\\.)*)"'
$samples = @{}

function Split-Call([string]$expr) {
    # Returns @(name, argCount) when the whole expression is one function call, else $null
    $m = [regex]::Match($expr, '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(')
    if (-not $m.Success) { return $null }
    $depth = 0; $inStr = $false; $count = 1; $empty = $true
    for ($i = $m.Length; $i -lt $expr.Length; $i++) {
        $c = $expr[$i]
        if ($inStr) { if ($c -eq '"') { $inStr = $false }; continue }
        if ($c -eq '"') { $inStr = $true; $empty = $false; continue }
        if ($c -eq '(' -or $c -eq '{' -or $c -eq '[') { $depth++; $empty = $false; continue }
        if ($c -eq ')' -or $c -eq '}' -or $c -eq ']') {
            if ($depth -eq 0) {
                if ($expr.Substring($i + 1).Trim().Length -ne 0) { return $null }   # e.g. Foo(1)+2
                if ($empty) { $count = 0 }
                return @($m.Groups[1].Value, $count)
            }
            $depth--; continue
        }
        if ($c -eq ',' -and $depth -eq 0) { $count++; continue }
        if (-not [char]::IsWhiteSpace($c)) { $empty = $false }
    }
    return $null
}

foreach ($dir in $InstallDir) {
    foreach ($f in Get-ChildItem $dir -Filter *.dll -File) {
        $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($f.FullName))
        if ($text.IndexOf('["A0"', [StringComparison]::Ordinal) -lt 0 -and $text.IndexOf('$A$0', [StringComparison]::Ordinal) -lt 0) { continue }
        foreach ($m in $cellRe.Matches($text)) {
            $expr = $m.Groups[1].Value -replace '\\"', '"'
            $call = Split-Call $expr
            if (-not $call) { continue }
            $name = $call[0]; $n = $call[1]
            if (-not $samples.ContainsKey($name)) { $samples[$name] = @{ counts = @{}; example = $expr } }
            $samples[$name].counts["$n"] = 1 + [int]$samples[$name].counts["$n"]
        }
    }
}

$out = [ordered]@{}
foreach ($k in ($samples.Keys | Sort-Object)) {
    $out[$k] = [ordered]@{ counts = $samples[$k].counts; example = $samples[$k].example }
}
$out | ConvertTo-Json -Depth 4 | Set-Content -Path $OutFile -Encoding UTF8
"functions with samples: $($out.Count) -> $OutFile"
