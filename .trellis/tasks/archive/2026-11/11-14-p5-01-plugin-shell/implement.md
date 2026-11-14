# P5-01 執行計畫

1. 讀取現有 Plugin、Core 通知、測試替身與打包設定，搜尋既有相同 helper，確認新增專案／檔案／resx 的登錄位置。
2. 先新增指定測試並確認紅燈；保留測試名稱，不以改測試使其通過。
3. 依底層順序實作例外／GUID／通知介面、設定與深度模型、recording service、PluginServices、PluginShellBase，再接測試替身與資源。
4. 每個新增／修改來源補完整繁中檔案說明與成員 XML 註解；檢查 CRLF、無 BOM、final CRLF 與 JSON companion。
5. 依 05A 命令跑三組 Release 測試、架構測試、Plugin 打包與 assembly checker；失敗時先定位根因再修正。
6. 跑文件、I18n、編碼、重複碼與固定 guard；產生附錄 F 一頁回報、更新進度頁 DATA，提交後 push 並以 `git ls-remote` 核對 SHA。
7. 本包只完成離線基礎，不執行 P5-02 以上或任何 goodshepherdback 部署。
