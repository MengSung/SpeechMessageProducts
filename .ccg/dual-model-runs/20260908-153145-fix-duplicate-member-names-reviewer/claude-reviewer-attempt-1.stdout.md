# 審查結論：fix-duplicate-member-names（working tree 未提交變更）

## 審查範圍說明（重要落差）

實際 `git diff`（未加 staged）只動到 **7 個檔案**：`AGENTS.md`、`.trellis/spec/backend/index.md`、`.trellis/spec/frontend/index.md`、`ListManager.cs`、`SmallGroupController.Date.cs`、`ListSmallGroupWeeklyReport.cs`、`SmallGroupDataList.cs`，加上一份新檔 `.trellis/spec/backend/duplicate-row-publication-contract.md`。

任務描述中提到的「LINE 登入移除 Task.Run/WhenAll」「DownloadIntegrateData 只在完成後設 LoadFlag」「SmallGroup/NewPerson/Chart API 不再直傳 Session 集合」「5 個併發測試」，經查**已在前一個 commit（`6abe3d43`）中完成並提交**，本次 working tree 並未再改動這些檔案。我針對「真正未提交」的差異做逐行審查，並額外交叉確認已提交程式碼與新契約是否一致（結論：一致，見下方 Info）。

## 已執行的驗證（非僅閱讀程式碼）
- `dotnet build` 主專案與測試專案：**成功，0 錯誤 0 警告**。
- `dotnet test --filter ListManagerIntegratePublicationTests`：**5/5 全數通過**。
- 對測試檔案中的中文字面值做位元組層級核對（見下方「修正前一輪自動化審查」）。

## 修正前一輪自動化審查（Gemini）的錯誤結論

`.ccg/dual-model-runs/20260908-153145-fix-duplicate-member-names-reviewer/gemini-reviewer-attempt-1.stdout.md` 的兩項結論**都是誤判，不應採信**：

- **其 Critical C1**（宣稱 `ListManagerIntegratePublicationTests.cs` 等檔案有 Mojibake 亂碼，"王小明" 變成 "????" 導致測試必然失敗）：實測 `dotnet test` 5/5 全過，且第 53、71 行 "王小明" 的原始位元組為 `E7 8E 8B / E5 B0 8F / E6 98 8E`，是合法 UTF-8（無 BOM），並無亂碼。這是 Gemini 自身讀取管線的顯示問題，不是檔案缺陷。
- **其 Warning W2**（宣稱 `CreateDetachedReadCopy` 對 Member 只複製容器、未做屬性層級深拷貝）：`Member.cs:37` 明確有複製建構式 `Member(Member source)`，`SmallGroupDataList.CloneSmallGroupData`（`SmallGroupDataList.cs:306-309`）對每個成員呼叫 `new Member(member)`，是逐屬性複製，不共享參考。

---

## Critical
本次審查**未發現**會造成跨使用者/跨產品 Session leakage、credential/scope 串用、發布半完成資料、錯誤刪除合法同名資料，或死結的 Critical 缺陷。核心新增邏輯（`SetupListManagerCore` candidate-then-publish、`EnsureAndGetIntegrateDetachedRead` 的 gate + row-key fail-closed、`ReloadDateAndGetIntegrateDetachedRead` 的 preferredListEntityId 重新授權）在失敗路徑上都能正確保留舊快照且不寫回半完成欄位。

## Warning

- **W1 — 新增最複雜的路徑完全沒有測試覆蓋**（`ListManager.cs:149-164` `ReloadDateAndGetIntegrateDetachedRead`）
  既有 5 個測試（`ListManagerIntegratePublicationTests.cs`）都只涵蓋 `EnsureAndGetIntegrateDetachedRead`，沒有任何測試涵蓋這個新方法：(a) `preferredListEntityId` 在新日期已不可見時是否正確 fallback 到新 `ActiveListId`；(b) CRM loader 在日期切換過程中失敗時，`m_Account`/`m_SelectDate`/`m_MultiGroupList` 是否真的維持舊值可重試；(c) `m_PublishedIntegrateLoadKey` 歸零後緊接著在同一 lock 內呼叫 `EnsureAndGetIntegrateDetachedRead` 的重入路徑。這正是「日期切換合併為同一 gate 內原子操作」這個賣點的主體邏輯，建議比照既有 5 個測試的風格（barrier / 例外注入）補上 3 項：preferred-list 失效 fallback、日期切換中失敗保留舊 scope、新舊 `m_HappyGroup` 一併驗證。

