# P2 技術設計

## 邊界

階段 2 的雙目標核心只依賴可在 `net462` 執行的純資料、規則與傳輸抽象；
`net10.0` 的 Hosting、D365 連線、ImageSharp、背景服務與 Web API 依賴留在
上層。Plugin 不保存 request、Session、HttpContext、token 或教會可變狀態。

## 依賴圖

```text
P1-05/06/09/10 ──> P2-01 Hosting ──┐
P1 核心／資料契約 ──> P2-02 Core ──┼──> P2-04 推播鏈 ──> P2-11 骨架驗證 ──> P2-12 實機驗收
P1 Deployment/schema ──> P2-03 Token ─┘
P2-04 ──> P2-05 範本 ──> P2-06 副本整合
P2-07 待辦 writer/worker ──> P2-08 RichMenu、P2-09 QR、P4-03 後續金流
P1-08 快照介面 ──> P2-10 改字輪詢
```

P2-03 的加密純核心、P2-06 的來源盤點／特性測試、P2-09 的 QR 內容往返可
在不等待實機推播的情況下並行；正式整合仍依上述依賴順序。

## 主要契約

- 所有跨邊界的結果先在 operation-local candidate 中完成驗證，再以不可變值發布。
- Channel 是可被多間教會共用的資源；授權邊界是「來源教會、會友所屬教會、
  伺服器查得的 Channel 指派」三者一致，不以 caller 提供的名稱或索引授權。
- Token 封包使用 `v1:base64(iv):base64(ciphertext):base64(mac)`，
  AES-256-CBC + HMAC-SHA256，先驗 MAC 再解密；錯誤訊息固定且不含秘密。
- 待辦使用資料庫替代索引鍵做冪等，使用樂觀並行認領；重試、停止、清理與資料
  保留均有上限，任何取消／逾時後不重用不確定狀態的連線或 client。
- 例外通知只在最終失敗邊界進入共用 owner；雙開固定先 flush `Exception.log`
  再排入 LINE，LINE-only 不建立檔案，通知失敗不得遞迴。

## 相容與回復

- 優先沿用 P1 已驗證的 `SpeechMessage.Localization`、`SpeechMessage.Tenancy`
  與 `ToolUtility.Core` 契約；不另造第二套語系、租戶或例外 owner。
- 既有 ChurchReport 以 adapter／DI registration 接入 Hosting；保留現有行為
  直到 P6 移除相容薄殼。
- 每個 D365 寫入前保存可回復的 export／plan，失敗時由現有 Deployment
  rollback 契約處理；本階段不向正式組織寫入。

## 驗證策略

- 純核心：xUnit 雙目標、固定測試向量、假的 HTTP handler／OrganizationService。
- Host：四種通知開關、故障注入、A／B 隔離、停止 drain、資源計數。
- Deployment：L1/L2 離線測試；L3 由使用者在 goodshepherdback 執行。
- 所有實機結果在報告中標示為實機；離線、mock、沙盒與文件準備不可互相替代。
