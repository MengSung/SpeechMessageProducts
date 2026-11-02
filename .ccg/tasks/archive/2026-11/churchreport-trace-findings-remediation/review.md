# F2 無 Session 快取隔離審查紀錄

日期：2026-08-22

## 變更範圍

- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
- `ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs`

## 外部審查

已透過 `Start-CcgDualModelRun.ps1` 執行自我修復審查；產物位於
`.ccg/dual-model-runs/20260822-110201-churchreport-trace-remediation-f2-review-reviewer/`。

- Gemini：兩次嘗試皆產生可用審查報告；沒有 Critical，六個 getter 的無 Session 後備路徑、有 Session
  key 組成、legacy wrapper 範圍與 1,000 次回歸測試均核對通過。
- Claude：兩次嘗試皆為 `no-usable-output`，自我修復 runner 已重試但未取得結果。
- 結論：本次是 Gemini-only 的降級審查，**不可宣稱雙模型審查完成**。runner summary 的
  `ok=false`、`degradedFallback=false` 與 `fallbackAccepted=true` 已保留供後續追查。

Gemini 的唯一 Warning 指稱新測試檔為 Big5；在修改後以位元組層級檢查確認兩個 C# 檔均為有效 UTF-8、
無 BOM、全 CRLF、以 CRLF 結尾，且沒有 PUA 或 replacement character，因此此 Warning 已排除。Gemini
提出為 `GetCurrentSessionId()` 加上 `Obsolete` 的 Info 未採納：其仍由七個範圍外 legacy getter 使用，
加入標記只會在既有呼叫點產生非必要編譯警告。

## 本地驗證

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --filter FullyQualifiedName~InMemoryDataContextSmallGroupCacheIsolationTests -p:BaseOutputPath="ChurchReport.MemberInfo.Tests\\bin\\f2-focused5\\"
```

結果：1/1 通過。

```powershell
dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --no-restore -p:OutputPath="$env:TEMP\\churchreport-f2-compile-final2\\"
```

結果：0 warnings、0 errors。

完整 `ChurchReport.MemberInfo.Tests` 仍有已知 22 項既存失敗，主因為舊測試硬編碼
`ChurchReport.sln`／原始碼路徑與付款組件命名契約；它們不屬 F2 範圍，未藉此擴大變更。

## 結論

沒有待修 Critical。F2 將無 Session 路徑改為 Scoped data context 實例後備物件，避免寫入程序級
`IMemoryCache` 的一次性 Ticks key；後備資料隨 scope 結束失去唯一持有者，沒有跨 request、使用者、
profile 或 tenant 的保留路徑。
