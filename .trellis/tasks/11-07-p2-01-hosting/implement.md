# P2-01 執行計畫

- [ ] 讀取 `.trellis/spec/backend/index.md`、error-handling、cross-user-isolation、
  duplicate-row-publication 及相關 guides。
- [ ] 建立 `SpeechMessage.Hosting`／`SpeechMessage.Hosting.Tests` csproj 與 solution 參考。
- [ ] 先補四種通知開關、故障注入、取消／逾時／drain／資源基線測試並確認紅燈。
- [ ] 加入 Hosting registration、`StartupGuards` 與架構測試搬移／擴充。
- [ ] 以 ChurchReport 的既有組合根接入，不重複建立通知 owner。
- [ ] 執行 P2-01 focused tests、ChurchReport exception tests、Release build。
- [ ] 執行 file documentation、encoding、duplicate、I18nCheck 與架構驗證。
- [ ] 撰寫 `wp-reports/P2-01.md`，更新 `實作進度.html`，檢查未辨識 dirty files。
- [ ] 載入 `trellis-check` 完成主會話審查；若有 blocker，修復後重跑。
- [ ] 載入 `trellis-update-spec` 評估是否需沉澱新規則，再提交 P2-01。

## 受影響檔案界線

預期新增／修改：

- `SpeechMessage.Hosting/**`
- `SpeechMessage.Hosting.Tests/**`
- `SpeechMessageProducts.sln`
- `SpeechMessageProducts.ChurchReport/Program.cs`、必要 registration／project reference
- 既有架構測試與 P2-01 回報／進度／任務文件

不得在本子任務修改 P2-02～P2-12 的產品規則、D365 schema、正式組織資料或
任何秘密設定值。
