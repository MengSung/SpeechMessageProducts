# Implement multi-category donation operation page and payment settlement sync

## Goal

TBD.

## Requirements

- TBD

## Acceptance Criteria

- [ ] TBD

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
# 奉獻操作頁面與付款同步需求

## 目標

在 `SpeechMessageProducts.ChurchReport` 專案內，將奉獻操作頁面改善為可清楚輸入多筆奉獻類別與金額的流程，並沿用既有付款、Callback、CRM 收費單與 LINE/Exception.log 管線。

## 範圍

- 修改 `Views/Dedication/DonationPaymentView.cshtml` 的操作體驗。
- 保留現有 `DedicationController` 提交流程與 `PaymentReturnController` 回呼契約。
- 付款成功後由既有後台流程更新收費單實收金額、付款狀態與付款紀錄。
- 不搬移任何來源教會的帳號、token、Cookie、Session、CRM 或 LINE 設定。

## 驗收

- 單類別與多類別輸入可提交，最多 7 類。
- 每列保留 Category、Amount、Others；重複類別、0 元、負數、超過 7 列由伺服器拒絕。
- 定期定額僅允許單一類別。
- 信用卡、ATM/匯款與既有 PaymentReturn GET/POST 行為不退化。
- 重複 Callback 不重複增加 CRM 實收金額或 LINE 通知。
- 未登入及不同 Session 不可互相讀取奉獻資料。
