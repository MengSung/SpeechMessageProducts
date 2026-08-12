# P7.3 ChurchReport 特殊資源能力遷移：審查紀錄

## 結果

- 本機 review：通過，未保留 Critical/Warning。
- 外部雙模型 review：未完成。已使用核准的 self-healing runner 啟動；45 秒上限內僅有 health/prompt
  artifact，沒有 Gemini 或 Claude 的可用 finding/summary，因此依規則停止等待並明確標記降級。

## 已驗證的安全契約

1. `ProfileAlias + GenerationId` 參與 metadata cache 隔離；cache 只保存 bounded immutable pure values，
   不保存 SDK metadata graph、session、credential、request 或 response。
2. 五項 operation 均為固定 allowlist，Gateway/ProductClient 不接受任意 entity、field、FetchXML、cookie、
   endpoint 或 credential。
3. image input/output 使用 defensive copy，僅允許 PNG/JPEG，並驗證實際 decoder format、byte、width、
   height 與 pixel limits。
4. weekly paging 使用 server-owned query；cookie 不越過 connector request，cancellation／over-limit／
   schema mismatch 不產生 partial success。
5. connector `Succeeded=false`、invalid response、cancel、exception 或 timeout 都令 lease faulted，避免未知
   Data8/WCF session 回池；focused regression test 驗證 dispose 與 permit release。
6. ChurchReport consumer、feature gate、ToolUtility reference 與 CE 流量均未改動，因此沒有將本機 capability
   誤宣稱為 cutover、CE proof 或 legacy removal。

## 驗證命令

- `dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore`：通過。
- `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore`：通過，0 warnings／0 errors。
- `git diff --check`：通過。
- 23 個 task-owned `.cs`：UTF-8 無 BOM、CRLF-only、final CRLF。
