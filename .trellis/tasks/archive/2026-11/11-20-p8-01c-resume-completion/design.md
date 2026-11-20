# P8-01c 執行設計

## 邊界

此任務是既有部署工具對單一核准測試組織的分段遷移與驗收，不新增第二套遷移機制。操作依序經過 build 20 組件檢查、readonly 起點狀態、最小結構差異、migration plan 審查、apply／verify／冪等性以及指定 Live 測試；任何前置失敗均禁止進入後續寫入。

## 信任與安全界線

組織唯一允許值為 `goodshepherdback`。Organization catalog、既有 User Secrets 與部署工具現有 allowlist 是連線設定來源；不得新增組織或從使用者提供的文字推導組織 URL。秘密由工具內部讀取，不列印、不保存至 repo 或證據。每次寫入由既有組織寫入鎖序列化。

唯讀備份只含來源實體、D365 record ID 與教會 ID，與 migration plan 分開保存；不得包含姓名、業務本文、連線資訊或秘密。plan 的每段輸出需與黃金 metadata、備份及當前組織記錄數相互核對。

## 身分與失敗處理

奉獻類別依 08A D8-6／D8-7，以組織選項組 metadata label 與 `legacy-<選項值>` 識別；不可改用程式硬編碼對照、語意猜標籤或合併不同選項值。若多個來源 option set 在相同選項值上出現不同 label，且已核定 alternate key 無法同時表達，維持 fail closed，不提交 apply，也不執行依賴該回填資料的 P8Live 測試。衝突需被記錄為待核定提案及上線前置，不可描述成驗收通過。

任何 apply 若中斷，僅使用原始已審查 plan 的冪等 resume 能力；apply 後 verify 不為零時不得宣告成功。P8Live 的測試資料與流程依既有測試工具命名及清理規則管理。

## 結尾狀態

本任務是組織操作 owner。其結尾檢查必須確認新步驟為 off／shadow、舊步驟與舊流程啟用數為零、待辦與追蹤狀態回到起點、測試資料及流程殘留為零，並以 readonly verify 證明 off。例外僅限 08A D8-5 列明的期望 schema、稽核與類別資料。
