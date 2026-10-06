<#
    NetSwitch — быстрое переключение IPv4-настроек сетевого адаптера:
    «Автоматически (DHCP)» <-> профили с ручными IP / маской / шлюзом / DNS.

    Запуск:
      NetSwitch.bat                                   — окно с профилями
      NetSwitch.bat -Profile "Кабинет"                — применить профиль без окна
      NetSwitch.bat -Profile DHCP -Adapter "Ethernet" — вернуть автонастройку
      NetSwitch.bat -Proxy On | Off | Toggle          — системный прокси без окна

    Профили хранятся в profiles.json рядом со скриптом (создаётся при первом запуске).
    Нужны права администратора — скрипт сам запросит их через UAC
    (кроме запуска только с -Proxy: прокси — настройка пользователя, ей админ не нужен).
#>
[CmdletBinding()]
param(
    [Alias('Profile')][string]$ProfileName,
    [string]$Adapter,
    [string]$Proxy
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, Microsoft.VisualBasic

$ScriptPath = $PSCommandPath
$ScriptDir  = $PSScriptRoot
$ConfigPath = Join-Path $ScriptDir 'profiles.json'
$DhcpNames  = @('DHCP', 'auto', 'авто')

# ---------- Права администратора ----------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$needAdmin = $ProfileName -or -not $Proxy
if ($needAdmin -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$ScriptPath`"")
    if ($ProfileName) { $argList += @('-Profile', "`"$ProfileName`"") }
    if ($Adapter)     { $argList += @('-Adapter', "`"$Adapter`"") }
    if ($Proxy)       { $argList += @('-Proxy', "`"$Proxy`"") }
    try { Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList $argList } catch { }
    exit
}

# ---------- Конфиг ----------
function Save-Config($cfg) {
    $cfg | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
}

function Get-Config {
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        Save-Config ([pscustomobject]@{
            DefaultAdapter = ''
            Profiles       = @(
                [pscustomobject]@{ Name = 'Пример'; IP = '192.168.1.50'; Mask = '255.255.255.0'; Gateway = '192.168.1.1'; DNS = @('192.168.1.1', '8.8.8.8') }
            )
        })
    }
    $cfg = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $cfg.PSObject.Properties['DefaultAdapter']) { $cfg | Add-Member -NotePropertyName DefaultAdapter -NotePropertyValue '' }
    if (-not $cfg.PSObject.Properties['Profiles'])       { $cfg | Add-Member -NotePropertyName Profiles -NotePropertyValue @() }
    $cfg.Profiles = @($cfg.Profiles | Where-Object { $_ })
    $cfg
}

# ---------- Проверка адресов ----------
function Test-IPv4([string]$s) {
    if ($s -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { return $false }
    foreach ($o in $s.Split('.')) { if ([int]$o -gt 255) { return $false } }
    $true
}

function ConvertTo-Mask([int]$Prefix) {
    $bits = ('1' * $Prefix).PadRight(32, '0')
    (0..3 | ForEach-Object { [Convert]::ToByte($bits.Substring($_ * 8, 8), 2) }) -join '.'
}

function Resolve-Mask([string]$s) {
    $s = "$s".Trim()
    if ($s -match '^/?(\d{1,2})$' -and [int]$Matches[1] -le 32) { return ConvertTo-Mask ([int]$Matches[1]) }
    if (Test-IPv4 $s) {
        $bin = ($s.Split('.') | ForEach-Object { [Convert]::ToString([int]$_, 2).PadLeft(8, '0') }) -join ''
        if ($bin -match '^1*0*$') { return $s }
    }
    throw "Неверная маска: '$s' (пример: 255.255.255.0 или 24)"
}

function Get-ValidatedSettings($IP, $Mask, $Gateway, $Dns1, $Dns2) {
    $IP = "$IP".Trim(); $Gateway = "$Gateway".Trim()
    if (-not (Test-IPv4 $IP)) { throw "Неверный IP-адрес: '$IP'" }
    $Mask = Resolve-Mask $Mask
    if ($Gateway -and -not (Test-IPv4 $Gateway)) { throw "Неверный шлюз: '$Gateway'" }
    $dns = @()
    foreach ($d in @($Dns1, $Dns2)) {
        $d = "$d".Trim()
        if ($d) {
            if (-not (Test-IPv4 $d)) { throw "Неверный DNS: '$d'" }
            $dns += $d
        }
    }
    [pscustomobject]@{ IP = $IP; Mask = $Mask; Gateway = $Gateway; DNS = $dns }
}

# ---------- Работа с адаптером ----------
function Invoke-Netsh([string[]]$NetshArgs) {
    $ErrorActionPreference = 'Continue'
    $out = & netsh.exe @NetshArgs 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw ("netsh: " + $out.Trim()) }
}

function Clear-PersistentGateway([int]$Index) {
    Get-NetRoute -InterfaceIndex $Index -DestinationPrefix '0.0.0.0/0' -PolicyStore PersistentStore -ErrorAction SilentlyContinue |
        Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue
}

function Set-AdapterDhcp([int]$Index) {
    Clear-PersistentGateway $Index
    $ipif = Get-NetIPInterface -InterfaceIndex $Index -AddressFamily IPv4
    if ($ipif.Dhcp -ne 'Enabled') {
        Invoke-Netsh @('interface', 'ipv4', 'set', 'address', "name=$Index", 'source=dhcp')
    }
    Set-DnsClientServerAddress -InterfaceIndex $Index -ResetServerAddresses
}

function Set-AdapterStatic([int]$Index, [string]$IP, [string]$Mask, [string]$Gateway, [string[]]$Dns) {
    Clear-PersistentGateway $Index
    $gwArg = if ($Gateway) { "gateway=$Gateway" } else { 'gateway=none' }
    Invoke-Netsh @('interface', 'ipv4', 'set', 'address', "name=$Index", 'source=static', "address=$IP", "mask=$Mask", $gwArg)
    $Dns = @($Dns | Where-Object { $_ })
    if ($Dns.Count -gt 0) { Set-DnsClientServerAddress -InterfaceIndex $Index -ServerAddresses $Dns }
    else                  { Set-DnsClientServerAddress -InterfaceIndex $Index -ResetServerAddresses }
}

function Get-AdapterInfo([int]$Index) {
    $ipif  = Get-NetIPInterface -InterfaceIndex $Index -AddressFamily IPv4 -ErrorAction SilentlyContinue
    $addrs = @(Get-NetIPAddress -InterfaceIndex $Index -AddressFamily IPv4 -ErrorAction SilentlyContinue)
    $gws   = @(Get-NetRoute -InterfaceIndex $Index -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
               Select-Object -ExpandProperty NextHop -Unique)
    $dnsObj = Get-DnsClientServerAddress -InterfaceIndex $Index -AddressFamily IPv4 -ErrorAction SilentlyContinue
    $dns = @(); if ($dnsObj) { $dns = @($dnsObj.ServerAddresses | Where-Object { $_ }) }
    [pscustomobject]@{
        HasIPv4   = [bool]$ipif
        Dhcp      = [bool]($ipif -and $ipif.Dhcp -eq 'Enabled')
        Addresses = $addrs
        Gateways  = $gws
        Dns       = $dns
    }
}

function Format-AdapterInfo($AdapterObj, $Info) {
    $lines = @("Состояние: $($AdapterObj.Status)   ($($AdapterObj.InterfaceDescription))")
    if (-not $Info.HasIPv4) { $lines += 'IPv4 на этом адаптере недоступен (адаптер отключён?)'; return $lines -join "`r`n" }
    $lines += 'Режим:     ' + $(if ($Info.Dhcp) { 'автоматически (DHCP)' } else { 'вручную' })
    if ($Info.Addresses.Count) {
        foreach ($a in $Info.Addresses) { $lines += "IP:        $($a.IPAddress)/$($a.PrefixLength)   (маска $(ConvertTo-Mask $a.PrefixLength))" }
    } else { $lines += 'IP:        —' }
    $lines += 'Шлюз:      ' + $(if ($Info.Gateways.Count) { $Info.Gateways -join ', ' } else { '—' })
    $lines += 'DNS:       ' + $(if ($Info.Dns.Count) { $Info.Dns -join ', ' } else { '—' })
    $lines -join "`r`n"
}

function Show-Message([string]$Text, [string]$Icon = 'Information') {
    [void][System.Windows.Forms.MessageBox]::Show($Text, 'NetSwitch', 'OK', $Icon)
}

# ---------- Системный прокси ----------
# Ручной прокси WinINet (Параметры → Сеть и Интернет → Прокси) текущего пользователя.
# Читаем из реестра (быстро), меняем через InternetSetOption: так обновляется и
# DefaultConnectionSettings, и запущенные программы сразу узнают о смене.
$InetSettingsKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'

$ProxyApiSource = @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class NetSwitchProxy {
    const int OptionRefresh = 37, OptionSettingsChanged = 39, OptionPerConnection = 75;
    const int ConnFlags = 1, ConnProxyServer = 2, ConnProxyBypass = 3;
    const int ProxyTypeDirect = 1, ProxyTypeProxy = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct FileTime { public int Low, High; }

    [StructLayout(LayoutKind.Explicit)]
    struct OptionValue {
        [FieldOffset(0)] public int Int;
        [FieldOffset(0)] public IntPtr Ptr;
        [FieldOffset(0)] public FileTime Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Option { public int Id; public OptionValue Value; }

    [StructLayout(LayoutKind.Sequential)]
    struct OptionList { public int Size; public IntPtr Connection; public int Count; public int Error; public IntPtr Options; }

    [DllImport("wininet.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int length);

    [DllImport("wininet.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool InternetQueryOption(IntPtr handle, int option, IntPtr buffer, ref int length);

    // Connection = NULL — настройки локальной сети (Ethernet / Wi-Fi)
    static void Call(Option[] opts, bool query) {
        int optSize = Marshal.SizeOf(typeof(Option));
        int listSize = Marshal.SizeOf(typeof(OptionList));
        IntPtr optPtr = Marshal.AllocHGlobal(optSize * opts.Length);
        IntPtr listPtr = Marshal.AllocHGlobal(listSize);
        try {
            for (int i = 0; i < opts.Length; i++)
                Marshal.StructureToPtr(opts[i], new IntPtr(optPtr.ToInt64() + i * optSize), false);
            var list = new OptionList { Size = listSize, Count = opts.Length, Options = optPtr };
            Marshal.StructureToPtr(list, listPtr, false);
            int len = listSize;
            bool ok = query
                ? InternetQueryOption(IntPtr.Zero, OptionPerConnection, listPtr, ref len)
                : InternetSetOption(IntPtr.Zero, OptionPerConnection, listPtr, len);
            if (!ok) throw new Win32Exception();
            if (query)
                for (int i = 0; i < opts.Length; i++)
                    opts[i] = (Option)Marshal.PtrToStructure(new IntPtr(optPtr.ToInt64() + i * optSize), typeof(Option));
        } finally {
            Marshal.FreeHGlobal(listPtr);
            Marshal.FreeHGlobal(optPtr);
        }
    }

    public static void Set(bool enable) { Set(enable, null, null); }

    // server / bypass = null — оставить как есть. Автоопределение и PAC-скрипт не трогаем.
    public static void Set(bool enable, string server, string bypass) {
        var cur = new[] { new Option { Id = ConnFlags } };
        Call(cur, true);
        int flags = (cur[0].Value.Int & ~ProxyTypeProxy) | ProxyTypeDirect | (enable ? ProxyTypeProxy : 0);

        var opts = new List<Option> { new Option { Id = ConnFlags, Value = new OptionValue { Int = flags } } };
        var strings = new List<IntPtr>();
        try {
            if (server != null) {
                strings.Add(Marshal.StringToHGlobalUni(server));
                opts.Add(new Option { Id = ConnProxyServer, Value = new OptionValue { Ptr = strings[strings.Count - 1] } });
            }
            if (bypass != null) {
                strings.Add(Marshal.StringToHGlobalUni(bypass));
                opts.Add(new Option { Id = ConnProxyBypass, Value = new OptionValue { Ptr = strings[strings.Count - 1] } });
            }
            Call(opts.ToArray(), false);
        } finally {
            foreach (IntPtr p in strings) Marshal.FreeHGlobal(p);
        }
        InternetSetOption(IntPtr.Zero, OptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, OptionRefresh, IntPtr.Zero, 0);
    }
}
'@

function Get-ProxyState {
    $p = Get-ItemProperty -LiteralPath $InetSettingsKey -ErrorAction SilentlyContinue
    [pscustomobject]@{
        Enabled = [bool]($p -and $p.ProxyEnable -eq 1)
        Server  = if ($p) { [string]$p.ProxyServer } else { '' }
        Bypass  = if ($p) { [string]$p.ProxyOverride } else { '' }
    }
}

# Без $Server — только включить/выключить, адрес и исключения остаются прежними
function Set-SystemProxy([bool]$Enable, $Server, $Bypass) {
    if (-not ('NetSwitchProxy' -as [type])) { Add-Type -TypeDefinition $ProxyApiSource }
    if ($null -eq $Server) { [NetSwitchProxy]::Set($Enable) }
    else                   { [NetSwitchProxy]::Set($Enable, [string]$Server, [string]$Bypass) }
}

function Resolve-ProxyServer([string]$s) {
    $s = "$s".Trim()
    if (-not $s) { throw 'Не указан адрес прокси (пример: 127.0.0.1:8080)' }
    # «http=host:port;https=host:port» и «socks=host:port» — как в Windows, проверяем только пробелы
    $ok = $s -notmatch '\s' -and (
        $s -match '[=;]' -or
        ($s -match '^(?:[a-z]+://)?(?:\[[0-9a-f:.]+\]|[^:/\[\]]+):(\d{1,5})$' -and [int]$Matches[1] -in 1..65535))
    if (-not $ok) { throw "Неверный адрес прокси: '$s' (нужно адрес:порт, например 127.0.0.1:8080)" }
    $s
}

# Исключения: «<local>» — флажок «Не использовать для локальных адресов», остальное сохраняем как было
function Join-ProxyBypass([string]$Current, [bool]$Local) {
    $items = @("$Current".Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -ne '<local>' })
    if ($Local) { $items += '<local>' }
    $items -join ';'
}

function Invoke-ProxyCommand([string]$Action) {
    $st = Get-ProxyState
    $on = switch -Regex ($Action.Trim()) {
        '^on$'     { $true }
        '^off$'    { $false }
        '^toggle$' { -not $st.Enabled }
        default    { throw "Неизвестное значение -Proxy: '$Action' (нужно On, Off или Toggle)" }
    }
    if ($on -and -not $st.Server) { throw 'Адрес прокси не задан. Один раз включи прокси из окна NetSwitch — адрес запомнится.' }
    Set-SystemProxy $on
    if ($on) { "Прокси включён: $($st.Server)" } else { 'Прокси выключен' }
}

function Invoke-ProfileCommand {
    $cfg = Get-Config
    $adName = if ($Adapter) { $Adapter } else { $cfg.DefaultAdapter }
    if (-not $adName) { throw 'Не указан адаптер. Добавь -Adapter "Имя" или один раз примени профиль из окна — адаптер запомнится.' }
    $ad = Get-NetAdapter -Name $adName -ErrorAction SilentlyContinue
    if (-not $ad) { throw "Адаптер '$adName' не найден." }

    if ($ProfileName -in $DhcpNames) {
        Set-AdapterDhcp $ad.ifIndex
        $what = 'автоматически (DHCP)'
    } else {
        $p = @($cfg.Profiles) | Where-Object { $_.Name -eq $ProfileName } | Select-Object -First 1
        if (-not $p) { throw "Профиль '$ProfileName' не найден в profiles.json." }
        $d = @($p.DNS)
        $s = Get-ValidatedSettings $p.IP $p.Mask $p.Gateway $d[0] $d[1]
        Set-AdapterStatic $ad.ifIndex $s.IP $s.Mask $s.Gateway $s.DNS
        $what = "профиль «$($p.Name)» ($($s.IP))"
    }
    "$($ad.Name): $what"
}

# ---------- Режим без окна (для ярлыков) ----------
if ($ProfileName -or $Proxy) {
    try {
        $done = @()
        if ($ProfileName) { $done += Invoke-ProfileCommand }
        if ($Proxy)       { $done += Invoke-ProxyCommand $Proxy }
        Show-Message ($done -join "`r`n")
    } catch {
        Show-Message $_.Exception.Message 'Error'
    }
    exit
}

# ---------- Окно ----------
[System.Windows.Forms.Application]::EnableVisualStyles()
$script:cfg = Get-Config
$script:adapters = @()
$script:currentInfo = $null

function New-Ctl([string]$Type, [int]$X, [int]$Y, [int]$W, [int]$H, $Text = $null) {
    $c = New-Object "System.Windows.Forms.$Type"
    $c.Location = New-Object System.Drawing.Point($X, $Y)
    $c.Size = New-Object System.Drawing.Size($W, $H)
    if ($null -ne $Text) { $c.Text = $Text }
    $c
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'NetSwitch — настройки сети'
$form.ClientSize = New-Object System.Drawing.Size(560, 580)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

# Адаптер
$lblAdapter = New-Ctl Label 12 17 60 20 'Адаптер:'
$cbAdapter  = New-Ctl ComboBox 75 13 370 24
$cbAdapter.DropDownStyle = 'DropDownList'
$btnRefresh = New-Ctl Button 455 12 93 26 'Обновить'
$form.Controls.AddRange(@($lblAdapter, $cbAdapter, $btnRefresh))

# Текущие настройки
$grpCurrent = New-Ctl GroupBox 12 48 536 122 'Сейчас'
$txtCurrent = New-Ctl TextBox 10 20 516 92
$txtCurrent.Multiline = $true
$txtCurrent.ReadOnly = $true
$txtCurrent.BackColor = [System.Drawing.SystemColors]::Window
$txtCurrent.Font = New-Object System.Drawing.Font('Consolas', 9)
$grpCurrent.Controls.Add($txtCurrent)
$form.Controls.Add($grpCurrent)

# Профили
$grpProfiles = New-Ctl GroupBox 12 178 536 252 'Профили'
$lbProfiles  = New-Ctl ListBox 10 22 190 220
$lbProfiles.IntegralHeight = $false
$grpProfiles.Controls.Add($lbProfiles)

$labels = @('IP-адрес:', 'Маска:', 'Шлюз:', 'DNS 1:', 'DNS 2:')
$boxes = @()
for ($k = 0; $k -lt $labels.Count; $k++) {
    $y = 24 + $k * 30
    $grpProfiles.Controls.Add((New-Ctl Label 215 ($y + 3) 75 20 $labels[$k]))
    $tb = New-Ctl TextBox 292 $y 230 23
    $grpProfiles.Controls.Add($tb)
    $boxes += $tb
}
$tbIP, $tbMask, $tbGw, $tbDns1, $tbDns2 = $boxes

$btnTake     = New-Ctl Button 215 176 150 28 'Взять текущие'
$btnSave     = New-Ctl Button 372 176 150 28 'Сохранить как…'
$btnDelete   = New-Ctl Button 215 210 150 28 'Удалить профиль'
$btnShortcut = New-Ctl Button 372 210 150 28 'Ярлык на рабочий стол'
$grpProfiles.Controls.AddRange(@($btnTake, $btnSave, $btnDelete, $btnShortcut))
$form.Controls.Add($grpProfiles)

# Системный прокси
$grpProxy      = New-Ctl GroupBox 12 438 536 90 'Системный прокси'
$lblProxyAddr  = New-Ctl Label 10 27 60 20 'Адрес:'
$tbProxy       = New-Ctl TextBox 75 24 180 23
$chkProxyLocal = New-Ctl CheckBox 268 25 258 22 'Не использовать для локальных адресов'
$lblProxyState = New-Ctl Label 10 61 250 20
$lblProxyState.AutoEllipsis = $true
$btnProxyOn    = New-Ctl Button 268 54 82 28 'Включить'
$btnProxyOff   = New-Ctl Button 356 54 82 28 'Выключить'
$btnProxyLnk   = New-Ctl Button 444 54 82 28 'Ярлык'
$grpProxy.Controls.AddRange(@($lblProxyAddr, $tbProxy, $chkProxyLocal, $lblProxyState, $btnProxyOn, $btnProxyOff, $btnProxyLnk))
$form.Controls.Add($grpProxy)

$tips = New-Object System.Windows.Forms.ToolTip
$tips.SetToolTip($tbProxy, 'адрес:порт, например 127.0.0.1:8080')
$tips.SetToolTip($btnProxyLnk, 'Ярлык на рабочий стол: включает / выключает прокси без окна и без UAC')

# Низ окна
$btnApply = New-Ctl Button 12 536 160 34 'Применить'
$btnApply.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$lblStatus = New-Ctl Label 185 545 363 20 'Двойной щелчок по профилю — применить сразу'
$lblStatus.AutoEllipsis = $true
$lblStatus.ForeColor = 'DimGray'
$form.Controls.AddRange(@($btnApply, $lblStatus))
$form.AcceptButton = $btnApply

# Индексы в списке: 0 — DHCP, 1 — разовая ручная настройка, 2+ — профили
function Set-Status([string]$Text, [string]$Color = 'DarkGreen') {
    $lblStatus.ForeColor = $Color
    $lblStatus.Text = $Text
}

function Get-SelectedAdapter {
    $i = $cbAdapter.SelectedIndex
    if ($i -lt 0) { return $null }
    $script:adapters[$i]
}

function Update-AdapterList {
    $keep = if ($cbAdapter.SelectedIndex -ge 0) { $script:adapters[$cbAdapter.SelectedIndex].Name } else { $script:cfg.DefaultAdapter }
    $script:adapters = @(Get-NetAdapter | Sort-Object @{ Expression = { $_.Status -ne 'Up' } }, Name)
    $cbAdapter.Items.Clear()
    foreach ($a in $script:adapters) { [void]$cbAdapter.Items.Add("$($a.Name)   [$($a.Status)]") }
    $idx = [array]::IndexOf(@($script:adapters | ForEach-Object { $_.Name }), $keep)
    if ($idx -lt 0 -and $cbAdapter.Items.Count -gt 0) { $idx = 0 }
    $cbAdapter.SelectedIndex = $idx
}

function Update-Current {
    $sel = Get-SelectedAdapter
    $a = if ($sel) { Get-NetAdapter -Name $sel.Name -ErrorAction SilentlyContinue }
    if (-not $a) { $script:currentInfo = $null; $txtCurrent.Text = 'Адаптер не найден. Нажми «Обновить».'; return }
    $script:currentInfo = Get-AdapterInfo $a.ifIndex
    $txtCurrent.Text = Format-AdapterInfo $a $script:currentInfo
}

function Update-ProfileList([int]$Select = 0) {
    $lbProfiles.Items.Clear()
    [void]$lbProfiles.Items.Add('Автоматически (DHCP)')
    [void]$lbProfiles.Items.Add('Вручную (без профиля)')
    foreach ($p in @($script:cfg.Profiles)) { [void]$lbProfiles.Items.Add([string]$p.Name) }
    if ($Select -ge $lbProfiles.Items.Count) { $Select = $lbProfiles.Items.Count - 1 }
    $lbProfiles.SelectedIndex = $Select
}

function Show-SelectedProfile {
    $i = $lbProfiles.SelectedIndex
    $manual = $i -ge 1
    foreach ($t in $boxes) { $t.Enabled = $manual; $t.Text = '' }
    $btnDelete.Enabled = $i -ge 2
    $btnShortcut.Enabled = $i -ne 1
    if ($i -ge 2) {
        $p = @($script:cfg.Profiles)[$i - 2]
        $d = @($p.DNS)
        $tbIP.Text = $p.IP; $tbMask.Text = $p.Mask; $tbGw.Text = $p.Gateway; $tbDns1.Text = $d[0]; $tbDns2.Text = $d[1]
    }
}

function Copy-Current([switch]$Quiet) {
    $info = $script:currentInfo
    if (-not $info -or -not $info.Addresses.Count) {
        if (-not $Quiet) { Show-Message 'У адаптера сейчас нет IPv4-адреса.' 'Warning' }
        return
    }
    if ($lbProfiles.SelectedIndex -lt 1) { $lbProfiles.SelectedIndex = 1 }
    $a = $info.Addresses | Where-Object { $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1
    if (-not $a) { $a = $info.Addresses[0] }
    $tbIP.Text   = $a.IPAddress
    $tbMask.Text = ConvertTo-Mask $a.PrefixLength
    $tbGw.Text   = @($info.Gateways)[0]
    $tbDns1.Text = @($info.Dns)[0]
    $tbDns2.Text = @($info.Dns)[1]
}

function Select-MatchingProfile {
    $info = $script:currentInfo
    if (-not $info -or $info.Dhcp) { $lbProfiles.SelectedIndex = 0; return }
    $ips = @($info.Addresses | ForEach-Object { $_.IPAddress })
    $list = @($script:cfg.Profiles)
    for ($k = 0; $k -lt $list.Count; $k++) {
        if ($ips -contains $list[$k].IP) { $lbProfiles.SelectedIndex = $k + 2; return }
    }
    $lbProfiles.SelectedIndex = 1
    Copy-Current -Quiet
}

function Invoke-Apply {
    $a = Get-SelectedAdapter
    if (-not $a) { return }
    $i = $lbProfiles.SelectedIndex
    $form.Cursor = 'WaitCursor'
    try {
        if ($i -eq 0) {
            Set-AdapterDhcp $a.ifIndex
            $msg = 'включён DHCP (адрес может появиться через пару секунд)'
        } else {
            $s = Get-ValidatedSettings $tbIP.Text $tbMask.Text $tbGw.Text $tbDns1.Text $tbDns2.Text
            Set-AdapterStatic $a.ifIndex $s.IP $s.Mask $s.Gateway $s.DNS
            $msg = "применено $($s.IP) / $($s.Mask)"
        }
        $script:cfg.DefaultAdapter = $a.Name
        Save-Config $script:cfg
        Set-Status "$($a.Name): $msg"
        Start-Sleep -Milliseconds 1500
        Update-Current
    } catch {
        Set-Status 'Ошибка — см. сообщение' 'Firebrick'
        Show-Message $_.Exception.Message 'Error'
    } finally {
        $form.Cursor = 'Default'
    }
}

function Save-AsProfile {
    try { $s = Get-ValidatedSettings $tbIP.Text $tbMask.Text $tbGw.Text $tbDns1.Text $tbDns2.Text }
    catch { Show-Message $_.Exception.Message 'Warning'; return }
    $suggest = if ($lbProfiles.SelectedIndex -ge 2) { [string]$lbProfiles.SelectedItem } else { '' }
    $name = ([Microsoft.VisualBasic.Interaction]::InputBox('Название профиля:', 'NetSwitch', $suggest)).Trim()
    if (-not $name) { return }
    if ($name -in $DhcpNames) { Show-Message 'Это имя зарезервировано для DHCP, выбери другое.' 'Warning'; return }

    $new = [pscustomobject]@{ Name = $name; IP = $s.IP; Mask = $s.Mask; Gateway = $s.Gateway; DNS = @($s.DNS) }
    $list = @($script:cfg.Profiles)
    $pos = -1
    for ($k = 0; $k -lt $list.Count; $k++) { if ($list[$k].Name -eq $name) { $pos = $k } }
    if ($pos -ge 0) {
        $ans = [System.Windows.Forms.MessageBox]::Show("Профиль «$name» уже есть. Перезаписать?", 'NetSwitch', 'YesNo', 'Question')
        if ($ans -ne 'Yes') { return }
        $list[$pos] = $new
    } else {
        $list += $new
        $pos = $list.Count - 1
    }
    $script:cfg.Profiles = $list
    Save-Config $script:cfg
    Update-ProfileList ($pos + 2)
    Set-Status "Профиль «$name» сохранён"
}

function Remove-SelectedProfile {
    $i = $lbProfiles.SelectedIndex
    if ($i -lt 2) { return }
    $name = [string]$lbProfiles.SelectedItem
    $ans = [System.Windows.Forms.MessageBox]::Show("Удалить профиль «$name»?", 'NetSwitch', 'YesNo', 'Question')
    if ($ans -ne 'Yes') { return }
    $list = [System.Collections.ArrayList]@(@($script:cfg.Profiles))
    $list.RemoveAt($i - 2)
    $script:cfg.Profiles = @($list)
    Save-Config $script:cfg
    Update-ProfileList ($i - 1)
    Set-Status "Профиль «$name» удалён"
}

function New-DesktopShortcut([string]$Title, [string]$Arguments, [string]$Description) {
    $file = ($Title -replace '[\\/:*?"<>|]', '_') + '.lnk'
    $path = Join-Path ([Environment]::GetFolderPath('Desktop')) $file
    try {
        $sh  = New-Object -ComObject WScript.Shell
        $lnk = $sh.CreateShortcut($path)
        $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ScriptPath`" $Arguments"
        $lnk.WorkingDirectory = $ScriptDir
        $lnk.Description = $Description
        $lnk.Save()
        Set-Status "Ярлык «$Title» создан на рабочем столе"
    } catch {
        Show-Message $_.Exception.Message 'Error'
    }
}

function New-ProfileShortcut {
    $a = Get-SelectedAdapter
    $i = $lbProfiles.SelectedIndex
    if (-not $a -or $i -eq 1) { return }
    $pname = if ($i -eq 0) { 'DHCP' } else { [string]$lbProfiles.SelectedItem }
    $title = "Сеть - $pname"
    New-DesktopShortcut $title "-Profile `"$pname`" -Adapter `"$($a.Name)`"" "$title ($($a.Name))"
}

function Update-ProxyView {
    $script:proxyState = Get-ProxyState
    $tbProxy.Text = $script:proxyState.Server
    $chkProxyLocal.Checked = (-not $script:proxyState.Server) -or (($script:proxyState.Bypass -split ';') -contains '<local>')
    if ($script:proxyState.Enabled) {
        $lblProxyState.Text = "Сейчас: включён ($($script:proxyState.Server))"
        $lblProxyState.ForeColor = 'DarkGreen'
    } else {
        $lblProxyState.Text = 'Сейчас: выключен'
        $lblProxyState.ForeColor = 'DimGray'
    }
    $btnProxyOff.Enabled = $script:proxyState.Enabled
}

function Switch-ProxyFromForm([bool]$Enable) {
    $form.Cursor = 'WaitCursor'
    try {
        if ($Enable) {
            $server = Resolve-ProxyServer $tbProxy.Text
            Set-SystemProxy $true $server (Join-ProxyBypass $script:proxyState.Bypass $chkProxyLocal.Checked)
            Set-Status "Прокси включён: $server"
        } else {
            Set-SystemProxy $false
            Set-Status 'Прокси выключен'
        }
    } catch {
        Set-Status 'Ошибка — см. сообщение' 'Firebrick'
        Show-Message $_.Exception.Message 'Error'
    } finally {
        $form.Cursor = 'Default'
        Update-ProxyView
    }
}

# События
$cbAdapter.Add_SelectedIndexChanged({ Update-Current; Select-MatchingProfile })
$btnRefresh.Add_Click({ Update-AdapterList; Update-Current; Update-ProxyView })
$lbProfiles.Add_SelectedIndexChanged({ Show-SelectedProfile })
$lbProfiles.Add_DoubleClick({ Invoke-Apply })
$btnApply.Add_Click({ Invoke-Apply })
$btnTake.Add_Click({ Copy-Current })
$btnSave.Add_Click({ Save-AsProfile })
$btnDelete.Add_Click({ Remove-SelectedProfile })
$btnShortcut.Add_Click({ New-ProfileShortcut })
$btnProxyOn.Add_Click({ Switch-ProxyFromForm $true })
$btnProxyOff.Add_Click({ Switch-ProxyFromForm $false })
$btnProxyLnk.Add_Click({ New-DesktopShortcut 'Прокси - вкл-выкл' '-Proxy Toggle' 'Включить / выключить системный прокси' })
# Enter в поле адреса прокси включает прокси, а не применяет сетевой профиль
$tbProxy.Add_Enter({ $form.AcceptButton = $btnProxyOn })
$tbProxy.Add_Leave({ $form.AcceptButton = $btnApply })
$form.Add_Shown({ $form.Activate() })

Update-ProfileList 0
Update-AdapterList
Update-Current
Select-MatchingProfile
Update-ProxyView

[void]$form.ShowDialog()
