# P5-03a 佈署清單與影子切換

## 目標
依 05A 修訂 1 完成 Deployment 的 manifest v2、活動註冊、舊註冊唯讀匯出、`switch` 與 `shadow` 離線契約；實機只允許測試組織 `goodshepherdback`，且必須先確認 User Secrets 的 `ShadowTestChurchId`／`ShadowTestContactId`，不以名稱猜測。

## 已確認約束
- `switch --to legacy` 先自動 export，先通過 D5-18；一次只啟用一個模組的舊步驟，新步驟必須全停用。
- `switch --to enabled` 只啟用新步驟；本包禁止 Live 模式，所有新步驟必須 Shadow。
- `switch --to off` 必須在成功、失敗、取消後都停用舊／新步驟並還原追蹤設定。
- `shadow` 固定先舊後新兩輪：舊輪建立一批 seed、trigger、observe、刪除、停用；新輪使用另一批同樣情境、Shadow trace parser；兩輪互比且各自與 expected 比對。
- 不碰正式組織；測試組織 `goodshepherdback` 依修訂 1 與 D5-18 可直接推 LINE，舊新不得同時啟用，離線測試不得冒充實機證據。

## 驗收條件
1. manifest v2 可表達多組件、多步驟、images、legacy 與活動註冊，缺欄位／重複 ID／Shadow secure 設定會拒絕。
2. `export-legacy` 只讀輸出舊步驟摘要，不輸出 token、LINE ID、訊息正文或秘密。
3. `switch` 具組織白名單、模式互斥、D5-18 關卡及 finally 還原；任何未允許組織拒絕且零寫入。
4. `shadow` 具 bounded trace、overflow 不可比對、取消／例外還原、seed 清理與雙輪比較契約。
5. 活動第一輪的讀取／註冊流程以合成 SDK 測試驗證；不把 User Secrets 缺鍵補成假 GUID。
6. 每個綠燈小段以 `P5-03a` 本地提交；整包驗收與固定 guard 通過後才推送並以 ls-remote 核對。實機前以既有 evidence 或唯讀權威查詢確認測試綁定及 parentcustomerid 後，把兩個 Shadow ID 放入 User Secrets；查不到才問使用者。

## 不在本包
- 不在任何正式組織啟用步驟。
- 不實作 P5-03b 的 UI 匯出入與完整活動測試流程。
- 不啟動 P5-04 以上模組。

## 唯一驗收清單（05A P5-03a 與 §4A）

以下同名測試已存在，截至 2026-11-14 22:47 Asia/Taipei (UTC+08:00) 已驗證的 Deployment Release 全套 674／674 包含這些案例；這不等於整包驗收。

- [x] `Shadow_SameFields_NoDifference`
- [x] `Shadow_DifferentValue_Reported`
- [x] `Shadow_OnlyOneSide_Reported`
- [x] `Shadow_Overflow_NotComparable`
- [x] `Shadow_MissingEndMarker_NotComparable`
- [x] `Shadow_VolatileField_ComparedByPresence`
- [x] `Shadow_FailureMidRun_RestoresStepsTraceAndData`
- [x] `Manifest_V2_MultipleStepsPerType_Planned`
- [x] `Manifest_Images_CreatedAndVerified`
- [x] `Manifest_Activity_RegisteredWithGroup`
- [x] `Manifest_UpdateWithoutFilter_Rejected`
- [x] `Manifest_ImageWithoutAttributes_Rejected`
- [x] `Manifest_ShadowWithSecureConfig_Rejected`
- [x] `Manifest_PublicPluginWithoutStep_Rejected`
- [x] `ExportLegacy_ExcludesOwnAndMicrosoftAssemblies`
- [x] `Switch_NonWhitelistedOrganization_Rejected`
- [x] `Switch_EnabledThenOff_ReturnsToManifestState`
- [x] `Switch_ModuleWithLiveStep_Rejected`
- [x] `Switch_Legacy_EnablesOnlyListedLegacySteps`
- [x] `Switch_Legacy_RefusedWhenMemberLineIdsNotCleared`
- [x] `Switch_Legacy_RefusedWhileNewStepsEnabled`
- [x] `Shadow_LegacyRound_AlwaysRestoresLegacyDisabled`
- [x] `Shadow_TwoRounds_ComparedAfterApplyingIntents`
- [x] `Shadow_TestToken_NeverWrittenToOutputOrReport`

## 完成定義（00 §7.1，不得加碼）

- [x] 1. 本包上述同名測試全部通過；Deployment 為 net10.0 單目標，Release 674／674。
- [ ] 2. 00 §8.1 全部命令在固定 guard `C:\smv\guard` 通過；本包尚未完成，不能沿用 P5-02 的守門。
- [x] 3. 本包沒有新增專案，覆蓋率條件不適用。
- [ ] 4. diagnostics enabled → smoke → off → verify=0、步驟 ID 不變、殘留 0 與一頁操作紀錄；F16 已確認為 Microsoft ActivityFeeds 分類誤判，修正後只重跑唯讀匯出，仍未執行本項。
- [ ] 5. 子任務 `wp-reports\` 的一頁驗收回報與進度頁；目前只有進行中前置紀錄，不能標驗收。

## 尚缺產物與時間盒

- `shadow` 的 new／reading／legacy-golden CRUD 新輪 CLI、同步 RequestId、關聯 observe 與 Create 別名已接通；26 項新測試含舊輪 SDK 關聯讀回、歧義拒絕、同輪 Create／Delete 的完整意圖及送達後失敗／取消的子列清理。受控 `switch --to legacy` 及 live 基準受審 token 盤點接線、推播／外部修改摘要、映像接線與同步實機追蹤識別仍待完成；全部仍是離線證據。
- P5-03a 上限為 1 天；P5-03 已依 D5-11／D5-12 拆 a、b。原 2026-11-14 23:30 Asia/Taipei (UTC+08:00) 驗收估計不再承諾，離線契約完成後重新估計，不虛填完成時間。
- 39 個啟用步驟已以公開簽章、managed 狀態及 ActivityFeedsCore／microsoftdynamics 的正式方案關聯確認為 Microsoft ActivityFeeds；`0323f4cd` 修正窄分類後，唯讀 CLI 回 0、舊步驟與流程啟用各 0，沒有變更遠端狀態，無需使用者處置。
