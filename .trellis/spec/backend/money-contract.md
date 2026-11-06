# 金額與顯示契約

## 範圍與入口
`SpeechMessage.Localization/Money.cs`、`MoneyFormatter.cs` 為 net462/net10.0 純值地基。
產品只接受 TWD、HKD、SGD、MYR、USD、GBP、AUD 精確大寫代碼；不能用 SDK 的無幣別 Money 直接替代。

## 不可改變的不變量
- `Money(decimal,string)` 保存完整 decimal；零與負值合法，不做換匯或隱含捨入。
- 預設 struct 沒有合法幣別；讀 Currency、Add、+、Format 必須拒絕。相等與雜湊仍保持反身性，default 不是 TWD 0。
- Add 和 + 共用唯一實作，先驗雙側幣別，再 checked decimal 相加；跨幣別或溢位明確拒絕。
- TWD 顯示 0 位是產品政策，**不是** ISO minor units=0；其他六幣顯示 2 位。
- 明確 Math.Round AwayFromZero，使 net462/net10 midpoint 一致；只改顯示副本，儲存金額不變。
- 格式為 ISO代碼 + ASCII空白 + N0/N2數字。文化決定數字分隔與負號，不改幣別；不要改用 `C` 讓 en-US 把 TWD 顯示成美元。
- 文化由 caller 擁有，僅借讀至同步方法回傳；不能同時修改同一文化。格式器無欄位，不保存 CultureInfo、使用者、租戶或金額。
- 無 I/O／cache／queue／timer／task owner，不需要 Dispose。真正的格式來源故障原樣拋回 host notifier 邊界，不自行發 LINE；預期輸入拒絕不通知。

## 必要證據
`MoneyTests` 保留正式五測試名稱，涵蓋七幣、非法/null/default、負值及正負 decimal 邊界、跨幣別、精確加法、相等與雜湊、自訂文化不修改與固定德文輸出。
兩宿主各自新目錄 TRX，Framework 既有反射斷言仍須確認載入 net462。千次 A/B 任務全部 await，百次 caller 文化弱參照 GC 回基線，注入原例外後下一次呼叫成功。
嚴格雙目標、獨立每目標 >=80% coverage、00§8及Plugin守門，不可拿 net10 報告替代 Framework。

## 錯誤與正確方式
錯誤：`amount.ToString("C", culture)`、以文化預設貨幣補缺、建構時先.Round、跨幣別直接相加。
正確：有效Money保留原始數值；只在MoneyFormatter顯示副本上套固定政策；ISO身份永遠來自Money.Currency。
