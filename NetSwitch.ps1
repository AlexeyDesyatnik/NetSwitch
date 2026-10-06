<#
    NetSwitch — быстрое переключение IPv4-настроек сетевого адаптера:
    «Автоматически (DHCP)» <-> профили с ручными IP / маской / шлюзом / DNS.

    Запуск:
      NetSwitch.bat                                   — окно с профилями
      NetSwitch.bat -Profile "Кабинет"                — применить профиль без окна
      NetSwitch.bat -Profile DHCP -Adapter "Ethernet" — вернуть автонастройку

    Профили хранятся в profiles.json рядом со скриптом (создаётся при первом запуске).
    Нужны права администратора — скрипт сам запросит их через UAC.
#>
[CmdletBinding()]
param(
    [Alias('Profile')][string]$ProfileName,
    [string]$Adapter
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, Microsoft.VisualBasic

$ScriptPath = $PSCommandPath
$ScriptDir  = $PSScriptRoot
$ConfigPath = Join-Path $ScriptDir 'profiles.json'
$DhcpNames  = @('DHCP', 'auto', 'авто')

# ---------- Права администратора ----------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$ScriptPath`"")
    if ($ProfileName) { $argList += @('-Profile', "`"$ProfileName`"") }
    if ($Adapter)     { $argList += @('-Adapter', "`"$Adapter`"") }
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

# ---------- Режим без окна (для ярлыков) ----------
if ($ProfileName) {
    try {
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
        Show-Message "$($ad.Name): $what"
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
$form.ClientSize = New-Object System.Drawing.Size(560, 484)
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

# Низ окна
$btnApply = New-Ctl Button 12 440 160 34 'Применить'
$btnApply.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$lblStatus = New-Ctl Label 185 449 363 20 'Двойной щелчок по профилю — применить сразу'
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

function New-ProfileShortcut {
    $a = Get-SelectedAdapter
    $i = $lbProfiles.SelectedIndex
    if (-not $a -or $i -eq 1) { return }
    $pname = if ($i -eq 0) { 'DHCP' } else { [string]$lbProfiles.SelectedItem }
    $title = "Сеть - $pname"
    $file  = ($title -replace '[\\/:*?"<>|]', '_') + '.lnk'
    $path  = Join-Path ([Environment]::GetFolderPath('Desktop')) $file
    try {
        $sh  = New-Object -ComObject WScript.Shell
        $lnk = $sh.CreateShortcut($path)
        $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ScriptPath`" -Profile `"$pname`" -Adapter `"$($a.Name)`""
        $lnk.WorkingDirectory = $ScriptDir
        $lnk.Description = "$title ($($a.Name))"
        $lnk.Save()
        Set-Status "Ярлык «$title» создан на рабочем столе"
    } catch {
        Show-Message $_.Exception.Message 'Error'
    }
}

# События
$cbAdapter.Add_SelectedIndexChanged({ Update-Current; Select-MatchingProfile })
$btnRefresh.Add_Click({ Update-AdapterList; Update-Current })
$lbProfiles.Add_SelectedIndexChanged({ Show-SelectedProfile })
$lbProfiles.Add_DoubleClick({ Invoke-Apply })
$btnApply.Add_Click({ Invoke-Apply })
$btnTake.Add_Click({ Copy-Current })
$btnSave.Add_Click({ Save-AsProfile })
$btnDelete.Add_Click({ Remove-SelectedProfile })
$btnShortcut.Add_Click({ New-ProfileShortcut })
$form.Add_Shown({ $form.Activate() })

Update-ProfileList 0
Update-AdapterList
Update-Current
Select-MatchingProfile

[void]$form.ShowDialog()
