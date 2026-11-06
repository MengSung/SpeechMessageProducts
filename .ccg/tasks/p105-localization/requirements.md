# P1-05 驗收清單
正式依據：02 P1-05、90 C.1／E.3、00 §5／§8，主 P1 既有 prd/design/implement。P1-01～04 保留既有驗收；不改正式計畫。
- 保留 ISystemTextCatalog/GetText/GetTable 舊完整後備及全部 P0 測試；另外提供未展開的原始語系查詢。
- TextContext 不可變，教會/組織/產品邊界由伺服器授權，變更語系不可改變邊界；不持有身分、連線或 request。
- ITextCatalog 五層：教會 exact、教會 *、system exact、parent/alias、zh-TW，全部缺少明確失敗。
- TextArg 具名參數；缺/多/重複拒絕；按明確格式文化處理日期/數值，不修改全域文化。
- 覆寫為完整不可變快照，拒絕重複正式 ID、重複 key/culture 及 scope 不符，來源呼叫/暫存屬一次操作。
- 純 MemberCultureResolver 使用 enabled/released 交集，偏好→教會預設→zh-TW；en 不默認開放，pseudo 明確 opt-in，Production 啟動驗證拒絕 pseudo。
- KeywordCatalog 按 NFKC／合併空白／Invariant lower，比對會友→教會→繁中，正規化後異意圖衝突 fail closed；資源唯一來源，英文僅既有關鍵字。
- 兩宿主實際載入 net462/net10，五層與錯誤路徑、A/B 1000 並行、scope 變更、失敗重試與弱參照/資源回基線有證據。
- 正式編碼、strict、I18n 生成/verify、架構、覆蓋率及方案回歸，需求對照實作/測試/證據逐項填寫後才驗收。
