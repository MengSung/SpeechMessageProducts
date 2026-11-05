# 階段 0 審查修正 H-01（核定改列 Medium）：套件路徑邊界

使用者 2026-11-04 已確認問題屬實；因唯讀 XML 且 XmlResolver=null，嚴重度改列 Medium，仍修正。

## TDD 與修正
- 真正以 New-Item -ItemType Junction 建立隔離案例：先追蹤假專案，再把父目錄換成外部 junction，確保 Git 索引仍列出該路徑。
- 修正前 2／5 通過，3 個功能紅燈：一般 junction 未在 XML 前拒絕、其他工作樹 junction 未排除、建置產物未排除。
- 所有讀檔入口（含中央表及固定安全套件檢查）均先驗證根前綴，再逐段拒絕 reparse point。錯誤只回固定 UnsafePath，不把外部 XML 內容帶出。
- 排除清單僅補已核定的 .worktrees、.其他解決方案、bin、obj；保留 docs-like 等實際專案盤點，不把孤兒專案排除。
- PowerShell 5.1 與 7 各 5／5；實際 repo 39 專案、149 引用、2 排除、0 違規。
- 每案外部合成檔案雜湊不變且可立即獨佔開啟；先解除 junction，再刪除經絕對前綴驗證的自有暫存根。
- 新守門測試已納入 CI 命令，GitHub 是否實跑仍依後續使用者推送結果；本次無推送、無 D365／LINE。

本修正列入第 09 份複審，最後再跑完整 00 §8。
