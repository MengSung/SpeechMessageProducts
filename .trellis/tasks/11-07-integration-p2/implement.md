# P2 執行計畫

## 前置與記錄

- [ ] 讀取並遵守 `AGENTS.md`、00、03、90 與相關 backend spec。
- [ ] 以 P1 最後提交 `62b409c80` 為現況基線，保存每個 WP 的 dirty path 與測試結果。
- [ ] 在進度頁建立 P2 需求→現況→缺口→依賴→實作→證據追蹤。

## 依賴分組

### Layer 1：可離線並行的基礎

- [ ] P2-01 `SpeechMessage.Hosting` 與共用例外通知／啟動守門。
- [ ] P2-02 `GoodShepherd.Core` 雙目標骨架。
- [ ] P2-03 `ChannelTokenProtector` 與 Deployment 寫入命令。
- [ ] P2-06 送訊副本盤點與特性測試。
- [ ] P2-09 QR 內容編碼與圖片往返測試。

### Layer 2：依賴 Layer 1 的整合

- [ ] P2-04 單人推播、教會一致性、Channel 查詢與宮格 API。
- [ ] P2-05 Flex／sender 範本。
- [ ] P2-07 OutboundTask writer／worker。
- [ ] P2-08 RichMenu 多語 handler。
- [ ] P2-10 改字時間輪詢。

### Layer 3：端到端與使用者驗收

- [ ] P2-11 `DiagnosticsPingPlugin` member 骨架。
- [ ] P2-04 前置沙箱推播、P2-12 推播鏈驗收。
- [ ] 全範圍品質檢查、文件／進度／任務報告、spec 回饋與本地提交。

## 每個 WP 固定步驟

1. 先讀完整受影響檔案與適用 spec。
2. 先寫測試並執行紅燈；保留故障證據。
3. 實作最小變更，補完整繁中檔案說明與生命週期註解。
4. 執行該 WP 測試，再執行跨模組測試。
5. 更新 `wp-reports/P2-xx.md` 與 `實作進度.html`。
6. 檢查 UTF-8/BOM/CRLF、隔離、資源 owner、失敗路徑與差異範圍。

## 最終驗證命令

```powershell
dotnet build .\SpeechMessageProducts.sln -c Release -warnaserror:NU1605
dotnet test .\SpeechMessageProducts.sln -c Release --no-build
python tools/RepoQuality/check_file_documentation.py
powershell -NoProfile -File .\tools\RepoQuality\Test-TextEncoding.ps1 -Scope Changed -Base 62b409c80
powershell -NoProfile -File .\tools\RepoQuality\Find-DuplicateCode.ps1 -Baseline .\tools\RepoQuality\duplicate-baseline.json
dotnet run --project .\tools\I18nCheck -- verify
```

若某命令因既有環境或外部依賴失敗，保留完整輸出與明確範圍，不以放寬警告、
停用測試、刪除資料或假造外部證據繞過。

## 回復點

- 每個 WP 使用獨立提交；不得把未辨識的 dirty files 加入。
- D365／LINE 實機操作前保存 plan、export、清理與回復步驟；失敗立即停止同一
  外部操作，先完成離線修復。
- 使用者 goal 明確取消外部雙模型；主會話負責整合與審查，結果不得宣稱外部
  雙模型完成。
