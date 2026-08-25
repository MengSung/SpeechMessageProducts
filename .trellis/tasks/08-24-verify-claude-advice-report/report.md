# Claude 建議獨立查證報告

查證日期：2026-08-24  
範圍：`SaveIntegrate` 背景上傳、快照、快取、診斷追蹤、Git 合併與發布判定。  
限制：本報告只讀取程式碼、Git 與既有 trace；沒有操作 CRM，因此 CRM 最終資料狀態仍須人工煙霧測試確認。

## 摘要

Claude 對 C3、快取逾期、C4 與 `bg.end` 的技術更正大致正確；但「新版比舊版好，所以 2A 通過即可正常上線」不能成立。它把兩個不同的判斷混在一起：相對於舊版，快照隔離確實大幅降低背景工作直接改寫 Session 圖的風險；但目前仍存在可靜默寫入混合欄位資料的 C3 競態，且 2A trace 驗證不會證明 CRM 業務成功。

因此：

- 正常發布：不建議只跑 2A 就放行；至少先補齊 C3 的共同同步協定與安全的背景 outcome／錯誤分類，然後做一次真實 CRM 煙霧測試。
- 緊急修補：若舊版正在造成較嚴重、可確認的共享集合破壞，可在明確風險接受下限縮上線；但必須人工核對 CRM、建立回滾點，不能把現有 trace 視為成功監控。
- Git：功能提交已併入 `1.0.0.6.DesignNewArchitector`；不應重寫 feature branch 歷史。

## 證據與驗證

| 項目 | 結果 |
| --- | --- |
| `dotnet test ChurchReport.MemberInfo.Tests ... SmallGroupDataListSnapshotIsolationTests / InMemoryDataContextSmallGroupCacheIsolationTests` | 4 / 4 通過 |
| `dotnet test ToolUtility.Dataverse.Tests ... DataverseTraceTests` | 20 / 20 通過 |
| `python .trellis/scripts/verify_trace_invariants.py D:\除錯追蹤` | 11 通過、1 失敗；沒有 `bg.begin`／`bg.end`，本次 capture 未涵蓋 SaveIntegrate |
| `git merge-base --is-ancestor 71b42c31 1.0.0.6.DesignNewArchitector` | 成功；`71b42c31` 已合併 |
| `git log --merges --ancestry-path ...` | 合併提交為 `ebd2af507` |

測試證明深拷貝、無 Session 快取邊界與 DataverseTrace 的既有契約沒有退步；它們**沒有**模擬「同一個 `Member` 被前景原地寫入，同時被快照逐欄讀取」，也沒有操作真實 CRM。因此不能用綠色測試或 `bg.end` 證明 C3 已安全或上傳已成功。

## 逐項裁定

### C3：靜默混合欄位快照

裁定：**正確，且比 Claude 原先的「可能丟出 `InvalidOperationException`」更嚴重。**

直接證據：

- `SmallGroupDataList.CreateIsolatedSnapshot()` 在 `SmallGroupDataList.cs:83` 取得私有 `_syncRoot`，並在 `CloneSmallGroupData()` 以 `new Member(member)` 逐欄讀取來源 `Member`（107–128 行）。
- `Member(Member source)` 在 `Member.cs:37–89` 逐一讀取大量欄位。
- `SmallGroupData.UpdateMember()` 在 `SmallGroupData.cs:52–79` 沒有啟用鎖，使用 `JsonConvert.PopulateObject` 原地改寫既有 `Member`。
- `UpdateSmallGroupPresentRecord()` 在 `SmallGroupController.Crud.cs:79–87` 同時啟動兩個 `Task.Run` 更新兩個資料集合。
- 全 repo 的 `SyncRoot` 搜尋只有宣告處，沒有任何寫入端持有同一把鎖。

因此，在快照逐欄讀取時，另一請求可同時逐欄寫入同一成員；.NET 沒有提供這種跨多個屬性的原子快照保證。結果可能是一個不屬於任何使用者輸入時點的「部分舊值＋部分新值」副本，且因 `List<Member>` 結構未變，不需要出現 `InvalidOperationException`。背景上傳會使用此副本寫 CRM。

這不是深拷貝失效；深拷貝在**複製完成後**能隔離背景清理，但複製瞬間仍未與寫入端形成共同同步協定。現有測試只驗證背景改寫副本不會破壞來源，未覆蓋這個讀寫重疊情境。

