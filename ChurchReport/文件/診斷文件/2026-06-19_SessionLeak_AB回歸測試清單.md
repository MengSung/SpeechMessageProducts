# Session-Leakage A/B 交錯登入回歸測試清單（FeeManagement 重用優化上線閘門）

## 文件資訊
| 項目 | 內容 |
|------|------|
| **建立日期** | 2026-06-19 |
| **用途** | 驗證「2026-06-19 補強實作計畫 Task 1」對 FeeManagement 重用去重的登入綁定修補，**確認不發生跨使用者資料外洩**。 |
| **對應修補** | commit `2e3cec47`（FeeList.EnsureLoginScope + EnsureLessonListLoaded/EnsurePresentFeeListLoaded 綁 session 登入）、`c1e77584`（GetFeeData 同步綁定） |
| **執行者** | **必須由人工操作真實帳號**（subagent/自動化無法登入真實使用者、切換帳號）。 |
| **閘門規則** | 本清單**全數 PASS** 才可在正式環境開啟相關重用優化。任一 FAIL → 視為 P0，立即回退（見文末「保底回退」），修復後重測。 |

## 前置設定
1. 以 **Debug build** 啟動站台（`Profiling:Enabled=true`，方便用 `Logs\Trace.log` 佐證）。
2. 準備兩組可登入身分：
   - **A**：帳密使用者甲（有課程/繳費資料）。
   - **B**：帳密使用者乙（與 A 的可見課程不同）。
   - **L**：LINE 身分（LIFF / LINE ID 登入）。
3. 全程使用**同一瀏覽器、同一分頁**（這是最危險情境：Session ID 不會因重新登入而更換，`InMemoryContext.FeeList` 以 Session ID 為快取鍵）。
4. 每個步驟同時記錄：①畫面看到的資料 ②`Logs\Trace.log` 對應行（`[FeeManagement] Reuse ...` vs `SetupPresentFeeList` / `SetupLessonList`）。

> **安全前提**：先跑完本清單（安全）才看效能數字。只要任一案例讓 B 看到 A 的資料，無論效能多好都不得上線。

---

## 測試案例

### C1 — 同瀏覽器「帳密 → 帳密」換人（核心案例）
1. A 登入 → 進 `/FeeManagement/LessonList` →（單一課程會自動載入）或進 `/FeeManagement/Fee/{X}` → **記下 A 看到的 FeeData**。
2. 登出 → **B 登入（同分頁）**。
3. B 直接進 `/FeeManagement/Fee/{X}`（用 A 剛才那門課的同一個 `discipleLessonsId`）。
- ✅ **PASS 條件**：B 看到的是 B 自己的可見範圍（或被擋 / 空），**絕不出現 A 的 FeeData**；`Trace.log` 出現 B 的 `FeeManagement.Fee.SetupPresentFeeList`（**非** `Reuse PresentFeeList for discipleLessonsId={X}`）。
- ☐ 結果：____（貼上關鍵 Trace 行）

### C2 — `/Api/FeeData` 直打（GetFeeData 旁路，sibling 修補驗證）
1. A 登入 → 進課程繳費頁，讓 DataGrid 呼叫 `/FeeManagement/Api/FeeData`（含或不含 `discipleLessonsId`）→ 記下資料。
2. 登出 → B 登入（同分頁）。
3. B 觸發 `/FeeManagement/Api/FeeData`（先不帶 `discipleLessonsId`，再帶 A 那門課的 `discipleLessonsId` 各測一次）。
- ✅ **PASS 條件**：兩種呼叫都**不得**回傳 A 的 FeeData；`Trace.log` 應顯示為 B 重新 `SetupFeeDataList` / `SetupPresentFeeList`。（此案例專門驗證 `GetFeeData` 內 `EnsureLoginScope` 是否在「不帶 id 直接吃 `FeeList.FeeDataList`」的旁路也生效。）
- ☐ 結果：____

### C3 — 「LINE → 帳密」交錯
1. L（LINE）登入 → 載課程/繳費 → 記下資料。
2. 登出 → B（帳密）登入（同分頁）→ 進相同課程頁 / 打 `/Api/FeeData`。
- ✅ **PASS 條件**：B **不得**看到 L 的資料（`_LoginAccount` 由 `"LineIdLogin"` 變成帳號 → `EnsureLoginScope` 應清空）。
- ☐ 結果：____

### C4 — 「帳密 → LINE」交錯
1. A（帳密）登入 → 載資料。
2. 登出 → L（LINE）登入（同分頁）→ 進相同課程頁 / 打 `/Api/FeeData`。
- ✅ **PASS 條件**：L **不得**看到 A 的資料。
- ☐ 結果：____

### C5 — 同一人重複載入（確認效能優化**未**被改壞）
1. A 登入後，連續多次進 `/FeeManagement/LessonList`、`/Fee/{X}`、刷新 `/Api/FeeData`。
- ✅ **PASS 條件**：第 2 次起 `Trace.log` 應出現 `[FeeManagement] Reuse LessonList for current login.` / `Reuse PresentFeeList for discipleLessonsId={X}`（重用仍生效），且資料皆為 A 自己的、正確。
- ☐ 結果：____

### C6 — 竄改 id（後端授權，非本次修補但須一併確認）
1. A 登入後，手動把請求中的 `discipleLessonsId` / `StorLessonsId` 改成**未授權**值，打 `/Api/FeeData`、`/Api/SaveBatch`。
- ✅ **PASS 條件**：回 401/403 或省略未授權資料，且 **CRM 不被更新**。
- ☐ 結果：____（若 FAIL，屬 2026-06-18 報告硬性規則第 3 條「後端須以當前 session 重新授權 id」的待辦，另案處理。）

### C7 — 動態回應快取標頭（Session Bleeding 防護仍在）
1. 對 `/FeeManagement/Api/FeeData` 等動態回應檢查標頭。
- ✅ **PASS 條件**：`Cache-Control: no-store, no-cache, must-revalidate, max-age=0`、`Pragma: no-cache`、`Vary: Cookie` 皆存在。
- ☐ 結果：____

---

## 判讀與保底回退
- **全數 PASS** → Task 1 修補驗證通過，重用優化可上線。
- **任一 FAIL（尤其 C1–C4）** → P0。立即**保底回退**：把 `EnsurePresentFeeListLoaded` / `EnsureLessonListLoaded` 內的「已載入則略過」改為**無條件重載**（移除 `IsPresentFeeListLoadedFor`/`IsLessonListLoadedFor` 的 early-return，但**保留** `CurrentLogin()` + `EnsureLoginScope`），犧牲效能換安全；同時把 `GetFeeData` 不帶 id 的旁路也改為一律重載。修復根因後再重跑本清單。

## 變更紀錄
- 2026-06-19：建立。對應 Task 1（commit `2e3cec47`、`c1e77584`）。
