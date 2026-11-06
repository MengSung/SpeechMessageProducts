# P1-07 需求及驗收
依據02 P1-07、90 C.1、主P1 Task7。P1-05已完成；與P1-06／09無先後依賴。
- readonly Money(decimal amount,string currency)，七種大寫ISO白名單TWD/HKD/SGD/MYR/USD/GBP/AUD；未知／空白／default不得進入金額運算或顯示。
- 儲存decimal完整精度；TWD顯示0位是產品政策，其餘2位；不宣稱ISO TWD小數單位是0。
- IMoneyFormatter.Format(Money,CultureInfo)只以明確文化決定數字分隔，固定ISO代碼防止美元符號歧義；不換匯。
- 加法同幣別且溢位拒絕；負值／零合法，不因顯示四捨五入改原金額。
- 正式五個測試名稱保留，加default、邊界、相等、負值midpoint與兩產品一致向量。
- 不修改舊業務使用方式、不新增IO／cache／timer／logger或背景工作；預期輸入拒絕回呼叫端，不自行發LINE。
- 繁中XML、UTF8無BOM/CRLF；strict雙目標、獨立coverage>=80%、00§8與Plugin守門。
