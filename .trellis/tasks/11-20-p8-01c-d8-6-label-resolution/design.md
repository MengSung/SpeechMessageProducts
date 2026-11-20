# 設計：P8-01c D8-6 標籤衝突修正

## 邊界

只修改 Deployment 的奉獻 migration 計畫、其測試，以及 P8 階段任務／回報資料。ChurchReport 顯示行為不在本修正範圍。goodshepherdback 是唯一核准實機組織。

## 資料流

SDK adapter 分別讀取 `new_fee.new_category` 與 `new_dedication_booking.new_dedication_category` 的 1028 標籤。各欄先獨立拒絕重複值，再以數值建立聯集：fee 欄存在時以 fee 標籤作唯一類別名稱；只在 booking 欄出現的值沿用 booking 標籤。跨欄標籤差異作為明確的純資料欄位流入 plan JSON 與 Markdown。

plan 指紋必須涵蓋兩欄各自的原始值與標籤，確保 apply 重建 plan 時任何來源 metadata 漂移都會拒絕。類別唯一身份仍是教會＋`legacy-<值>`，不按名稱合併。

## 驗證

先增加精確命名回歸測試並確認紅燈，再實作最小修正。離線測試僅證明純資料規則；組織 migration 與 P8Live 依 08A 實機步驟另行執行並保存安全摘要。
