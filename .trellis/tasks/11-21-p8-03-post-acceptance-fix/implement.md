# 執行清單：P8-03 驗收後修正

## 執行順序

- [x] 核對工作樹、HEAD、分支、遠端及 dirty paths；確認 L1 已提交內容不在本次工作樹差異中，掛鉤紀錄另行隔離。
- [x] 以正確失敗行為新增三項測試並取得紅燈：MSBuild 缺少 Area view Content、Terms catalog 遇到冷快取、MVC action descriptor 缺少 area metadata。
- [x] 修正 `.csproj` glob、`ChurchSettingsAccess.EnsureTextScopeLoadedAsync` 與 controller Area／Terms 順序。
- [x] 更新檔案整體說明及必要成員文件。
- [x] 使用 R2 既有產生入口更新 Debug／Release route-table 與 serialization-shape 黃金，並維持比對測試。
- [x] 三項具名測試綠燈 3/3；ChurchReport.MemberInfo.Tests Release 693/693。
- [x] `python tools/RepoQuality/check_file_documentation.py` 通過，檢查 1,883 檔、0 失敗；`git diff --check` 通過。
- [x] 對本次唯一提交在 `C:\smv\guard-p8` 執行完整 00 §8.1；原始紀錄只能寫入 `C:\smv\guard-p8-evidence`。完整紀錄 `C:\smv\guard-p8-evidence\guard-c8d088fe-full.log`，結束碼 0。
- [x] 追加 P8-03b 回報「驗收後修正」段，保留原結論並明確標示事後修正。
- [x] 檢查提交範圍：`c8d088fe` 僅含 ChurchReport Area、相應測試與 R2 黃金；不含 L1 檔案、`.turns.json` 或 P3 任務副本。
- [x] 確認分支含未驗收 L1 ancestor 時不推送，以免提前發布／驗收 L1；記錄遠端仍為 `10cd19a6dafe156c49952fe3c3c7a508f361ea17`。
- [x] 按執行清單 §5 的條件分支記錄推送相依衝突，接續 L1；不 force push、不改寫遠端歷史。

## 驗證命令

- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Release --filter "FullyQualifiedName~Publish_AreaRazorViews_IncludedInPublishOutput|FullyQualifiedName~Terms_Get_LoadsOverrideSnapshotBeforePreview|FullyQualifiedName~Area_Views_ResolvedUnderChurchSettingsArea"`
- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Release`
- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Debug --filter "FullyQualifiedName~R2VisibilityContractTests"`
- `python tools/RepoQuality/check_file_documentation.py`
- 完整守門依 00 §8.1；提交驗收依 `C:\smv\guard-p8` 唯一 clone。
