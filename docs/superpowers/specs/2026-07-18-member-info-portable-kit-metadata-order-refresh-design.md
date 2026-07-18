# 會友資訊 portable kit metadata 排序增量更新 — 設計規格

日期：2026-07-18
來源分支：`Sunny_5.1.2.WorktreeTuneMemberView`
來源 commit：`2406b126e989cc980e8cada9da0e07a2ede1e08d`

## 1. 設計目標

portable kit 目前是以 `589f0baa` 為來源的 10／10 版本，內容內部一致但不知道最新的
Dynamics metadata rank 排序。本次採選擇性增量更新：只改會被新排序契約影響的整合文件、
參考快照、來源索引、Manifest 與 ZIP，保留其餘歷史證據原貌。

## 2. 權威排序契約

套件必須明確傳達：`PicklistAttributeMetadata.OptionSet.Options` 集合順序是唯一權威；
raw OptionSet 整數、中文 label 與 Sunny 專屬硬編碼清單都不是排序來源。已設定選項依 rank，
metadata 未知舊值其次，真正空白最後；反向排序只反轉已設定選項。一般小組、搜尋結果與
無小組遠端分頁必須一致。

## 3. 文件架構

- 將原始 Spec／Plan 加入 `original-specs/` 與 `original-plans/`，權威數量升為 11／11。
- 在整合規格與依賴矩陣加入 metadata API、共用記憶體快取、aggregate FetchXML、segment paging
  與前端 rank selector。
- Prompt Playbook 新增獨立的 metadata 排序遷移階段，最終驗收順延為 Prompt 9。
- Runbook 與 Acceptance Checklist 提供跨教會 schema 盤點、正反向排序、未知值、空白與跨頁驗收。
- Prompt History 只追加本次真實使用者訊息；圖片內容不可杜撰，個人／機器值依既有政策遮罩。

## 4. 參考實作架構

新增三個 feature snapshots：metadata provider、count query、共用 sort；新增其三個測試快照，
並同步 DTO、SearchBuilder 與三份既有 contract tests。完整 Controller 與 Razor 仍不放入
`feature-files/`，只透過 `06-member-info-commitment-type-metadata-order.patch` 提供 path-limited
整合證據。Patch base 接續 patch 05 的 end `589f0baa`，end 為已推送 `2406b126`。

## 5. 封裝與安全

`verify-package.ps1` 的生成來源 commit 升級為 `2406b126`；Manifest 只由 `-GenerateManifest`
產生，不手改 bytes／hash。ZIP 以單一頂層目錄重建，解壓到新的安全暫存位置後，使用 ZIP 內
verifier 驗證並逐檔對照 Manifest。套件不得包含秘密、個資、runtime log、bin／obj 或發佈輸出。

## 6. 非目標

- 不修改已驗收的 application source。
- 不替其他教會決定 OptionSet 順序或硬編碼 Sunny 的 value／label。
- 不把 host patch 當成可直接 `git apply` 的更新程式。
- 不 Commit、不 Push。
