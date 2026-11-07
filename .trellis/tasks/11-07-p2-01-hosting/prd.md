# P2-01 Hosting 共用通知與啟動守門

## Goal

建立 `SpeechMessage.Hosting` net10 共用主機層，集中例外通知、Development／
Production 啟動守門與架構規則，讓 ChurchReport 及後續 LineWebhook 使用同一個
有界、可停止、不可洩漏的通知 owner。

## Requirements

- 沿用 P1 已驗證的 `ExceptionOutputOptions`、`ExceptionDiagnostics` 與
  `ExceptionReporting` 行為，四種 `WriteExceptionLog`／`SendLine` 組合獨立運作。
- 雙開時每個事件必須先成功寫入並 flush `Exception.log` 才能入列 LINE；寫檔失敗不得送 LINE。
- LINE 佇列有界，通知 sender 有逾時、取消與單一 reader；Host 停止時先停止接收、
  drain，再 Dispose client、CTS、mutex、subscription，不能保留 request／Session／credential。
- `StartupGuards` 必須驗證 ICU、正式環境禁止 `qps-ploc`／偽語系設定、未發布語系
  不可啟用，並沿用 P0 Development 的 goodshepherdback／LINE token fail-closed 規則。
- 架構測試搬至 Hosting.Tests，保留 R2／R3／R9，且不得讓主機、Plugin 或共用核心
  形成反向參考。
- ChurchReport 既有測試與例外責任不退化；相容 adapter 不保存 request 或 logger state。

## Acceptance Criteria

- [x] `SpeechMessage.Hosting` 與測試專案加入 solution，Release 建置通過。
- [x] 四種通知開關、寫檔失敗、LINE sender 失敗、佇列滿載、正常取消、內部逾時、
  drain／Dispose、A／B 兩組 operation 不串資料的測試通過。
- [x] ChurchReport 以 Hosting registration／adapter 使用共用入口，原有
  `ExceptionOutputOptionsTests` 與既有例外測試通過。
- [x] 啟動守門的正反例測試通過，錯誤訊息不含 token、密碼、完整 request 或個資。
- [ ] 架構測試通過；新增／修改 `.cs` 完成繁中檔案整體說明、XML 註解與 UTF-8 無 BOM／CRLF。
- [ ] `python tools/RepoQuality/check_file_documentation.py`、相關 `dotnet test`
  與 P2-01 報告均有證據。

## Notes

- 依正式計畫03實際搬移通知sender與options；三個diagnostics型別真正定義於Hosting。
  保留舊namespace與CLR型別轉送以支援舊呼叫端，不把adapter／薄殼當成搬移或驗收完成。
  跨層參考須通過正反圖及實際方案評估，Hosting不得反向依賴ToolUtility。
- P2-01 不執行 D365 寫入、LINE 實際推播或讀取秘密；外部驗收依 P2-04 前置另行處理。
- 本輪已核對的切片證據見 `research/culture-completeness-checkpoint.md`；
  R2／R3／R9 與整包回歸仍待完成，不封存本子任務，不把勾選的離線切片當成 P2 驗收。

## 3.4 收尾依據
唯一依據為現行 03 的 P2-01 與 00 §7.1。D28：ChurchReport R2 清冊只減不增，清零屬 P6-07，不阻擋本包；Plugin／新主機仍嚴格。既有必補測試、啟動守門及 R2／R3／R9 正反例須全過；目前組態 Plugin 產物測試不依賴另一組態。時間盒自 2026-11-07 21:10 Asia/Taipei 起半天。

## 完成定義（00 §7.1，不得加碼）
- [ ] 1. 本 WP 段落列出的測試全部同名存在並通過（雙目標專案兩個目標都過）
- [ ] 2. 00 §8.1 全部命令在乾淨複本（00 §8.2）通過；提交待驗證
- [ ] 3. Hosting 覆蓋率 ≥ 80%
- [ ] 4. 實機不適用
- [ ] 5. 一頁回報已寫、進度頁已更新
