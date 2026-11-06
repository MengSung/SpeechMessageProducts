# P1-07 技術決策及執行
1. 搜尋既有Money：Localization無既有實作；不誤用SDK Money（沒有幣別）。沿用既有readonly struct風格。
2. Money保存decimal與經白名單驗證的大寫ISO；default不合法但值相等／雜湊仍遵循.NET等價關係。公開Amount保留原精度；Currency/加法/格式化驗有效。Add與+共用一份邏輯。
3. MoneyFormatter無狀態；固定ISO + 一個ASCII空白 + N0/N2文化數字；先decimal Math.Round AwayFromZero使Framework/.NET10 midpoint一致。選擇此對人類金額常見的捨入，明記而不依runtime預設；不改會計儲存金額。
4. 原生CultureInfo只借用於本次格式化，不保留到欄位；不改CurrentCulture／NumberFormat／全域文化；故障原樣拋回owner共用通知。
5. 先反射契約測試真紅，再完整行為測試；strict雙目標／兩宿主／並行1000與呼叫者Culture弱引用、逐項需求對照。
6. 獨立coverage與方案回歸及00§8。主會話自審，誠實註明無第二人／外模審查；進度一般資訊持續追加。分包本地提交再只封存本子任務。
