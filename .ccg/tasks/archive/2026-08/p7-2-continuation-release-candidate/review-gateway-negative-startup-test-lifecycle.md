# Gateway 負向啟動測試審查紀錄

## 範圍

- `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`

本次只修正 .NET 10 `WebApplicationFactory` 在預期 startup failure 時的 TestHost disposal race。
純 deployment configuration 負向測試改直接 materialize 正式 startup validator；正向 HTTP、
TestHost 與 Kestrel integration coverage 維持。

## 本機審查與驗證

- focused Gateway boundary tests：58 passed、0 failed、0 skipped。
- complete `SpeechMessage.Dynamics.Tests`：553 passed、0 failed、7 explicit live SQL skips。
- `git diff --check` 與兩個變更 C# 的 UTF-8 無 BOM、CRLF-only、final CRLF byte-level 檢查通過。
- 無 production Gateway、profile、Data8 connector、CE、feature flag 或流量變更。

## 外部雙模型審查

- run ID：`20260812-103840-p7-2-gateway-negative-startup-test-lifecycle-review-reviewer`。
- Gemini：45 秒後 timeout；已有可讀輸出，未列 Critical 或 Warning。
- Claude：session quota，沒有輸出。
- 結論：**雙模型未完成**。此項只能報告為本機完整驗證加上部分 Gemini 輸出，絕不可稱完整雙模型審查；依使用者的 45 秒規則不重試等待。

## 結論

未發現可由目前程式碼證明的 Critical 或 Warning。修正維持 deployment configuration 的 fail-closed
契約，同時避免測試 framework 的 provider disposal 競態遮蔽實際設定錯誤。
