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

以下同名測試已存在，最近已驗證的 Deployment Release 全套 748／748 包含這些案例；這不等於整包驗收。

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

- [x] 1. 本包上述同名測試全部通過；Deployment 為 net10.0 單目標，Release 748／748。
- [x] 2. 33485508 已於固定 guard `C:\smv\guard` 通過完整 00 §8.1；實機條件仍未通過，不等於本包驗收。
- [x] 3. 本包沒有新增專案，覆蓋率條件不適用。
- [ ] 4. diagnostics enabled → smoke → off → verify=0、步驟 ID 不變、殘留 0 與一頁操作紀錄；F16 已確認為 Microsoft ActivityFeeds 分類誤判，修正後只重跑唯讀匯出，已執行本項，但 smoke 未取得 trace，不能勾選完成。
- [ ] 5. 子任務 `wp-reports\` 的一頁驗收回報與進度頁；目前只有進行中前置紀錄，不能標驗收。

## 尚缺產物與時間盒

- `shadow` 的 new／reading／legacy-golden CRUD 新輪 CLI、同步 RequestId、關聯 observe 與 Create 別名已接通；26 項新測試含舊輪 SDK 關聯讀回、歧義拒絕、同輪 Create／Delete 的完整意圖及送達後失敗／取消的子列清理。受控 `switch --to legacy`、live 基準受審 token 盤點、推播及映像已接線；外部修改摘要與同步實機追蹤識別仍待完成，全部仍是離線證據。
- P5-03a 上限為 1 天；P5-03 已依 D5-11／D5-12 拆 a、b。原 2026-11-14 23:30 Asia/Taipei (UTC+08:00) 驗收估計不再承諾，離線契約完成後重新估計，不虛填完成時間。
- 39 個啟用步驟已以公開簽章、managed 狀態及 ActivityFeedsCore／microsoftdynamics 的正式方案關聯確認為 Microsoft ActivityFeeds；`0323f4cd` 修正窄分類後，唯讀 CLI 回 0、舊步驟與流程啟用各 0，沒有變更遠端狀態，無需使用者處置。

## 2026-11-15 02:20 Asia/Taipei (UTC+08:00) 續跑更正

2026-11-15 02:20 Asia/Taipei (UTC+08:00)：P5-03a續作；更正前次判讀：async statecode=3／statuscode=30為成功，不能寫成Plugin執行失敗。唯讀確認原追蹤為Exceptions(1)；本輪暫改All(2)並讀回，再執行enabled→smoke→off→verify。smoke三分鐘未取得trace回1，off及verify回0，原追蹤1已還原，同一步驟停用、測試task為0；時間窗有1筆該步驟成功作業、trace為0，尚未證明job殘留為0。未證實服務故障，撤回要求使用者重啟的判斷，不再盲目重試。離線最近已驗證684/684；2f946479已接通受審token舊輪shadow CLI，獨立legacy switch、推播比較／外部修改摘要與其餘接線待完成。P5-03未驗收、未推送，完整固定guard未跑；驗收時間待剩餘接線及追蹤原因釐清後重估。

## 2026-11-15 02:26 Asia/Taipei (UTC+08:00) 推播比較進度

2026-11-15 02:26 Asia/Taipei (UTC+08:00)：P5-03a續作，45813c77補上runner的推播完整意圖比較；新增3案先紅後綠，涵蓋吻合、內容摘要差異及缺少推播，欄位三方相同仍不得掩蓋推播差異。收件者及Retry Key僅依明確ID別名正規化；Deployment Release687/687、文件1562檔零失敗、變更編碼零問題。CLI情境推播接線、獨立受控legacy switch、外部修改摘要等尚待完成。diagnostics本輪在All(2)下仍無trace，smoke回1，off及verify回0，同一步驟停用、原追蹤1還原、測試task為0；async狀態30為成功，未證實服務故障，不要求使用者重啟，job零殘留尚未證明。P5-03未驗收、未推送，完整固定guard未跑；P5-02工具三小時修正尚未開始。


## 2026-11-15 02:39 Asia/Taipei (UTC+08:00) 推播接線進度

2026-11-15 02:39 Asia/Taipei (UTC+08:00)：P5-03a續作，b8953380接通expected推播摘要至reader／factory／真CLI；新增14項離線測試，含內容差異、缺摘要／錯收件者／不受控Retry Key拒絕及清理。修正Push中繼Entity須為空的parser一致性，保留嚴格驗證。Deployment Release701/701、文件1562檔零失敗、變更編碼零問題。獨立受控legacy switch、外部修改摘要、映像接線及同步實機識別仍待完成。diagnostics在All(2)下未取得trace的既有結果不變；未再實機重試。P5-03未驗收、未推送，完整固定guard未跑；驗收時間待接線及追蹤原因釐清後重估，沒有等待使用者的事項；P5-02工具三小時修正尚未開始。

## 2026-11-15 02:58 Asia/Taipei (UTC+08:00) 映像接線與受控第一輪現況

2026-11-15 02:58 Asia/Taipei (UTC+08:00)：P5-03a續作；a20a1e8c接通公開legacy切換的受控第一輪及確定清理（710項通過），75568edd接通情境映像與正式步驟的別名、欄位及前後值核對，14項新增測試含真CLI舊新兩輪；完整Deployment Release724/724。映像由平台產生，不注入合成context。本輪沒有實機寫入或推送；diagnostics成功作業無trace的原因仍待查，外部修改摘要、同步實機識別、固定guard及P5-03b尚未完成。P5-03未驗收，驗收時刻待證據補齊後重估，無需使用者操作；P5-02工具三小時修正尚未開始。

2026-11-15 02:58 Asia/Taipei (UTC+08:00)：更正02:39的701項證據適用範圍：b8953380曾在測試後的註解編輯漏掉Push意圖加入；a20a1e8c已補回，且在全部程式修改後完整710項通過。本次724項亦為最終程式修改後的完整執行，不以先前測試代替提交版本驗證。

## 2026-11-15 03:50 Asia/Taipei (UTC+08:00) 固定守門與實機停止點

2026-11-15 03:50 Asia/Taipei (UTC+08:00)：P5-03a離線Deployment 748／748；33485508修復queue停止時timer清理競爭，一千輪回歸及AspNetCore 79／79通過，固定C:\smv\guard完整八項全通過。diagnostics實機同一步驟多次未取得trace，成功作業不等於追蹤驗收，依05A §7第3點停重試並請選處置；仍未驗收、未推送。P5-03b離線規劃已提交b5edf560，尚未實作；P5-02工具三小時修正尚未開始。

外部既有列摘要由d2cadf45接通，null／未宣告／未執行明確區分，分頁故障不發布部分報告；fd5ead95維持I18n棘輪。同步RequestId已離線接線，但實機識別仍未證明。guard原始證據為C:\smv\guard-33485508-full.log，SHA-256 CCB3847BEB14AD6F4160AFFBD3D8C9B4809485C62D3608940D1D372547295185。未啟用Live，未重跑smoke；既有成功job一筆，不能寫成job殘留零。
