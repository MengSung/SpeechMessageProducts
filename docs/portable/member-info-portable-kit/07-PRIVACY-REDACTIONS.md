# 隱私與可攜性遮罩說明

## 為什麼需要遮罩

本套件會被拖入其他教會的 repository，內容不能攜帶目前教會的會友識別資料，也不能把目前電腦的路徑、連接埠或 PID 當成目標版本指令。來源文件、提示詞、測試 fixture 與歷史 patch 曾重複使用相同範例；若只改其中一份，仍可能從其他層還原原值，因此封裝前對整個套件逐檔執行一致的 privacy review。只有實際命中下列規則的內容才做置換；零命中的檔案可在審查後保持 byte-identical，不能把「已審查」一律寫成「已改寫」。

## 遮罩範圍

- 可能對應真實會友、區長或小組長的姓名，改成 `會友甲`、`區長甲`、`小組長甲` 等角色化泛稱；相同 fixture 在文件、測試與 patch 中使用一致 token。
- 可能對應真實組織的名稱，改成 `範例小組甲`、`範例牧區` 等無原名語意的泛化 fixture；疑似由姓名衍生的組織名稱也一併泛化。
- 目前電腦的 repository／worktree 絕對路徑，改成 `<來源儲存庫根目錄>`。
- 使用者桌面的參考圖片路徑，改成 `<本機參考圖片路徑>`。
- 固定 localhost 連接埠與程序識別碼，改成 `<本機連接埠>`、`<程序 PID>`。

遮罩流程不建立、不保存、不交付任何原值↔泛化 token 的 mapping／對照表，避免套件本身成為還原個資的來源。

## Privacy review 與條件式衍生狀態

