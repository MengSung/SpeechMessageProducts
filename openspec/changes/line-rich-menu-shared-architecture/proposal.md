# Proposal: LINE Rich Menu 共用架構（LineMessagingProcessor.RichMenus）

日期：2026-07-04
狀態：research 完成（CCG spec-research 產出）
權威設計文件：`docs/superpowers/specs/2026-07-03-line-rich-menu-architecture-design.md`（§1–13，已含使用者核可決策與 2026-07-04 創意功能補充）

## Why

未來產品（建設公司維修系統、協會會員系統、發票收款系統）都需要「依身分顯示不同選單」與「依輸入文字切換選單」。現有 rich menu 能力散落在 ChurchReport 的 `PushUtility` / `LineUtilityClass`（逐人 create-upload-link 的 legacy 模式），無法跨產品重用，也不支援 alias 分頁、宣告式佈建與批次操作。

## What Changes

1. 新專案 `LineMessagingProcessor.RichMenus`（+ `.Tests`）；`ILineRichMenuWorkflow` 全家自 `LineMessagingProcessor.Workflows` 原子搬移（namespace 改 `LineMessagingProcessor.RichMenus`，狀態列舉獨立為 `LineRichMenuStatus`）。
2. `LineMessagingProcessorClass` 補薄包裝：list / default(get+set) / alias CRUD+list / validate / bulk link+unlink / batch operation+progress（SDK 全部已有）。
3. 四核心元件：`ILineRichMenuCatalog`（宣告式目錄）、`ILineRichMenuProvisioningWorkflow`（手動同步，命名即狀態 `{key}:{sha256 前 8 碼}`，LINE 平台為唯一真相來源）、`ILineRichMenuAssignmentWorkflow`（身分指派）、`ILineRichMenuTextTriggerResolver`（Trim+Ordinal 完全比對）。
4. 創意功能（2026-07-04 網路研究納入，spec §13）：C1 佈建前驗證、C2 依 key 切換預設選單（時間帶/檔期選單基礎）、C3 分頁分析動作工廠（`richmenu:tab:{from}->{to}` postback 約定）、C4 批次指派與汰換（bulk link 500/批 + richmenu/batch replace）。
5. `LineMessagingProcessor.AspNetCore` 增 `AddLineRichMenus(...)` DI 註冊。
6. ChurchReport 最小驗證接入：legacy 單鈕認證選單宣告為 `legacy-auth`，`AddRichMenuMessage`/`DeleteRichMenuMessage` 改走指派工作流，補 `SyncRichMenusAsync()` 手動同步入口，更新 `ChurchReport.MemberInfo.Tests` 既有測試。

## Hard Constraints（雙模型探索彙整）

- RichMenus 專案禁止依賴 ChurchReport / `Microsoft.Xrm` / `IOrganizationService` / `Entity` / Controller / `IActionResult` / `DbContext`，也不依賴 `LineMessagingProcessor.Workflows`（`LineNotificationStatus` 耦合以 `LineRichMenuStatus` 解除）。
- Channel access token 由呼叫端注入；快取為頻道層級、一 channel 一份（ChurchReport `SetupChannelAccessToken` 執行期切換 jesus/jesusback token，快取必須跟 client 一起重建）；絕不快取使用者相關資料。
- LINE 平台為唯一真相來源，不引入資料庫；同步絕不自動刪除線上選單（唯一例外：本次呼叫剛建立、圖片上傳失敗的半成品 best-effort 清理）。
- 使用者解綁（Unassign）只 unlink、不刪共用選單本體（與 legacy「刪本體」為有意差異，spec §8 已記錄）。
- 選單名稱 ≤300 字元（SDK setter 截斷）；`RichMenu.Name` 由系統以 `{key}:{hash}` 覆寫；序列化採 SDK 同款 camelCase（`CamelCaseJsonSerializerSettings` 為 internal，共用層自建等價設定）。
- net10.0、Nullable enable；測試 xunit 2.6.6 + FluentAssertions 6.12.0 + capture-handler（不打真實 LINE API）；Newtonsoft.Json 13.0.3；UTF-8 無 BOM + CRLF；不提交 bin/obj。
- ChurchReport 以 `new PushUtility(client)` 手動建構為主流（非 DI），共用層 API 必須可脫離 DI 使用。

## Dependencies

Line.Messaging（SDK 模型與 client）→ LineMessagingProcessor（薄包裝）→ LineMessagingProcessor.RichMenus（本案）→ LineMessagingProcessor.AspNetCore（DI）→ ChurchReport（產品接入）。測試面：LineMessagingProcessor.Tests、RichMenus.Tests（新）、AspNetCore.Tests、ChurchReport.MemberInfo.Tests。

## Risks & Mitigations

- 型別搬移破壞 using／建構子 fallback → 原子搬移 + 全 solution 建置驗證 + 與並行 Codex 工作錯開（單一寫者）。
- 名稱雜湊比對被人工改名破壞 → 只認完全相符名稱；不符者列 Unknown 報告，人工處置。
- 跨頻道快取污染 → 快取實例綁 workflow 實例／DI 容器（單 token），token 重建時重建。
- 指派時選單尚未同步 → `ValidationFailed`（`line-richmenu-not-provisioned`）不發 link，由管理者先跑同步。
- 既有測試斷言舊行為 → `PushUtilityWorkflowTests` 兩個 RichMenu 測試改驗指派工作流。

## Success Criteria（可驗證）

1. `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` 0 錯誤；全部相關測試專案綠。
2. 邊界掃描：RichMenus 專案無任何禁用 token。
3. 同步冪等：stub 已含同名選單時第二次同步全 `UpToDate`、零寫入呼叫（含 default 不重設）。
4. 改版換新：hash 變更 → 建新版 + alias 改指新 ID + 舊版列 Unknown、無 DELETE。
5. 指派防護：key 不在目錄或未同步 → `ValidationFailed` 且無 HTTP link 呼叫。
6. C1–C4：validate 先於 create；`SetDefaultAsync(menuKey)` 解析與指派一致；tab action data 格式 `richmenu:tab:{from}->{to}`；bulk 自動 500 分批、batch replace 回 requestId。
7. ChurchReport 使用者可見行為不變（同選單、同訊息）；touched 檔案 UTF-8 無 BOM + CRLF。

## User Confirmations

- 2026-07-03 brainstorming：身分=兩者並存；觸發詞=設定檔；佈建=宣告式+手動同步；切換=混合；範圍=共用層+ChurchReport 最小驗證；架構=獨立 RichMenus 專案（使用者否決擴充 Workflows 方案）。
- 2026-07-04：使用者指示網路搜尋創意作法並設計進本次修改；選項確認時使用者暫離，C1–C4 由 Claude 依最佳判斷全數納入（C4 獨立切片，最易剔除）——**待使用者回來後複核**。

## 建議切片（供 plan 階段）

R1 搬移 → R2 processor 包裝（含 validate/bulk/batch）→ R3 目錄+命名+同步（含 C1）→ R4 指派+觸發+DI（含 C2、C3）→ R4b 批次指派汰換（C4）→ R5 ChurchReport 接入 + 雙模型 review。
