# P1-07 金額及幣別格式化驗收

2026-11-06，Asia/Taipei。依據02 P1-07、90 C.1及正式Word8.5。P1-05已驗收；本包不依賴P1-06/08/09。

| 正式需求 | 實作位置 | 實際測試與證據 | 狀態 |
| --- | --- | --- | --- |
| readonly Money與七幣白名單 | Localization/Money.cs | 七幣、未知/null/空白/大小寫/default拒絕 | 通過 |
| TWD顯示0位、其他2位 | MoneyFormatter.cs | Twd_NoDecimals、Usd_TwoDecimals、七幣與負中點/負零/正負decimal邊界 | 通過 |
| 文化不改貨幣R8 | IMoneyFormatter、MoneyFormatter | Format_EnUsCulture_KeepsTwdCurrency、固定德文與自訂separator/negative sign，不改caller | 通過 |
| 同幣別精度及跨幣拒絕 | Money.Add與+ | Add_DifferentCurrencies_Throws、decimal精確加法、雙向溢位 | 通過 |
| 隔離及釋放 | 無狀態formatter/純值 | 千次A/B全部await、百次文化弱參照、原故障/下次重試 | 通過 |
| 兼容及品質 | 正式腳本/方案 | 下列結果與evidence/p107-final/ | 通過 |

## 執行證據
原始根：artifacts/p1-07-20261106/；可追蹤摘要：.ccg/tasks/integration-p1/evidence/p107-final/。
缺型別1案真紅；行為暫殼32紅/1綠後實作，正式兩宿主Money各35/35。net48實際引用net462的原反射守門保留。
完整方案17TRX **4197/4197**、0失敗/略過；Localization **159/185**、ChurchReport629、ToolUtility1214/1345。
產品strict重建雙目標 **0警告/0錯誤**；方案首次仍23既有測試警告，最後增量0/0，不宣稱修完既有警告。
獨立Localization coverage **98.17%/98.52%**，各恰一份且>=80%；收集時157/183案，之後僅加負零兩向量、產品無異動，正式方案再驗159/185全綠。
I18n119/119、verify/generatecheck；Debug7/7且正式TRX守門；PE27/27、Plugin metadata10/build6、CI6；套件36/167零違規、duplicates零新增。
UTF8無BOM/CRLF與進度頁結果見summary；主會話自審review.md，無外部模型或獨立第二人。

## 決策與交付界線
ISO代碼用作無歧義幣別標記，格式文化只控制數字；TWD0位是產品政策，不宣稱ISO minor unit=0。明確AwayFromZero只影響顯示副本，原decimal不改。
沒有搬業務／更改付款規則／正式計畫，無Word修正報告需求。未push／部署／連VM或D365／發LINE；不是支付provider或D365收費單實機整合證據。
本地工作commit收尾後由此紀錄與進度一般資訊同步補入。只封存p107-money，主P1增8/12、Goal保持active；接續P1-09。P1-07已知阻斷缺陷：**無**。