建議：把「快照讀取」和所有會原地寫入／替換三組 `Members` 的前景路徑納入同一個、資料圖專屬的同步協定；鎖內只做短暫記憶體複製或更新，不做 CRM／網路 I/O。這是正常發布的資料一致性阻斷項。

### 快取逾期與 ListManager 重載

裁定：**正確。**

直接證據：

- `InMemoryDataContextSmallGroup.ListManager` 在 cache miss 時直接建立 `new ListManager()` 並寫回快取（660–689 行）；不是 CRM 重新載入。
- `EnsureCorrectUserData()` 只有 session 密碼及 ListManager 密碼都非空、而且兩者不等時，才呼叫 `SetupListManager()`（`BaseChurchController.cs:716–735`）。
- cache miss 之後，新 `ListManager` 的密碼為空；該分支不會成立。LINE 補救分支又只在 session 密碼為空時運作（741–770 行）。

所以「閒置 30 分鐘後自然恢復」是錯的：可能重建成空白 `ListManager`，而既有校正邏輯通常不會自動載入 CRM。這是既有問題，但它也表示 C2 不可宣稱使用者等待 cache 到期就能脫困。

附帶裁定：移除快取以求重載是危險方案，因 getter 的實際行為正是建立空白物件。`SetSessionDirtyFlag()` 只寫入 `dirty=1`；本次搜尋沒有找到這個旗標的讀取端，不能把它當重載機制。

### C4：背景上傳失敗的可觀測性

裁定：**正確，但安全修法不是記錄完整例外文字。**

直接證據：

- 背景 lambda 的 outer catch 在 `SmallGroupController.Save.cs:176–190` 只寫 `ex.GetType().Name`。
- `ToolUtilityClass.TraceByLevelStatic()` 寫入 `System.Diagnostics.Trace`（`ToolUtilityClass.Core.cs:156–171`）；`Program.cs` 的 listener 追加到 `Trace.log`。
- `FileToolUtilityTracer` 明確說明一般 `Trace.WriteLine` 不會複製到 `CHURCH_REPORT_TRACE.TXT`，且它使用自己的 private writer。
- 若錯誤發生在 `scopeFactory.CreateScope()`、`GetRequiredService<IToolUtilityProvider>()` 或 `GetToolUtility()`，尚未進入 `UploadDataAsync()`；上傳器的 instance tracer 不可能補寫完整原因。

因此 pre-upload fault 可能只留下例外型別，且不會出現在 `CHURCH_REPORT_TRACE.TXT`。Gemini 第一次輸出建議記錄 `ex.ToString()`；這點被駁回，因為專案規範明確禁止將可能含帳密、成員資料的 exception text／stack 寫入一般追蹤檔。

建議：記錄安全的 `outcome`、粗粒度 `errorClass`、`operationId/traceId` 和固定 stage（例如 `scope-create`、`provider-resolve`、`upload`）；受保護的服務端日誌若存在，再以 ID 關聯詳細診斷。這是高優先可觀測性改善；若沒有真實 CRM smoke 的替代證明，正常發布前應完成。

### `bg.end` 的意義與 2A trace

裁定：**正確。**

直接證據：`DataverseTrace.BackgroundScope.Dispose()` 在 427–456 行無條件 enqueue `BackgroundEnd`；JSON 序列化只輸出 duration、CRM count、lease 計數與統計欄位，沒有成功／失敗 outcome（1355–1398 行）。

即使上傳失敗，離開 `using var traceScope` 仍會寫 `bg.end`。相反地，`backgroundCopy == null` 時，程式也會略過上傳、印出「背景上傳完成」並離開 scope；因此有 `bg.end` 也不能證明 CRM 真的更新。

`verify_trace_invariants.py` 能驗證 trace 可解析、CRM 計數是否歸因、背景 scope 是否成對、租約是否歸還及是否出現 `NOSESSION`；它不查 CRM 資料、也不判定業務成功。本次資料夾輸出為 11 pass／1 fail，因沒有任何背景事件，只能證明**這次 capture**未執行 SaveIntegrate 背景路徑，不能推論歷史上從未執行。

### `requiresRefresh` 與提示語

