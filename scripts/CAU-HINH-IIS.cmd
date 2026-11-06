@echo off
powershell.exe -NoProfile -File "%~dp0ops\complete-iis-publish.ps1" -PublishRoot "%~dp0."
if errorlevel 1 echo IIS chua du dieu kien. Doc thong bao ben tren; khong tat guard bao mat.
pause
