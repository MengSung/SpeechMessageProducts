@echo off
@rem 立即更新一次三大產品共通層的即時進度（live-A.js、live-B.js），並在視窗顯示兩條線的摘要；看完按任意鍵關閉。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%USERPROFILE%\.speechmessage\Update-3PLiveProgress.ps1" -Once
echo.
pause