裁定：**Claude 對「提示語不可宣稱 CRM 已完成」正確；現況還有未消費的回應欄位。**

後端在 `Save.cs:196` 回傳 `requiresRefresh=true`，但 `IntegrateView.cshtml:140–149` 沒有讀取它，只在一秒後無條件呼叫 `grid.refresh()`。這不是 CRM reload 的證據，也不會使後端狀態機自動修復。前端文案目前是「資料已送出，正在背景上傳中」，比「已上傳」誠實，但 timeout 分支仍用相同文字，不能表示 CRM 成功。

因此 C2 的低成本處置應先明確選擇：要實作真正、可失敗且不清空 Session 的重新載入流程，或移除未使用欄位並把 UI 明確定義為「已接受上傳要求，完成狀態需另行確認」。不能藉由清除 cache 假裝重載。

### Git 合併與歷史重寫

裁定：**正確。**

`71b42c31` 和 `3a7fdf9d` 都是 `1.0.0.6.DesignNewArchitector` 的祖先；合併提交是 `ebd2af507`。功能分支遠端也包含 `71b42c31`。重寫 feature history 需要 force push，會使已合併的等價提交出現不同 hash，增加協作與稽核成本，沒有改善現有執行行為。保留歷史、以新的修正提交處理後續問題是正確方向。

## 對 Claude「新版較好即可上線」的評估

這個說法**部分正確，但不能作為正常發布結論**。

正確部分：新版不再讓背景上傳／清理直接持有並改寫前景 Session 集合，消除了舊版長時間、共享集合清理造成的高風險窗口；背景 DI scope 和 ambient override 的資源隔離也屬淨改善。

不足部分：

1. 「較舊版改善」是相對比較，不能證明「符合目前正常發布標準」。C3 已知會靜默污染 CRM，與專案的資料正確性、跨 request 隔離要求衝突。
2. 2A 的 trace 腳本不驗證 CRM 業務成功；本次甚至沒有捕捉到 SaveIntegrate。
3. 未受 Host 管理的 fire-and-forget `Task.Run` 是既有債務，不是這次新引入；但既然已確認它缺少 host shutdown drain／completion tracking，就不能只因為它舊而在正常發布判定中假裝不存在。是否延期是明確風險接受，不是技術推論。
4. 「新版嚴格優於舊版」本身也無法由目前測試嚴格證明；可證明的是它修掉一類共享集合寫入問題，同時仍保留另一類較短但靜默的快照一致性窗口。

## 發布建議

| 情境 | 建議 | 最低條件 |
| --- | --- | --- |
| 正常發布 | 不通過「只跑 2A 即發布」 | C3 共同同步協定；安全 outcome/error-class 記錄；真實 CRM smoke；確認前端／重載語意 |
| 緊急修補 | 可條件性考慮 | 具體證據顯示舊版正在造成更嚴重共享集合損壞；指定負責人接受 C3 殘留風險；限縮曝光；逐筆人工 CRM 比對；可立即回滾 |
| 上線後改善 | 不應遺忘 | 修復 cache miss 空白狀態、決定 `requiresRefresh` 契約、安排 host-managed background work／drain、修正誤導註解 |

正常發布不要求立刻大規模重構所有 legacy 路徑；但 C3 直接位於這次新增的快照邊界，且有可預見的靜默 CRM 污染後果，不能以「這個系統以前也有問題」降級。

## 外部模型狀態

已使用專案指定的 `Start-CcgDualModelRun.ps1` 執行兩次分析／審查與一次健康診斷：

- Gemini：分析與兩次審查皆產出內容，主要結論和本機證據一致。
- Claude：未產生可用輸出。健康 smoke 回報：`Failed to authenticate: OAuth session expired and could not be refreshed`。

因此本報告以本機程式碼、Git、測試與追蹤驗證結果為主要依據，Gemini 僅作為可選第二意見。Claude OAuth 過期是已記錄的工具狀態，但依使用者於 2026-08-24 的明確指示，不影響本報告完成，也不需要重新登入或補跑 Claude；本報告不把雙模型交叉驗證列為完成條件。

## 產出物

- 可重複使用提示詞：`independent-verification-prompt.md`
- 外部模型輸入與 runner artifacts：`.ccg/dual-model-runs/verify-claude-advice-report-*.md` 及對應時間戳目錄
- 本報告：`report.md`
