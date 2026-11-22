# P8-04a 偽語系稽核工具與網頁驗收

## Goal

建立可重複執行的 ChurchSettings 偽語系頁面稽核工具，並以 Development ChurchReport 與 Edge 實際驗收三個設定頁及 403 頁，修正範圍內的包覆或截斷問題。

## Requirements

- 把 P6 可共用的登入、臨時會友、SDK 橋接、Edge 啟動及 User Secrets 租約能力抽至 `tools/PageAcceptance/Shared/`，P6 改用共用實作，P6 情境與功能保持不變。
- 以 Node 與 repo 外的 `playwright-core` 建立 `tools/PseudoLocaleAudit/`；不新增 `.csproj` 或把瀏覽器依賴放進 repo。
- 只稽核 ChurchSettings 名詞、奉獻類別清單、修改紀錄及 403「沒有權限」頁。ChurchReport 既有頁與 LIFF 依 I6 排除，明確記錄理由。
- CJK 字元未被偽語系括號包覆時列為未包覆；捲動容器不算截斷；只有實際裁切／溢出才列截斷。
- 提供離線規則測試四案及生命週期測試三案：登入失敗、頁面逾時、清理失敗。生命週期測試不得連線、讀取真實秘密或保存敏感輸出。
- 以 Edge 實際開啟四個頁面並保存不含機密的結果與必要截圖；最終未包覆中文為 0、截斷為 0，否則逐項列出明確可接受的截斷及理由。
- 新增／搬移的 JSON 伴隨說明須同步維護；全部自行維護的 JS／CJS 使用繁中檔案整體說明及 UTF-8 無 BOM、CRLF、檔尾 CRLF。

## Acceptance Criteria

- [ ] `node --test tools/PseudoLocaleAudit/Tests/PseudoAuditRules.Tests.cjs` 四案全綠，且存在 CJK 未包覆、已包覆、捲動不算截斷、真正截斷四個確切測試名稱。
- [ ] `node --test tools/PseudoLocaleAudit/Tests/PseudoAuditLifecycle.Tests.cjs` 三案全綠，並以合成依賴注入登入失敗、頁面逾時及清理失敗。
- [ ] `node tools/PageAcceptance/P6/Tests/P6Lifecycle.cjs` 四案全綠；P6 工具改接共用元件且情境來源不變。
- [ ] Edge 實際成功開啟規定的四頁，產出可核對稽核結果與證據。
- [ ] 最終未包覆中文 0、截斷 0；任何例外逐項說明且有證據。
- [ ] `audit-urls.json`、`docs/file-purpose-companions.json`、回報及進度頁 DATA 同步；未修改計畫規劃文字或頁面 renderer。
- [ ] P6Lifecycle、兩支 PseudoAudit Node 測試、適用文件／位元組檢查及 `git diff --check` 通過；依序提交、完整守門、回報、驗收、推送並核對遠端 SHA。

## Boundaries

- 瀏覽器只允許連到本機 ChurchReport Development；組織連線只允許核准的 goodshepherdback，絕不連正式組織。
- 不把 token、密碼、cookie、個資或原始秘密設定輸出、寫入 repo 或截圖。
- 每個測試 fixture、Browser、Context、Host、子程序、計時器及秘密租約均有唯一 owner、有界等待與確定清理；清理失敗必須明確失敗，不可報成殘留 0。
- 本單元不執行 P8-04b 自訂或 16.4 LINE 實機驗收。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
