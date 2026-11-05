# P1-04 Activity 切片離線驗證紀錄

狀態：Activity 兩型別已抽取及完成主會話審查；P1-04 整包未完成，P1 仍 2/12。

## 需求與實作

| 需求 | 實作位置 | 驗證及證據 | 狀態 |
|---|---|---|---|
| 原 host 成功行為先綠 | ActivityCharacterizationTests／三份 activity golden | 原 host 六案、before.trx；群組查詢／逆序回應／前綴 append／純 ID／四 SetState | 通過 |
| 修異質表同 GUID 覆寫 | Core ActivityService 的複合識別及 ReadBatch | ActivitySafetyTests 原51全紅→57綠；跨表兩實體都正確保留 | 通過 |
| 完整消費集合及兩清單發布 | ValidatePrefix／ReadReferences／AppendComplete | 前綴衝突、錯型、後組／後頁故障、缺／重／錯表／多餘列，兩清單原內容不變 | 通過 |
| 有界 batch／完整分頁 | ReadBatch／AddPage | 500／500／1、不截斷；MoreRecords丟棄probe後主鍵排序重讀、cookie／無cookie；跨頁重複／空進度／timeout拒絕 | 通過 |
| 同 scope 借用及資源釋放 | ActivityService.Dispose／Facade既有子服務Dispose分支 | 1000 A/B、59故障重試、2059 SDK calls、active=0；weak references、Dispose競爭、borrowed Dispose=0 | 通過 |
| 保留活動1/4、appointment3/5 | ExecuteState及四公開狀態多載 | golden、空ID／null回應拒絕、SDK timeout／ArgumentException同InnerException | 通過 |
| 雙目標與跨組件呼叫 | Core兩產品檔、Framework測試白名單、CoreTargetFrameworkTests、ActivityCallerRouteTests | net48實載net462；495/495、net10 602/602；真ToolUtilityClass→Facade→Lazy→Core七案 | 通過 |
| 全方案相容／品質 | 00§8及獨立coverage | 16TRX2444/2444，ChurchReport623/623、架構67/67，Core99.56%／100%，strict零警告／錯誤 | 通過 |

## 相容與安全決策

六方法、namespace、舊ctor與正常append保留。缺partyid且帶addressused可為外部地址，仍略過，不生成正式ID。
不採先Clear再AddRange，不以姓名去重或把缺列當空結果。既有同表同ID重複改為明確拒絕，跨表同GUID合法。
logger只作ctor null相容、不保存；default路徑需要的SDK限原host scope保存，Dispose清除但不處置借入SDK。
原Facade已有activity IDisposable分支，七個真公開委派測試確認有執行，不需修改敏感Facade全文。
真故障固定英文wrapper、保留同一原InnerException，交上層共用通知邊界，不在Core另建LINE client。

兩個ArrayList無法跨物件原子swap。caller必須獨占兩個普通清單，不並行修改／列舉；拒絕alias、subclass、read-only／fixed wrapper。
先完成候選、兩個object[]與兩Capacity配置，再執行普通AddRange；不宣稱強制中止執行緒或違約並行下也有原子性。
來源5000容器、含前綴輸出5000列、每批500為本地預算，不是D365組織實測值。
AllColumns已反序列化的傳輸配置不能事後撤銷，host仍須限制傳輸量／逾時／併發。
完整分頁不代表跨頁資料庫快照；變動造成重複或缺列時整體拒絕，供caller明確重試。不自動重試未知寫入。

## 執行順序與結果

1. 前切片Attachment本地31ab8cea、Activity準備63878c9a保持不變。
2. 原host六特性先綠；新增安全51案全紅，沒有用編譯錯誤冒充紅測。
3. 修後57/57，加入生命周期後82/82搬前綠，再git mv兩型別至Core；原host測試全保留。
4. 首次Core strict拒絕CA2208／CA1510／CA1859，修真實參數名稱、條件API及具體回傳型別後兩產品0警告／錯誤；未放寬設定。
5. 最終495/602雙宿主、7公開委派、每目標新目錄各恰一份coverage99.56%／100%，完整16TRX2444/2444、0失敗／略過。
6. tracer7＋正TRX、I18n verify、工具108/27、plugin metadata10／一次性key build6、package34專案／158參考／0失敗、duplicates28專案／941檔／0新舊群組。
7. 頁面8/8，無登入Edge1440／390／320、明暗色及鍵盤通過，0頁面錯誤／溢出；外連阻擋，context／browser finally關閉。

產品strict通過不表示舊測試整體strict-clean。完整方案首次build有4個既有obsolete warning；host測試重新編譯亦有既有warning，沒有用NoWarn消除。
證據位於 artifacts/p1-04-activity-{red,all-before-move,dual,caller,coverage,final-section8,browser}/
及 .ccg/tasks/integration-p1/evidence/activity/（提交不含TRX／DLL／金鑰）。

## 審查與授權

使用者最新明確要求「不需要雙模型REVIEW了」，後續不啟動reviewer，主會話逐項審查並保存review.md。
原204755分析及205806準備review是歷史Gemini單模型降級，沒有回寫成完整雙模型。
本輪211941 analyzer仍按開發規範嘗試兩後端：Gemini成功／Claude週額度阻擋，dualModelComplete=false、quotaBlocked=true、degradedFallback=true。
分析並行Warning以明確caller獨占、服務scope owner及實際A/B／drain測試對應；不要求同一輸出可同時讀寫。
主會話未找到本切片尚未修復的Critical／High；沒有宣稱外部review完成。

## 未完成及界線

其他P1-04候選（Collection／Contact／List／Query／屬性及純契約）與P1-01、P1-05～12仍待完成，不封存主任務。
D365／Hyper-V／LINE未操作，沒有實機／hosted CI新證據；正式3.3計畫未修改，無Word修正報告需求。
原Goal仍usageLimited，沒有覆蓋、重建或宣稱背景執行。
外部p013任務索引編碼問題仍須正式Changed守門揭露；不為本切片修改／提交他人索引，不把自有文字通過說成完整Changed通過。

末端編碼：Changed 805 checked／769 raw excluded，唯一問題仍是非本切片p013索引MissingFinalCrlf；本輪30份自有文字以未修改repo byte-validator驗證0問題。來源／文件whitespace與進度8/8通過，不宣稱完整Changed成功。
