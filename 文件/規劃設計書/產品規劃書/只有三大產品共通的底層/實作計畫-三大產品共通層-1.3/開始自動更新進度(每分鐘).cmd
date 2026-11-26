@echo off
@rem 開始三大產品共通層的即時進度自動更新：每 60 秒產生一次 live-A.js、live-B.js。
@rem 會開一個縮小的 PowerShell 視窗；關掉那個視窗就停止。開一次就好，重開機後再雙擊一次。
start "3P live progress" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%USERPROFILE%\.speechmessage\Update-3PLiveProgress.ps1"
