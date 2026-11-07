# P2-02 GoodShepherd Core 雙目標骨架

## Goal

依實作計畫 3.3 建立 GoodShepherd 產品領域雙目標核心與雙目標測試骨架，讓後續 P2-04 送訊及 P2-07 待辦可以按核定的相依方向整合。此工作包只建立模組位置與服務型別邊界，不提前實作尚未完成依賴契約的通知、D365 或 LINE 行為。

## Requirements

- 產品專案名稱為 `SpeechMessageProducts.GoodShepherd.Core`，target 為 `net462;net10.0`，啟用 `SpeechMessageStrictCode=true` 及 XML 文件產出。
- 僅參考 `ToolUtility.Core`、`SpeechMessage.Localization`、`LineMessagingProcessor.Core`、`Line.Messaging`；不得參考僅 net10.0 專案。
- 新增 `Attendance`、`WeeklyReports`、`Lessons`、`Appointments`、`QrCodes`、`Care`、`Fees`、`Members`、`Messaging`、`Notifications` 模組導讀，交代階段 5 預計搬入的來源範圍。
- 建立 `Resources/GoodShepherdText.resx` 及由 `tools/I18nCheck` 產生的 `GoodShepherdText.g.cs`。初始空資源不宣稱產品文字已搬移。
- 建立 `Notifications/MemberNotificationService` 骨架型別。P2-02 不定義未核定的 NotifyAsync 參數或假裝完成讀取、教會授權、語系、範本與推播；完整行為由 P2-04 依其正式契約實作。
- 建立 `SpeechMessageProducts.GoodShepherd.Core.Tests`，target 為 `net48;net10.0`，直接參考產品核心，並以 smoke 測試確認各測試宿主載入的產品組件分別為 net462 與 net10.0 且服務型別存在。
- 建立兩個 `.csproj` 時同一工作步驟立即加入 `SpeechMessageProducts.sln`，核對每個專案恰好一筆、原專案未遺失且 Debug／Release 有建置映射。
- 全程只使用離線建置及合成測試，不連 D365、LINE、Hyper-V 或金流，不讀取秘密。

## Acceptance Criteria

- [ ] 產品／測試專案屬性、target 與參考方向符合規劃；Debug／Release 均可建置。
- [ ] 兩個測試 target 通過 smoke，且 net48 測試實際載入 net462 產品組件。
- [ ] 10 個模組導讀、通知骨架與空資源生成檔均存在；生成檔由正式 I18nCheck 產生並通過 `generate --check`。
- [ ] `dotnet sln list` 中兩個專案各出現一次，原專案集合保持完整，Debug／Release ActiveCfg 與 Build.0 映射存在。
- [ ] 檔案說明檢查、進度頁測試及新增／修改文字檔 UTF-8 無 BOM、CRLF、檔尾 CRLF 檢查通過。
- [ ] 進度頁將 P2-02 記錄為已完成時，附上實際命令、結果及證據路徑；此狀態只代表骨架工作包，不代表 P2-04 或 P2 總目標完成。

## Notes

- 總體依賴：P2-02 可脫離 P2-03 實機驗收獨立完成；P2-04 依賴本包的核心位置及型別界線。
- 方案即時納入規範見 `.trellis/spec/backend/directory-structure.md`。
- 使用者要求同階段一般資訊維持單一卡片，摘要本文及每筆 steps 均有 Asia/Taipei 日期時間。
