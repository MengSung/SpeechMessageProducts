# Fees

本模組是收費及撥款領域規則的預定位置；P2-02 尚未建立付款、金額轉換或外部查詢行為。

## 階段 5 搬移範圍

- P5-11：盤點舊 `AllotPlugIn`、`AllotWorkflow`、`AllotTlsWorkflow`、`FeePushLineWorkflow`。
- 新外殼為 `FeeAllotPlugin`、`FeeAllotActivity`、`FeeNotifyActivity`；撥款查詢建立 P2-07 的 `AllotQuery` 待辦，費用通知非同步推播且 Retry Key 使用收費單 ID。

金額與幣別依階段 4 的 `Money` 契約；測試須涵蓋冪等、重試及不跨教會使用設定。此目錄不持有金流憑證，也不代表已連接永豐測試環境。
