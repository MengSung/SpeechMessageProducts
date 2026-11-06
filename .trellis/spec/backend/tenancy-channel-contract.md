# P1 租戶設定與 LINE 頻道解析契約

## 1. 適用範圍

`SpeechMessage.Tenancy` 為 net10 固定產品部署設定入口。凡新增 LINE token
解析、教會 locale／時區／國家／幣別／品牌讀取，均須使用此入口，不再複製 key
選擇鏈。階段 1 仍使用既有設定鍵；階段 2 的 `new_linechannel` 尚未接入。

## 2. 可執行介面

- `TenantId(string organization, string product, Guid? churchId = null)`。
- `ILineChannelResolver.Resolve(TenantId)`：必填解析，未配置拋固定例外。
- `ILineChannelResolver.TryResolve(TenantId, out ResolvedLineChannel?)`：只有未配置回 false。
- `ITenantSettingsProvider.GetSettings(TenantId)`：必須有正式非空教會 ID。
- `services.AddSpeechMessageTenancy("ChurchReport")`：同產品可重入，跨產品誤註冊拒絕。
- `LegacyLineChannelAccess.GetOptionalAccessToken(configuration, organization, selection, product)`：
  舊靜態入口的薄轉接，不保存設定或結果。
- `ChurchReportLineChannelAccess.GetOptionalTokenFromFactory(static () => Configuration)`：
  連舊 Lazy 設定取得都在產品終端失敗邊界內；真正故障交共用 owner。

## 3. 資料與生命週期

`TenantId` 是 host 已授權範圍的純值，建構不代表完成授權。識別只接受有界 ASCII
字母／數字及 `_-.`，首字須為字母／數字，最多 128 字；拒絕設定路徑分隔符。
resolver 固定產品，不能服務另一產品。不可接受未驗證 request 路由作為權限。

設定與 logger 由 host 擁有，Tenancy 僅借用；沒有新增 provider、reload watcher、
timer、queue、連線、結果快取或背景 Task。每次結果獨立，不能保留至下一次操作。
DI 不接管以 instance 外部註冊的設定根；其原 owner 必須最後確定釋放。

回傳既有 Core `LineChannelCredential`，不另創憑證型別。token 最多 8192 字，
移除單一 Bearer 前綴後由 Core 驗證標頭安全。預設序列化與 `ToString()` 不含
token；只有明確 `GetAccessTokenForLegacyClient()` 可供舊 SDK 取得字串。
`ChannelId` 缺少時為 null，不能以別名捏造 LINE ID。來源判定使用獨立布林，
不能因真實別名叫 `environment` 就錯讀環境頻道 ID。

保留以下四種舊來源政策，不得合併：

| 固定政策 | 順序 |
| --- | --- |
| OrganizationThenDefault | 組織非空 token → 預設 |
| EnvironmentThenDefault | 環境非空白 token → 預設；不新增 CRM 組織層 |
| DefaultThenEnvironment | 預設 token 只有 null 才讀環境；明確空字串維持空 |
| EnvironmentThenOrganizationThenDefault | 環境非空白 → 組織非空白 → 預設 |

預設別名鍵仍為 `LineMessaging:DefaultOrganization`，只有 null 才後備到 Jesus。
token／ID 固定分別為 `LineMessaging:{alias}:ChannelAccessToken/ChannelId`；
環境來源固定為 `LINE_CHANNEL_ACCESS_TOKEN/LINE_CHANNEL_ID`。
後備與缺設定只記固定安全 Warning，不記來源值、tenant、token 或例外本文。

教會設定鍵完整包含 `Tenancy:{organization}:{product}:{churchId:D}:{field}`。
支援 TextCulture、FormatCulture、EnabledCultures（CSV）、TimeZoneId、Country、
Currency、BrandName。缺省 zh-TW／zh-TW／zh-TW、Asia/Taipei、TW、TWD、空品牌。
全部欄位驗證後才回不可變純值；品牌最多 256 字，其他欄位有固定上限及控制字元拒絕。
文化集合先複製再由 Localization 驗證，最多 64 項。明確空時區拒絕。
時區實際映射／DST 及金額幣別白名單仍由 P1-06／P1-07 驗證，不能冒稱已完成。

## 4. 驗證及錯誤矩陣

| 輸入／故障 | 必須結果 |
| --- | --- |
| 組織已配置 | 自身 token 與同來源 ChannelId，不用預設 |
| 組織缺設定 | 保留預設／Jesus 並固定安全告警 |
| 未配置／空白 token | TryResolve false、結果 null；Resolve 明確拒絕 |
| 非法標頭／超量 token | 拒絕，不改用另一頻道掩蓋錯誤 |
| provider 真正故障 | 共用層拋同一原例外，不快取失敗；修來源後可重試 |
| 舊 Lazy 建構或讀取故障 | 產品終端邊界上報原例外後維持舊空結果 |
| 同 scope 後續設定修改 | 已回傳快照保持原值，下次讀取取得新完整候選 |
| DI 外部設定根 | 容器 Dispose 不釋放；設定根 owner 最後恰釋放一次 |

## 5. 正常、缺省與禁止案例

- 正常：已授權 Alpha／ChurchReport，以相同 resolver 讀取自身單次結果。
- 缺省：舊設定沒有 ChannelId，清楚回 null；不宣稱供應商頻道實機驗證。
- 禁止：建立假 `default` 組織作 sentinel，誤選到真實別名的 token。
- 禁止：catch 記 `Exception.Message` 或只有回空卻未交共用例外入口。
- 禁止：移動 token 解析時刪除 `ChurchReportDevelopmentConfiguration` 安全驗證。

## 6. 必跑測試與證據

`SpeechMessage.Tenancy.Tests` 驗證四政策、來源配對、Jesus Warning、null 合併、
錯產品、路徑／標頭注入、容量限制、完整設定分區及快照不可變。
resolver 與 settings 各 A／B 千次並行並排空；持續存活 provider／resolver
完成百次操作後，以弱參照證明不保留結果、tenant、credential 或 locale。
真實 DI 容器驗證重入、跨產品與外部設定 owner；provider 故障驗原例外身份及重試。

Processor 搬前後公開建構結果必須相同。主機 source 架構檢查涵蓋盤點的
十五個消費者及後續新增來源；Development validator 只因用途是安全驗證而豁免。
主機終端故障以真實 Exception.log 驗證原例外去重及敏感本文排除，禁連 LINE。
必須另有完整方案、CI 閉包、獨立 coverage ≥80%、strict、編碼與打包守門。

## 7. 失敗邊界的錯誤與正確寫法

```csharp
// 錯誤：Lazy.Value 先於方法呼叫求值，其建構例外不在共用方法的 catch 內。
return ChurchReportLineChannelAccess.GetOptionalToken(Configuration);

// 正確：僅舊 Lazy 入口使用無捕捉 factory；本次求值、讀取及告警都在已知邊界內。
return ChurchReportLineChannelAccess.GetOptionalTokenFromFactory(static () => Configuration);
```

一般已取得的 DI IConfiguration 可直接借用，不需要再包 Lazy 或建立新 provider。
