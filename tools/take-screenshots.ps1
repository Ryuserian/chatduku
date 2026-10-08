# ChatDuku screenshot helper (README 미리보기용)
# 3종 캡처: 초기 / 리모컨 / OBS 하트비트 연결 끊김(빨간 배너)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$sig = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W32 {
  [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int awareness);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr p, IntPtr c, string cls, string win);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string win);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
Add-Type $sig
try { [void][W32]::SetProcessDpiAwareness(2) } catch { try { [void][W32]::SetProcessDPIAware() } catch {} }

# 기준 폴더 = 프로젝트 루트(ChatDuku/), 스크립트는 tools/ 에 위치
$base = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $base 'bin\Debug\net10.0-windows\ChatDuku.exe'
$docs = Join-Path $base 'docs'
if (-not (Test-Path $docs)) { New-Item -ItemType Directory -Path $docs | Out-Null }

# 하트비트 데모: 응답하지 않는 IP를 감시 대상으로 사전 설정 → 자동 Dead 판정 → 빨간 배너
$cfg = Join-Path (Split-Path $exe) 'appsettings.json'
@'
{
  "HeartbeatEnabled": true,
  "HeartbeatHosts": "192.168.0.250:4455"
}
'@ | Set-Content -Path $cfg -Encoding UTF8

Get-Process ChatDuku -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$script:TargetPid = 0
$script:Wins = New-Object System.Collections.ArrayList
$cb = [W32+EnumWindowsProc] {
    param($h, $lp)
    $wpid = [uint32]0
    [void][W32]::GetWindowThreadProcessId($h, [ref]$wpid)
    if ([int]$wpid -ne [int]$script:TargetPid) { return $true }
    if (-not [W32]::IsWindowVisible($h)) { return $true }
    $sb = New-Object System.Text.StringBuilder 256
    [void][W32]::GetWindowTextW($h, $sb, 256)
    $r = New-Object W32+RECT
    [void][W32]::GetWindowRect($h, [ref]$r)
    if ($r.Right -le $r.Left -or $r.Bottom -le $r.Top) { return $true }
    [void]$script:Wins.Add([pscustomobject]@{ Title = $sb.ToString(); L = [int]$r.Left; T = [int]$r.Top; Rt = [int]$r.Right; B = [int]$r.Bottom })
    return $true
}

function Get-WinRects($procId) {
    $script:TargetPid = [int]$procId
    $script:Wins = New-Object System.Collections.ArrayList
    [void][W32]::EnumWindows($cb, [IntPtr]::Zero)
    return @($script:Wins)
}

# 바탕화면 아이콘 숨김/복원 (투명 창 너머로 아이콘이 비치는 것 방지)
function Find-DefView {
    $d = [W32]::FindWindowExW([IntPtr]::Zero, [IntPtr]::Zero, 'SHELLDLL_DefView', $null)
    if ($d -ne [IntPtr]::Zero) { return $d }
    $prog = [W32]::FindWindowW('Progman', $null)
    if ($prog -ne [IntPtr]::Zero) {
        $d = [W32]::FindWindowExW($prog, [IntPtr]::Zero, 'SHELLDLL_DefView', $null)
        if ($d -ne [IntPtr]::Zero) { return $d }
    }
    $w = [IntPtr]::Zero
    while ($true) {
        $w = [W32]::FindWindowExW([IntPtr]::Zero, $w, 'WorkerW', $null)
        if ($w -eq [IntPtr]::Zero) { break }
        $d = [W32]::FindWindowExW($w, [IntPtr]::Zero, 'SHELLDLL_DefView', $null)
        if ($d -ne [IntPtr]::Zero) { return $d }
    }
    return [IntPtr]::Zero
}

$iconsHidden = $false
$defView = Find-DefView
if ($defView -ne [IntPtr]::Zero) {
    [void][W32]::SendMessageW($defView, 0x7878, [IntPtr]::Zero, [IntPtr]::Zero)  # 아이콘 표시 토글
    $iconsHidden = $true
    Start-Sleep -Milliseconds 500
}

function Capture-Region($rects, $outPath) {
    $L = [int](($rects | Measure-Object -Property L -Minimum).Minimum)
    $T = [int](($rects | Measure-Object -Property T -Minimum).Minimum)
    $R = [int](($rects | Measure-Object -Property Rt -Maximum).Maximum)
    $B = [int](($rects | Measure-Object -Property B -Maximum).Maximum)
    $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $L = [int][Math]::Max($L, $vs.X); $T = [int][Math]::Max($T, $vs.Y)
    $R = [int][Math]::Min($R, $vs.Right); $B = [int][Math]::Min($B, $vs.Bottom)
    $W = [int]($R - $L); $H = [int]($B - $T)
    if ($W -le 10 -or $H -le 10) { throw "region too small: ${W}x${H}" }
    $bmp = New-Object System.Drawing.Bitmap ([int]$W), ([int]$H)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($L, $T, 0, 0, [System.Drawing.Size]::new($W, $H))
    $g.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("saved " + (Split-Path $outPath -Leaf) + " " + $W + "x" + $H)
}

$env:CHATDUKU_SHOT = '1'
$p = Start-Process $exe -PassThru
Write-Output ("pid=" + $p.Id)

try {
    # ① 초기 상태 (t≈3초): 투명 창 + 안내 카드
    Start-Sleep -Seconds 3
    $r = Get-WinRects $p.Id
    # 창 너비로 구분: 메인≈900px / 리모컨≈300px (console 한글 매칭 인코딩 이슈 회피)
    $mainWin = @($r | Where-Object { ($_.Rt - $_.L) -ge 500 })
    if ($mainWin.Count -eq 0) { throw "main window missing: $($r.Title -join ' | ')" }
    Capture-Region $mainWin (Join-Path $docs 'screenshot-initial.png')

    # ② 리모컨 열림 (t≈10초): 툴바(t=5)+리모컨(t=8)
    Start-Sleep -Seconds 7
    $r = Get-WinRects $p.Id
    $remote = @($r | Where-Object { ($_.Rt - $_.L) -lt 500 })
    $mainWin = @($r | Where-Object { ($_.Rt - $_.L) -ge 500 })
    if ($remote.Count -eq 0 -or $mainWin.Count -eq 0) { throw "remote/main missing: $($r.Title -join ' | ')" }
    Capture-Region ($mainWin + $remote) (Join-Path $docs 'screenshot-remote.png')

    # ③ OBS 연결 끊김 (t≈30초): 감시 대상 3회 실패 → Dead → 빨간 배너
    Start-Sleep -Seconds 20
    $r = Get-WinRects $p.Id
    $remote = @($r | Where-Object { ($_.Rt - $_.L) -lt 500 })
    $mainWin = @($r | Where-Object { ($_.Rt - $_.L) -ge 500 })
    if ($mainWin.Count -eq 0) { throw "main missing at dead shot" }
    Capture-Region ($mainWin + $remote) (Join-Path $docs 'screenshot-heartbeat-dead.png')
}
finally {
    try { $p.Kill() } catch {}
    # 바탕화면 아이콘 복원
    if ($iconsHidden) {
        $dv = Find-DefView
        if ($dv -ne [IntPtr]::Zero) { [void][W32]::SendMessageW($dv, 0x7878, [IntPtr]::Zero, [IntPtr]::Zero) }
    }
    # 데모용 설정 되돌림
    if (Test-Path $cfg) { Remove-Item $cfg -Force }
}
Write-Output 'DONE'
