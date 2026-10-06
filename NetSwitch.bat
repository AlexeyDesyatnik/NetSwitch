@echo off
rem NetSwitch launcher: double-click to open the window,
rem or pass arguments, e.g.:  NetSwitch.bat -Profile DHCP -Adapter "Ethernet"
rem                             NetSwitch.bat -Proxy Toggle
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0NetSwitch.ps1" %*
