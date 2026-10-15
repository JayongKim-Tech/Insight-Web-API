# Saves the raw In-Sight cell clipboard data (copy cells in In-Sight first) to clip_raw.bin.
$src = @'
using System; using System.Runtime.InteropServices;
public static class ClipRaw {
 [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr h);
 [DllImport("user32.dll")] static extern bool CloseClipboard();
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint RegisterClipboardFormat(string s);
 [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint f);
 [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
 [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
 [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr h);
 public static byte[] Read(string name) {
  uint f = RegisterClipboardFormat(name);
  if (!OpenClipboard(IntPtr.Zero)) return null;
  try {
   IntPtr h = GetClipboardData(f); if (h == IntPtr.Zero) return null;
   IntPtr p = GlobalLock(h); int n = (int)GlobalSize(h).ToUInt64();
   byte[] b = new byte[n]; Marshal.Copy(p, b, 0, n); GlobalUnlock(h); return b;
  } finally { CloseClipboard(); }
 }
}
'@
Add-Type -TypeDefinition $src
$b = [ClipRaw]::Read('Cognex.InSight.Internal.CvsCellClipboardData')
if ($b -eq $null) { Write-Host 'No In-Sight cell data on clipboard'; exit 1 }
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'clip_raw.bin'), $b)
Write-Host "Saved clip_raw.bin ($($b.Length) bytes)"
