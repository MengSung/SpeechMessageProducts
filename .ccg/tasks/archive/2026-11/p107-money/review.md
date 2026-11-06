# P1-07 主會話審查

執行者：實作主會話自審；沒有外部模型或獨立第二人，不能宣稱雙模型完成。

## 需求及正確性
- 已核對02 P1-07、90 C.1及正式Word8.5。以ISO代碼為無歧義幣別標記而非文化預設貨幣；沒有使用C格式或換匯。
- 白名單switch回傳固定常值，不保留輸入；default幣別fail closed。Amount保存完整decimal，Add/+只用一條checked路徑；負值與零合法。
- TWD0位明確為產品政策；其餘六幣2位；AwayFromZero是明確顯示決策，保護雙runtime midpoint一致，原數值不變。
- Equals/HashCode包含貨幣與數值，scale相同值雜湊一致；default比較反身但不能作金融操作。

## 隔離與生命週期
- readonly純值；formatter無欄位、無cache/IO/timer/queue/背景工作。傳入CultureInfo只借讀至同步呼叫結束，不修改/複製到全域，不持有caller參照。
- 千次A/B全部await；百次文化弱參照GC全回基線；注入Culture原例外保真，下一次格式化成功。
- 輸入拒絕為預期不發通知；真正文化故障向owner原樣拋回，沒有新增通知client。
- 無重複資料列或mutable集合發布路徑；不涉及憑證、VM/D365或正式資料。

## 發現與處理
- CA1859在測試編譯真實阻擋：改具體型別，不停用規則。
- 首輪money-first兩目標TRX互覆：保留log；正式重跑用每目標獨立目錄，各35/35，未將互覆當雙宿主證據。
- 計畫中五個指定名稱全部保留；32行為紅/1契約綠後實作轉全綠，再加負零兩案。沒有TODO/暫殼/NotImplementedException留在交付。

## 結論
Critical/High/Warning：無未處理；適用守門與逐包驗收見wp-reports/P1-07-acceptance.md。主P1未完成。
