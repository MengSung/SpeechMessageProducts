# P1-04 Activity接續檢查點（未實作／未搬移）

## 已確認與證據

- 前切片Attachment本地提交 `31ab8cea`；本文不重做其已完成離線證據，也不宣稱主P1完成。
- Activity兩產品原檔尚未修改，仍在ToolUtility；已完整閱讀服務／介面及精確Facade委派，無讀取或外傳敏感Facade全文。
- 分析 `20261105-204755-integration-p1-04-activity-analysis-analyzer`：Claude週額度阻擋，
  Gemini單模型降級，dualModelComplete=false／quotaBlocked=true／degradedFallback=true。
- 原host六個特性測試已綠：分組查詢golden／逆序回應重排、同名不同ID保留、現有前綴append不Clear、
  無partyid的addressused略過、純ID無SDK I/O，以及兩組SetState各default／external多載。
  `artifacts/p1-04-activity-before/before.trx`為6Activity＋9CoreTarget＝15/15；net48CoreTarget另1/1。
- 特性測試僅鎖合法成功行為，沒有把下列安全缺陷做成通過期望；沒有SDK／D365實機驗收。

## 已核實的既有風險（阻擋Activity準入，不是已修復）

1. 不同表共用GUID時，Dictionary<Guid,Entity>覆寫導致錯表結果。新識別須包含logical name＋正式GUID，
   不按顯示姓名或GUID單欄合併異質記錄。
2. 先改caller的partyTypeList再SDK I/O，故障後部分發布；缺查詢結果亦讓type／value不對齊。
   純ID解析途中cast失敗也可能留下前半段。需先建局部候選，所有驗證／I/O成功後才append。
3. SDK根表、缺／重複ID、未請求ID、MoreRecords未完整、重複輸入參照、既有前綴身份與對齊，均待安全紅綠保護。
4. logger未使用卻保存；SDK須保留給default路徑但只能限原host授權scope且有清除／drain契約，不能Dispose借入SDK。
5. 固定英文wrapper與同一InnerException待修；SetState空ID／無效回應／fault不能假成功或盲目重試。

## 分析建議的核實與不採用項

- 採最小相容方案，不新增一組沒有caller需要的泛型API；原namespace／舊ctor／六方法簽章保留。
- **不採「先Clear再AddRange」**：原合法行為為append，已有6案中的prefix斷言；清除會無聲刪舊資料。
- 兩個ArrayList無法跨物件原子swap；必須明載caller獨占、不可並行enumerate／修改。
  先候選／驗證再正常成功append，不能宣稱任意自訂ArrayList、OOM或違約並行下也有跨物件原子性。
- 缺partyid但addressused可能是外部收件者；沿用略過，不因新的記錄ID規則為其補造正式ID。
- SDK自身ArgumentException仍應包裝保留InnerException，不按例外型別一律重拋破壞既有API。
- 容量／chunk／完整分頁策略尚待以有界本地預算及官方SDK證據定稿；不可把猜測上限宣稱D365實測。

## 接續順序

1. 先補上列安全紅測與錯頁／SDK故障／前綴不變／跨表同GUID案例，核對真正失敗。
2. 原host修正、重用EntityQueryService正式識別驗證、不複製驗頁或保存結果；測並行A/B與清除／借入Dispose=0。
3. 全部修後搬前綠，再git mv兩產品檔至Core；將特性與安全測試納入net48白名單，不刪host既有測試。
4. 新鮮雙目標反射、coverage、完整00§8、真公開委派與自癒review，再更新準入狀態。

正式計畫沒有修改，無Word修正報告需求；VM／D365／LINE未操作。
主P1維持2/12，P1-04與Activity均未驗收；本文件是可接續檢查點，不是最終交付。


## 本次準備收尾

最新全方案Release建置與16組件／目標集合2278/2278、0失敗／略過，
ToolUtility413/413與518/518、ChurchReport623/623、架構67/67；沒有只使用舊DLL。
`artifacts/p1-04-activity-baseline-section8/`及`evidence/activity-preparation/`保存摘要。
205806特性測試review為Gemini單模型降級，無Critical／High／Warning；Claude仍週額度，未取得其review。
分析所列兩個既有Critical尚待安全修正，不能以測試準備review通過代替產品修復。
本輪CoreTarget文件校正不改執行碼，附件Core產品與前輪coverage來源未變。

最新Changed編碼790 checked／758 raw排除，仍只有非本切片p013任務索引MissingFinalCrlf；不修改或提交它。本次19份自有文字以未修改repo byte-validator驗證0問題，來源／文件whitespace通過；不能把局部結果稱完整Changed通過。
