# P7.4 認證憑證策略安全邊界：審查結果

## Critical

無新增程式碼或執行期行為。唯一 Critical guardrail 已被明確保留：不得讓 contact typed-read
DTO 攜帶或驗證 password/hash，也不得透過 DTO-to-Entity rehydration 或 typed 後 fallback 繞過
安全邊界。

## Warning

既有 legacy login 的明碼 CRM password comparison 與 entity/session chain 仍存在；它是未來
專屬 credential-verification migration 的 blocker，不是本 child 可以安全修補的 local cutover。

## Info

Gemini partial output 建議的 LINE typed-read rehydration 已拒絕，因為它需要 CRM `Entity`／
legacy session behavior。Claude quota/session blocked；雙模型未完成，未重試等待。