- **W2 — `CreateDetachedReadCopy` 每次都做兩輪完整深拷貝**（`ListSmallGroupWeeklyReport.cs:132-150`）
  新增的第 135 行 `copy.m_SmallGroupDataList = m_SmallGroupDataList?.CreateDetachedReadSnapshot() ?? ...` 是在 `CreateBackgroundUploadCopy()`（第 134 行）已經呼叫過 `CreateIsolatedSnapshot()`（對 `m_SmallGroupData`/`m_NewPersonFollowUpData`/`m_AllMemeberData` 三個集合逐一 deep clone）之後，再整個丟棄、重新對四個集合（含 `m_HappyGroup`）再 deep clone 一次，並且對同一個 `SmallGroupDataList._syncRoot` 額外鎖一次。`EnsureAndGetIntegrateDetachedRead` 是每次 Grid/Chart AJAX 都會呼叫的熱路徑，這會讓每個 request 的 Member 複製與鎖定次數翻倍。建議讓 `CreateDetachedReadCopy` 不透過 `CreateBackgroundUploadCopy`（或讓後者的 snapshot 建構可被跳過），只呼叫一次 `CreateDetachedReadSnapshot()`。

- **W3 — `IntegrateLoadKey` record struct 內含明文 credential**（`ListManager.cs:337-343, 433-439`）
  `record struct` 會自動產生印出所有欄位的 `ToString()`，其中 `Credential` 欄位即目前的登入密碼。目前程式碼沒有任何地方對它呼叫 `ToString()`/記錄它，所以現在不構成實際外洩，但這是個容易被之後除錯／log/例外訊息不小心帶出明文密碼的地雷。建議 override `ToString()` 只印 Account/ListEntityId/日期，或用密碼雜湊取代明文 Credential 做比對鍵。

## Info

- **確認為真正的安全修正**：`SmallGroupController.Date.cs` 的 `UpdateIntegrateDate` 舊版邏輯在 `currentListId` 非空時會「不驗證就直接 `ActiveListId = currentListId`」，等同信任呼叫端上一輪日期的小組 ID 跨到新日期 scope；新版 `ReloadDateAndGetIntegrateDetachedRead` 改為在新日期的 `m_MultiGroupList` 中重新驗證 `preferredListEntityId`，不存在則 fallback 到伺服器新算出的 `ActiveListId`，堵住了越權沿用舊 scope 的漏洞。
- **確認為真正的正確性修正**：`SetupListManager` 移除了 `catch (Exception e) { ...; throw e; }`（`ListManager.cs` 原第 ~120 行），改為不截斷例外直接往外拋，保留原始 stack trace，並讓例外在 `SetupListManagerCore` 失敗時不寫回任何欄位（candidate-then-publish 模式），可安全重試。
- **`m_HappyGroup` 行主鍵驗證與 detached 快照都已補齊**（`ListManager.cs:399`、`SmallGroupDataList.cs:265-281`），幸福小組成員之前不在 row-key fail-closed 與 detached read 範圍內，現已一致覆蓋。
- **Gate 重入是安全的**：`ReloadDateAndGetIntegrateDetachedRead` 在已持有 `m_IntegratePublicationGate` 時又呼叫 `EnsureAndGetIntegrateDetachedRead`（內部也 `lock` 同一物件）——`Monitor`/`lock` 在同一執行緒下可重入，不會死結；且該 lock 為 instance-owned（掛在使用者專屬的 `ListManager`），不會跨使用者互相阻塞。
- **鎖內同步 CRM I/O 屬既有架構取捨**（`ListManager.cs:104-108, 155-163, 328-359`）：`m_IntegratePublicationGate` 在整個候選建立期間（含同步 CRM 呼叫）都不釋放，同一 Session 的多個併發 AJAX（Grid + Chart 同時打）會依序排隊。作者註解已明確說明這是「CRM SDK 為同步 API」下的刻意取捨，範圍侷限在單一 Session，非跨使用者風險；不阻塞本次合併，但建議後續評估把純讀取 CRM 呼叫移出鎖外。
- **新 spec 文件編碼**：`.trellis/spec/backend/duplicate-row-publication-contract.md` 目前落地是純 LF（無 BOM），但專案 `.gitattributes` 對 `*.md` 設定 `eol=crlf`，`git check-attr` 確認 `git add` 時會自動正規化為 CRLF，不需手動處理。
- 其餘四個修改檔（`ListManager.cs`、`SmallGroupController.Date.cs`、`ListSmallGroupWeeklyReport.cs`、`SmallGroupDataList.cs`）皆為 UTF-8 無 BOM、CRLF，符合檔頭聲明的編碼要求。

## 總結
本次未提交變更本身範圍不大且邏輯正確、已編譯與既有測試皆綠燈；主要缺口是**新增的日期切換原子操作（`ReloadDateAndGetIntegrateDetachedRead`）完全沒有回歸測試**（W1），以及一個**非阻斷性的效能重複複製**（W2）。建議合併前至少補上 W1 的測試，其餘可視為後續優化項目。前一輪 Gemini 自動化審查的 Critical/Warning 結論皆為誤判，已用實測結果駁回，請不要依那份報告退回本次變更。

---
SESSION_ID: cb5e2362-21f7-476c-be88-e46b822cd63b
