# P7.4 MemberInfo 完整聯絡人頭像回應邊界：品質檢查

## 實作結果

`ORG-CALL-00028` 新增獨立的 `memberinfo.contact.retrieve.image.display` derived mapping，
不改寫 immutable 70-row `normalizedCallSites`。Data8 對已授權 contact 執行唯一固定 projection，
以 image、精確 allowlist 的 LINE HTTPS redirect、或 default avatar 三選一 closed union 發佈。
ChurchReport route 維持 deployment-owned Package03 base/sub gate 均為 `false`；gate 是第一個
decision，true branch 才依 server scope、GUID locator、target authorization、固定 profile/workload 的
順序 dispatch typed ProductClient。

## 驗證結果

| 項目 | 結果 |
| --- | --- |
| Registry agreement RED | schema root 未要求 `derivedOperationMappings`，4 tests 中 1 failed，原因與預期相符。 |
| Registry agreement GREEN | 修正 schema required 後，`OperationRegistryAgreementTests` 4/4 passed。 |
| Dynamics Release tests | 836 passed、7 skipped（既有 live SQL 類測試未啟用）。 |
| ChurchReport MemberInfo Release tests | 634 passed、14 skipped（既有 live CE/fixture 類測試未啟用）。 |
| Solution Release tests | passed。 |
| Solution Release build | 0 warnings、0 errors。 |
| Encoding | 18 個本 child 實質變更的 C# 檔均為 UTF-8 無 BOM、CRLF-only、final CRLF。 |
| Static boundary checks | 兩份 checked-in setting 的 display sub-gate 均為 false；新 request-local service 與 route 未命中 ToolUtility、CRM SDK、`IMemoryCache` 或 legacy fallback 禁止字樣；`git diff --check` passed。 |

## 審查與修正

既有 CCG final-review 在每次 45 秒預算內未形成完整雙模型結果，故記錄為「雙模型未完成」。
可用 reviewer output 指出 ChurchReport redirect 應與 connector 同樣拒絕 non-default port，已先以
failing regression test 重現並修正 `!uri.IsDefaultPort`；同時已完成 child C# 的 byte-level CRLF
正規化。後續本機 full tests、build、encoding 與 static scans 均再次通過。

## 交付邊界

本 child 只證明 local-disabled implementation，沒有 CE 8.2/9.1 execution、fixture、read-back、
traffic switch、Embedded/Dedicated capacity/parity、soak/drain、P7.5 ToolUtility removal 或 P8
deployment 證據。保持 display sub-gate=false 是 deterministic rollback；因此沒有需要 cleanup 的
外部資料或資源。
