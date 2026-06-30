# 需求摘要

使用者希望確認下列檔案與金流 provider 的關係，並研究是否能將付款後共通流程抽出：

- `ChurchReport\Services\Payment*.cs` 是否與高鉅金流相關。
- `ChurchReport\Tools\Donation*.cs` 是否與永豐金流相關。
- 台新金流 TSPG 的相似功能目前放置在哪裡。
- 收費單寫入 CRM、發送 LINE 通知給奉獻者，是否能抽離成共通模組，讓永豐、高鉅、台新金流使用共通流程，再由各 provider 或產品層實作差異。

本階段只進行 brainstorming 與設計分析，不進行程式修改。
