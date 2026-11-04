# P0-13 verify 查詢欄位修復（2026-11-04）

## 問題與範圍
使用者回報 net462 apply 成功後 verify 為兩層 0x80041103、結束碼 1；P0-13 未完成。Codex 全程離線，只修改 DeploymentEngine、離線測試與文件；不改 Plugin、ToolUtility、框架、套件、清單或其他 worktree。

## 根因與變更
Microsoft 定義此碼為 QueryBuilderNoAttribute。步驟安全設定查閱應為 sdkmessageprocessingstepsecureconfigid，舊實作在查詢、寫入、存在性判斷、指紋與匯出八處誤用 secureconfigid。首次建立前不存在型別，不查既有步驟；trace 的安全設定為空，不觸發錯誤寫入，所以首次 apply 成功後 verify 才失敗。現以單一有文件的產品常數改正八處，secureconfig 內容欄位維持不變。

原假服務未驗證欄位，不能抓到這類錯字。現在替身依獨立官方欄位集合拒絕未知的步驟查詢／條件／建立／更新／讀取欄位；不共用產品常數。真實 CLI 案例在唯一暫存方案生成 unsigned net462 DLL 及假清單，執行 plan/apply/verify/export/停用 plan/apply/verify；不依賴本機正式產物。xUnit 集合禁止平行、finally 恢復 cwd，每命令租約均釋放。

## 先紅後綠
- 新增 4 例，保留所有既有方法；連同既有秘密設定寫入案例，共取得 5 個功能紅燈。
- 未修正時 FindStep／ReadSteps 與安全查閱寫入被假 SDK 以 0x80041103 拒絕；真正 CLI 首次 verify 預期 0、實得 1。
- 修正後 Deployment 56／56，行覆蓋率 84.95%。原 52 例全保留並通過。
- 新案例：Verify_AppliedTraceStep_UsesSupportedSchemaWithoutWrites、Cli_TraceLifecycle_ValidatesSchemaAndDisposesEveryLease、Export_ExistingSecureLookup_PreservesPresenceWithoutSecret、Apply_ExistingSecureLookup_TraceTransitionDeletesOriginalConfiguration。
- 安全查閱／內容只使用合成樣本，輸出不含樣本值；verify 不讀秘密或寫入。官方名稱獨立種入的既有查閱也可被辨識、解除並刪除原安全列。

## 完整 00 §8
Release 全方案建置 0 警告／0 錯誤；13 個測試目標共 1125／1125，ChurchReport 596／596；Debug tracer 7／7。Changed 編碼最終 527 檔、0 問題；重複碼 26 專案／879 檔、0 新增；I18n CJK ratchet 與兩個生成資源檢查通過。修改 C# 已逐位元組確認 UTF-8 無 BOM、CRLF、檔尾 CRLF。產品／測試／整理文件的 git diff --check 通過；原始審查包的 diff 上下文空白保留。證據在被忽略的 artifacts/p013-verify-schema，未讀取任何真實 export／設定／密鑰。

## 新審查
ClaudeOnly 根因分析完成：reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。本次新修正另經自癒入口 ClaudeOnly 審查通過（20261104-202450-p013-verify-schema-review-reviewer），無 Critical／High 或其他缺陷。分析與審查皆為單模型（使用者指定 ClaudeOnly）：reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。 這是第 10 份之後的新審查，不冒稱原 net462 包已涵蓋；不是配額降級。

## 風險與下一步
真實 verify 尚未重試；離線全綠不等於現場狀態一致。Codex 已完成 Deployment Release 重建；同一工作區使用者只需重跑 verify 與 $LASTEXITCODE，預期固定成功摘要及 0，再進 smoke。此修正不需要重打 Plugin 或再次 apply。若後續需要寫入，必須重新 plan 並審核；不能重用已套用計畫。舊 LINE token 與會友 LINE ID 保留的核定不變；既有舊流程維持停用。不推送、不合併、不改寫歷史。
