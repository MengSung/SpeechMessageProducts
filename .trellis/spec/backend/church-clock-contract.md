# 教會時鐘與時區生成契約

## 1. 適用範圍與觸發

P1-06 的 Localization 同源 net462／net10.0。後續 host、Plugin 與排程必須明確區分
UTC 瞬間、教會牆上時間、生日純日期；不把語系、伺服器 local 或 caller 路由當成授權教會。
業務搬移不屬本包，legacy ToolUtility.TimeOperations.IClock 的 LocalNow 相容介面不可偷偷改義。

## 2. 具體介面與命令

```csharp
IClock.UtcNow // DateTimeOffset
new ChurchClock(IClock clock)
IChurchClock.UtcNow // offset=0
DateTime ToChurchTime(DateTime utc, string ianaTimeZoneId)
DateTime TodayInChurch(string ianaTimeZoneId)
DateTime ToUtc(LocalDateTimeOfChurch local)
new LocalDateTimeOfChurch(DateTime value, string ianaTimeZoneId)
new ChurchDate(int year, int month, int day)
ChurchDate.FromDateOnly(DateTime date)
ChurchDate.ToDateTime()
// 僅 net10
new TimeProviderClock(TimeProvider timeProvider)
GlobalizationModeGuard.ValidateIcuMode()
```

```powershell
dotnet run --project tools/I18nCheck -c Release -- generate-timezones --cldr tools/I18nCheck/data/windowsZones.xml
dotnet run --project tools/I18nCheck -c Release -- generate-timezones --cldr tools/I18nCheck/data/windowsZones.xml --check
dotnet run --project tools/I18nCheck -c Release -- verify
```

## 3. 輸入、輸出與資源契約

- host 先授權教會並取得其時區，再呼叫純時鐘；值物件建構不是授權。
- UTC 只收 Utc Kind；local 只收 Unspecified；轉換輸出 local=Unspecified、UTC=Utc。
  Today 只讀一次 UTC，不讀伺服器 Today。offset 為來源純值，不跨呼叫快取。
- net10 SystemClock 只經 TimeProvider.System；可測來源由 TimeProviderClock 借用。
  Framework 無 TimeProvider 才直接 UTC。來源不可含 request／教會／身分狀態。
- DST invalid／ambiguous local 拒絕；UTC→local 在秋季重複時段仍有唯一瞬間。
  使用 ticks 先驗範圍，禁止 BCL ConvertTime 的 Min／Max 夾值掩蓋溢位。
- IANA 區分大小寫且限固定 CLDR 表；未知、Windows ID、前後空白不自動修正，不回伺服器時區。
  Framework 查 Windows ID，net10 直接查 IANA；OS 無規則原樣失敗。
- ChurchDate 只含年月日；Unspecified 午夜才能由 DateTime 轉入，不將生日視為 UTC。
  後續 net10 DateOnly 欄位／格式化亦須保持純日期，不能走時區轉換。
- 沒有動態 cache、timer、Task、queue、connection 或 subscription；唯一保留參照為借入 clock，
  最長至 ChurchClock 釋放，不處置外部 owner、不保存任何轉換輸入或回傳結果。
- CLDR 49 原 XML、完整授權、上游／正規化 SHA 在 `tools/I18nCheck/data/`。
  XML／stream 由單次命令 using；16 MiB、4096 來源列、2048 唯一 ID；同值 territory 重述可接受，
  相同 IANA 的異值衝突整份拒絕。固定 Ordinal 排序，不含 timestamp／原始列序。
- DTD 只 Ignore，不解析／不連外，實體引用拒絕；輸出驗證完才原子替換，check 永不寫入。
  正式 Localization 存在時，缺 CLDR 必須拒絕 verify，不能以刪檔跳過。
- ChurchReport.Main 在 ExceptionDiagnostics owner 建好後、RunApplication／Startup／CRM／HTTP 前
  呼叫 ICU guard。失敗傳原 Exception 至既有共用入口並 throw，finally 解除 owner、drain／Dispose；
  不新增 LINE client 或變更通知開關。階段 2 Hosting 必須沿用此守門。

## 4. 驗證與錯誤矩陣

| 條件 | 結果 |
| --- | --- |
| 非 Utc 輸入／非 Unspecified local／未知 ID | ArgumentException |
| DST invalid／ambiguous local | ArgumentException，不猜標準時間 |
| 日期範圍溢位 | ArgumentOutOfRangeException，不截斷 |
| OS 缺時區規則／來源真正故障 | 原例外向 host 傳播，下一次可重試 |
| NLS／Invariant 主機 | ICU guard 拒絕啟動，不改 runtime 設定 |
| CLDR 衝突／缺欄／超預算／非法版本／XXE | CLI 2，舊產物不變 |
| check 與現有生成檔不同 | CLI 1，不寫檔 |
| 合法生成／逐位元組一致 | CLI 0 |

## 5. Good／Base／Bad

- Good：同一 clock 並行轉台北／洛杉磯，不保存上一個教會；春秋 DST 使用明確 UTC 向量。
- Base：生日 2000-02-29 永遠原年月日；今天由當次教會時區計算。
- Bad：把 UTC 的 Kind 改成 Unspecified 後猜 local、NLS 下自動回伺服器、模糊 local 選標準時間、
  只建 guard helper 卻未接真實 Main、用刪 CLDR 檔讓 verify 少跑一項。

## 6. 必要測試與決定性斷言

`ChurchClockTests`：Taipei、London 2027 春秋、LA 2027 春、八個正式 zone 固定答案、DST 七個拒絕、
Kind／ID／日期溢位、生日、來源原例外與重試、千次 A/B、百次 local 弱參照回收。
兩宿主逐一比對原 XML 的 445 個 ID，保留原產品 TargetFrameworkAttribute 反射守門。

`TimeZoneGeneratorTests`：真 CLI、排序／同值 territory 重述、異值衝突、DTD／XXE、來源／ID 預算、
版本注入、缺 CLDR 的真 verify、stale check 不寫。共用命令既有案例不得刪除。

`ChurchClockStartupTests`：owner→guard→RunApplication→catch 原例外的來源組合契約；
另以獨立 dotnet 程序執行 ICU／NLS／Invariant guard，並以真實 ChurchReport NLS 啟動拒絕補證。
子程序使用合成 content root、Production、兩通知輸出關閉，不能讀真實 appsettings／User Secrets、
連 CRM 或發 LINE；逾時 kill／wait 自己的程序，不遺留 host。

## 7. 錯誤與正確方式

錯誤：`DateTime.Now.Date` 當教會今日；`ConvertTimeToUtc(local)` 讓 BCL 猜 local 時區；
接收 Windows ID 或失敗後回預設時區。

正確：

```csharp
var today = clock.TodayInChurch(serverAuthorizedSettings.TimeZoneId);
var utc = clock.ToUtc(new LocalDateTimeOfChurch(explicitLocal, serverAuthorizedSettings.TimeZoneId));
// 生日不走上面兩個瞬間 API。
var birthday = ChurchDate.FromDateOnly(dateOnlyFromAuthoritativeField);
```
