# P1-12 實機驗收與可復原前置

沿用使用者明確全自動授權；正式D23定義goodshepherdback是唯一開發測試組織、正式複本，須比照正式個資保護。
最新合法唯讀證據確認兩台VM可PowerShell Direct連入，API組織ID與本客體MSCRM_CONFIG的goodshepherdback完全一致。
實際DB名為GoodShepherd_MSCRM，不根據組織alias猜DB；VM亦有其他組織，不還原VM、不重啟或調整網域／帳號／安全政策。

1. 留存用途／組織ID／SQL映射及服務版本證據；只診斷D365DC01必要服務，不改DC。
2. 目標沒有現存backup歷史。使用SQL既有備份目錄建立唯一COPY_ONLY/CHECKSUM/COMPRESSION full backup，另外備份MSCRM_CONFIG；不覆寫、不打斷既有備份鏈。
3. RESTORE VERIFYONLY後，在全新P1RecoveryProbe_<GUID>資料庫驗證真正復原，所有資料／log檔使用唯一GUID路徑，禁止WITH REPLACE、禁止原DB名稱，RESTRICTED_USER且不註冊D365。
4. 檢查暫存DB ONLINE及DBCC CHECKDB PHYSICAL_ONLY；finally僅清理正式名稱與所有檔案路徑均吻合本run的暫存DB，保留兩份備份。未能清理即阻擋後續apply，不能宣稱資源已回收。
5. P1-11全部必要本機gates與自審完成後，重新唯讀plan，逐項審核hash與只新增內容；只用正式Deployment入口apply、verify全部key Active，第二輪新plan/apply零新增。
6. 後台確認SpeechMessageCore元件及安全／稽核／日期／樂觀並行政策；正式資料庫唯一鍵拒絕第二筆new_localizedtext，限定自己的合成資料與正式ID清理。不得發LINE或改舊記錄。
7. 逐項驗收、SHA證據／進度頁一般資訊、本地精確commit及子任務封存。P1-12無真正證據前不標完成。

備份與復原檢查只是實現既有安全前置，沒有改正式需求；不需Word計畫修正報告。
所有秘密只由既有User Secrets讀取、僅活於短程序；輸出不含密碼、token、個資或備份內容。備份留在客體既有SQL備份位置，不提交或送外部模型。

## 唯一性實機驗收入口
為落實R11與全自動可重跑驗收，既有Deployment的smoke明確帶--schema時進受限P1探測，無schema仍維持P0。先Verify已發布結構與四key Active，再於本命令租約／組織寫鎖內建立本次GUID標記account及new_localizedtext。固定fixture來自repo，內容Org.Level.Group／*／細胞小組，不接受任意業務表或payload。
只有官方DuplicateRecordEntityKey 0x80060892才算成功拒第二筆；其他Fault／成功建立第二筆均失敗。候選ID與標記先flush到ownership，再確認不存在後Create；即使回應遺失，也依本次ID與標記核對後清理，文字先於教會。cleanup不受caller取消阻斷，失敗聚合並保留恢復證據，不假稱成功。所有SDK同步返回才歸還lease。
此為正式P1-12驗收的實作方式，沒有修改需求／降低條件，不需Word修正報告。before與metadata snapshot仍非SQL備份；真正備份／獨立復原證據已取得。

## 實機第二差異
第一次受限補正於candidate planner階段因複合key欄位順序誤判而停止，沒有任何補正寫入。已核對三欄集合完全相同，只是D365回傳順序不同；新增排序正例及缺欄／重複／替換三反例（1紅/3綠），改canonical排序與完整集合比對，沒有刪除唯一性或身份斷言。全部Deployment138/138。原完整before與88項plan仍保持原始bytes/hash；第二輪受限補正使用相同原始證據，不重放舊新增plan。

## Solution根元件實機語意
官方solutioncomponent.rootcomponentbehavior=0包含子元件，1不含，2僅外殼。實機四新表根為0且無顯式欄/key列；既有account/contact根為1並有精確欄位列。投影新增nullable RootBehavior/RootId（Guid，非EntityReference），null不序列化維持原before hash；planner只以同entity正式ID/根0承認真實子元件。未知/1/2/別表/非根仍要求精確列，所有欄位約束及key Active檢查不變。新增7案先1真紅6綠，修後完整151/151，另SDK投影故障及真引擎二次零寫入案例。

## D3659.1通用唯一性故障的差分驗收
真實9.1.45.11的第二筆相同鍵回官方DuplicateRecord 0x80040237；SQL本文隱藏，不能解析或輸出，不能將任意SQL fault當通過。已唯讀核對實體索引ndx_for_entitykey_new_ak_localizedtext為啟用的唯一索引，三欄恰為account/culture/key。正式probe只在已發布完整verify及4key Active後執行：對0x80060892保留明確拒絕；對0x80040237另需精確查回本次account、第二ID不存在、原三欄tuple僅原第一ID，並實際建立/讀回同鍵不同culture=zh-TW控制列，重查原tuple仍唯一。控制故障、回應遺失、錯集合、第二筆已提交及caller取消皆拒絕並finally清理；原fault與控制故障聚合保真。第四候選ID也先寫ownership，倒序控制文字/第二/第一/教會清理，正常取消分類不變。結果記實際錯碼與LegacyIntegrityDifferentialPassed，不偽填0x80060892。5案真紅後綠，增加控制列取消實測，共157案通過。這是以更多服務端證據替代原先不適用的錯誤碼假設，不降低唯一性/清理驗收。