- `03-PROMPT-HISTORY-VERBATIM.md`：實際命中機器專屬值／識別資料並完成置換，因此是 sanitized derivative；除明確安全遮罩外，保留 session 可見文字、順序、標點與圖片 marker。
- `original-specs/`、`original-plans/` 及 `reference-implementation/feature-files/`、`reference-implementation/tests/`：全部逐檔 privacy-reviewed；只有實際含可識別人員／組織 fixture 的檔案才置換並標為 sanitized derivative。`original` 表示來源類型，不代表所有檔案都被改寫，也不單獨保證 byte-identical。
- `reference-implementation/host-integration/01-photo-prerequisite.patch` 與 `02-member-info-2026-07-15-plus.patch`：fixture-bearing 內容曾實際置換，保留 raw commit lineage／hunk 脈絡的同時屬 sanitized derivatives，不能由記錄的 raw diff 命令 byte-for-byte 重建。
- `original-specs/2026-07-17-member-info-resizable-sortable-columns-design.md` 與 `original-plans/2026-07-17-member-info-resizable-sortable-columns.md`：是第 9 份歷史 Spec／Plan 的 byte-identical、privacy-reviewed copies；掃描結果為 zero replacements。其歷史姓名寬度／最小寬度與舊欄序已由第 10 份規格明確取代，但原始 bytes 仍保留供決策追溯。
- `original-specs/2026-07-17-member-info-column-order-group-metadata-design.md` 與 `original-plans/2026-07-17-member-info-column-order-group-metadata.md`：是來源 commit `589f0baa3d53588ffd60c6c602472bd0779ef2e8` 新納入的第 10 份 Spec／Plan。privacy scan 若沒有敏感 token，交付副本必須是 zero replacements 且與來源 byte-identical；不得為了「看起來已遮罩」改寫 62px、欄序、CRM logical names、DTO 名稱或驗收文字。
- `original-specs/2026-07-18-member-info-commitment-type-sorting-design.md` 與 `original-plans/2026-07-18-member-info-commitment-type-sorting.md`：是來源 commit `2406b126e989cc980e8cada9da0e07a2ede1e08d` 新納入的第 11 份 Spec／Plan。Spec 掃描零命中並與來源 byte-identical；Plan 命中目前 worktree 絕對路徑、固定連接埠與 PID，交付副本已改為 `<來源儲存庫根目錄>`、`<本機連接埠>`、`<程序 PID>`，因此是 sanitized derivative（source `8bb9907393bec7d4e4c2fc963b3aa9750cae016b86d1ebd3046f36d06d00f805` → delivery `56bef9a5860386913e7879e1222760fbbe368316a31ca834baa05941fd7d5765`）。`牧師師母` 等 OptionSet 角色 label 與 `100000006` 等 schema 識別值只用來證明「value 大小不等於 configured order」，不代表真實會友個資，也不得被其他教會當成固定設定。
- 由同一來源 commit 更新的 `reference-implementation/feature-files/` 與 `reference-implementation/tests/` snapshots：每個檔案先獨立掃描；其中 10 份零命中並保持與對應來源 byte identity，`MemberInfoTreeSearchBuilderTests.cs` 命中姓名 fixture，已一致泛化為 `會友甲`／`會友乙` 等角色 token，成為有明確 lineage 的 sanitized derivative（source `b752fdf81ab343738499f313eec2139bc1dedda853b01f2b3a5ac30cfcd8e9f8` → delivery `a365b2f0f41184bd042c5339685fa44c920526c6b6eb4f29a1148123a6b993d5`）。不得把其他零命中 snapshot 一律改寫。
- `reference-implementation/host-integration/04-member-info-resizable-sortable-columns.patch`：是 `526b533d4b37644df8ed7bd6332ac5df2e4336f6` → `b238d96871fdd490a2a0493e27869753e86baae8` 的 raw、path-limited mechanical diff；privacy scan 結果為 zero replacements，未改寫 commit lineage、hunk 或 bytes。它仍只供閱讀與差異追溯，因綁定精確 endpoints 與來源版 DevExtreme 行為，不可直接盲套到其他版本。
- `reference-implementation/host-integration/05-member-info-column-order-group-metadata.patch`：是 `a7f497bd2ac69cd7c2af2bcc76be40bc71967a63` → `589f0baa3d53588ffd60c6c602472bd0779ef2e8` 的 raw、path-limited mechanical diff。privacy scan 若沒有敏感 token，必須 zero replacements 且保持 raw patch byte identity；任何內容置換都會使它不再是 raw patch。此檔只供 **EVIDENCE-ONLY**，不可直接套用到其他版本。
- `reference-implementation/host-integration/06-member-info-commitment-type-metadata-order.patch`：lineage 是 `589f0baa3d53588ffd60c6c602472bd0779ef2e8` → `2406b126e989cc980e8cada9da0e07a2ede1e08d` 的 13-path mechanical diff。Raw source 掃描命中姓名 fixture，交付檔已使用與測試 snapshot 一致的角色 token 泛化，只保留 path／hunk／技術脈絡，因此明確是 sanitized derivative，不宣稱 raw byte identity（raw `bd12b70d6d465ebe00da7aa1b4dc11eeb5e09a5a6096bf6ea2b508c6e79d988b` → delivery `45b86e0185329b8db94129f3a1296b426d6c61be90c476e0bfca192c4e611240`）。
- `reference-implementation/tests/ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`：來源命中可識別姓名與姓名衍生小組，交付副本已一致改為 `區長甲`、`區長乙`、`甲區`、`乙區`，因此是 sanitized derivative；來源 SHA-256 與交付 SHA-256 分別記錄在 SOURCE-MAP，不宣稱 byte identity。
- `reference-implementation/tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs`：來源中的三個姓名 fixture 已泛化為角色 token；ContactId、rank、授權、去重與姓名次排序關係保持不變，因此仍可作為相同行為契約。
- `original-plans/2026-07-16-member-info-layout-search.md`、`2026-07-16-member-info-mobile-responsive-typography.md`、`2026-07-16-sort-unassigned-district-last.md`：來源中的固定本機連接埠已改成 `<本機連接埠>`，因此是 sanitized derivatives；功能步驟與驗收語意不變。

Manifest 的 `sourcePath` 只記錄 lineage；它本身既不宣稱交付檔案與來源 SHA 相同，也不宣稱兩者不同。每個 artifact 的 zero-replacement／sanitized-derivative 狀態依本節與 reference provenance 說明判定；交付檔案本身的位元組數與 SHA-256 由 manifest 驗證。

## 沒有遮罩的內容

- API、class、method、DTO、CRM logical name、OptionSet schema 識別值、套件版本與 Git commit ID（包括來源 commit `2406b126e989cc980e8cada9da0e07a2ede1e08d`），因為它們是遷移所需的技術契約，不做遮罩；但其他教會仍必須讀取自己的 metadata，不可複製 Sunny 的值或順序。
- `ChannelAccessToken` 等組態 key 名稱與 `m_Password` 等既有欄位名稱；套件只保留名稱，不含實際 token、密碼或連線字串值。
- `fake.*`、重複數字 GUID、`會友甲` 等明確合成測試值。

## 在其他教會使用時

不要把泛化 fixture 改成真實會員資料後提交測試或 Prompt。目標版本的驗收應使用該教會核准的匿名測試資料；需要人工截圖或 log 時，也應先遮罩姓名、電話、生日、照片、LINE ID、token 與 CRM record ID。
