@echo off
@rem 立即更新一次三大產品共通層的即時進度（live-A.js、live-B.js），約 10 秒後自動結束。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%USERPROFILE%\.speechmessage\Update-3PLiveProgress.ps1" -Once
