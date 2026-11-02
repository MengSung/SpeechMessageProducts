# C3/C4 執行計畫

- [x] 建立 C3 RED 測試：持有來源同步根時，前景 `UpdateMember` 必須等待；鎖內 snapshot 為完整舊值、寫入後 snapshot 為完整新值。
- [x] 執行 C3 RED 測試並確認現況因寫入端未受鎖而失敗。
- [x] 讓 `SmallGroupDataList` 將共享同步根傳入來源 group，新增受鎖多集合 mutation API，並遷移 SmallGroup／Personal／NewPerson 前景寫入端。
- [x] 重跑 C3 測試與既有 snapshot isolation tests。
- [x] 建立 C4 RED 測試：預期 `bg.accepted`、成功／初始化失敗／上傳失敗的 `bg.outcome` schema 與 `bg.end` 非成功語意。
- [x] 執行 C4 RED 測試並確認現況缺少事件而失敗。
- [x] 以最小內部 runner 接管背景 lambda 主體，加入安全 outcome event；控制器只排程 immutable work item。
- [x] 重跑 C4 tests、DataverseTrace tests、相關 ChurchReport tests 與 build。
- [x] 追加稽核下載完成後的排序／狀態正規化寫入端，確認其與快照共用同步根；並將 Personal CRM 欄位投影改為鎖外解析、鎖內純記憶體套用。
- [x] 修正背景 runner 測試的 root ServiceProvider 所有權，確保子 scope 與 root provider 依序釋放。
- [x] 檢查 diff、敏感資料、UTF-8 無 BOM、CRLF、final CRLF、Session／資源所有權；不 commit、不 push。

## Validation commands

- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~SmallGroupDataListSnapshotIsolationTests"`
- `dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --filter "FullyQualifiedName~DataverseTraceTests"`
- 依新增 runner 的 test project 執行對應 filter。
- `dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --no-restore`
- `git diff --check`
