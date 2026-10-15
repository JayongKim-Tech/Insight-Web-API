# Puts template cells on the clipboard in In-Sight's cell format.
# -Part Block : select K21   -Part Q2 : select Q2   -Part Coef : select W18   -Part Out : select O28
# -Part OriginTable : select J18   -Part OriginSelect : select P25
# Do not copy anything else between running this and pasting.
param([ValidateSet('Block', 'Q2', 'Coef', 'Out', 'OriginTable', 'OriginSelect')][string]$Part = 'Block')
Add-Type -AssemblyName System.Windows.Forms
$name = @{ Block = 'maps_block.bin'; Q2 = 'maps_q2.bin'; Coef = 'calib_coef.bin'; Out = 'calib_out.bin'
           OriginTable = 'origin_table.bin'; OriginSelect = 'origin_select.bin' }[$Part]
$bytes = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $name))
$data = New-Object System.Windows.Forms.DataObject
$data.SetData('Cognex.InSight.Internal.CvsCellClipboardData', (New-Object IO.MemoryStream(, $bytes)))
[System.Windows.Forms.Clipboard]::SetDataObject($data, $true)
Write-Host "$Part copied ($($bytes.Length) bytes)"
