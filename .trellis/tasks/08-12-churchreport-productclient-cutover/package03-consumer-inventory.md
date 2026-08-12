# P7.4 Package03 特殊資源 Consumer Inventory

## 目的與範圍

本清冊只判定現有 ChurchReport consumer 能否在 P7.4 以「關閉預設、只讀、
server-authorized、DTO-only、可取消且可回復」的方式改走 P7.3 Package03
`IPackage03SpecialResourceClient`。它不是 feature enablement、CE 證據、流量
切換或 P7.5 zero-reference 證明。

P7.3 已提供 bounded 的 contact-image、option-set 與 weekly-meeting-statistics
typed contract；然而 capability 已存在不代表任何 consumer 可安全改走該路徑。
consumer 必須保留既有的授權、回應語意、取消傳遞、profile/generation 隔離及
deterministic resource ownership，且 feature-on 發生 fault 時不得 request-time
回落至 ToolUtility。

## ORG-CALL-00028：MemberInfo contact image

現有 `MemberInfoController.GetContactImage` 先以 `CanViewContact` 驗證 contact，
這一點符合 server-side authorization 前置。不過 action 的既有成功語意有三種：

1. `entityimage` 存在時傳回 JPEG bytes；
2. 未有 image、但有 LINE picture URL 時 redirect；
3. 兩者皆無時依 `gendercode` 傳回性別 SVG avatar。

Package03 `ContactImageResult` 只表示 image bytes 與 media kind；其無 image 的
fail-closed 結果不包含 LINE URL 或 gender。若 feature-on 的 typed fault／no-image
再於同一 request 呼叫 legacy CRM 取得 fallback，會違反 P7.4 禁止 request-time
fallback 的規則；若直接改傳中性 avatar 或 error，又會改變既有使用者可見行為。

此外，此 action 現在以 bounded `IMemoryCache` 快取圖像 bytes；任何後續設計必須
明確界定 cache key 的 profile/generation 隔離、TTL／大小上限、失效與 response
ownership，不得讓單一 contact 的 image 或 fallback 資料跨 profile、使用者或部署
generation 重用。

**結論：temporary-legacy。** 後續需要專屬 read child，以固定的 DTO contract
同時表達 image、LINE redirect 與 avatar fallback 的結果分類，並在既有
`CanViewContact` 後引入可取消 async action、disabled gate、A/B isolation 及
false-gate rollback 測試；不可直接把目前 action 指向 Package03。

## ORG-CALL-00040：MemberInfo commitment-type metadata

`MemberInfoCommitmentTypeMetadataProvider` 目前直接以 CRM metadata service 取得
`contact.customertypecode`，依 1028、2052、再 UserLocalizedLabel 的順序解析標籤，
並將結果放入單一 process-global cache key。Package03 的 option-set client 則使用
server-resolved locale 的 immutable DTO，且底層 metadata cache 的正確 key 需包含
server-owned profile、generation、target、locale 與有界 TTL／eviction 語意。

因此，直接把 provider 換成 Package03 不只改 transport：它會改變 locale priority，
也會讓現有 global cache 在 profile/generation 變更後可能重用錯誤結果。以 legacy
metadata read 作 typed path 的 request-time fallback 同樣不被允許。

**結論：temporary-legacy。** 後續需先建立 metadata-provider 專屬 child：以
server-derived profile/generation/locale 建立有界 cache key、定義 locale selection
parity、傳遞 request cancellation，並用 interleaved A/B profile test、generation
drain／eviction test 和 disabled rollback test 證明行為後，才可切換 consumer。

## ORG-CALL-00063：weekly meeting statistics

Package03 `RetrieveMeetingStatisticsAsync` 回傳 bounded 的 read-only DTO，只有
meeting-statistic ID、名稱、建立時間與週日日期。現有 `PersonalQrCodeUtility` 與
`SundayQrCodeUtility` 卻會取得完整 `new_meeting_statistics` CRM Entity 的動態
sign-on/sign-off 欄位，接著建立或更新出席、設定 relationship 並重新計算週報。

這不是單獨的讀取 consumer：它緊鄰出席／週報寫入、關聯與 aggregate 計算。將 DTO
重新水合為 `Entity` 會重新引入 SDK bridge；只將初步查詢改成 Package03 則仍需要
legacy retrieve 才能完成現有流程，形成禁止的 request-time fallback。它也尚未具備
P7.2 所要求的 idempotency、timeout-after-dispatch read-back、reconcile 與 rollback
evidence。

**結論：temporary-legacy。** 它應由出席／週報 write-family child 擁有，而非 P7.4
read cutover；必須先完成真正的 typed attendance write orchestration 與新 CE evidence。

## 本輪結論

目前不存在可從 Package03 直接遷移的 ChurchReport consumer。此結論不阻擋 P7.4
繼續盤點其他 capability 或修正已切換 read path 的本機品質缺口，但阻擋把上述三項
宣稱為 migrated、enablement-ready、P7.5-ready 或 P8-ready。所有 deployment-owned
feature gates 維持 false；本輪沒有 CE request、mutation、traffic switch、P7.5 或
P8 操作。

## 證據

- `SpeechMessage.Dynamics.ProductClient/SpecialResources/IPackage03SpecialResourceClient.cs`
- `SpeechMessage.Dynamics.ProductClient/SpecialResources/Package03SpecialResourceClient.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs`
- `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs`
- `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json`
