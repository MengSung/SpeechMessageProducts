# P7.4 認證憑證策略安全邊界：需求與決策

## 交付範圍

此 CCG child 僅產出認證憑證策略決策。帳密 legacy path 讀取 `new_app_pass` 並比較明碼；
已完成的 contact typed-read boundary 刻意沒有、也不得攜帶 password/hash/token/cookie/raw CRM entity。
因此不得把 typed read 接入帳密登入，也不得為 LINE path synthetic rehydrate `Entity` 或在 typed
path 失敗後 fallback 至 legacy CRM。

## 未來安全方向

若要遷移帳密登入，另建 `auth.contact.credential.verify` capability：受控 executor 內完成
secret comparison，只回傳固定 non-secret outcome。必須先有 credential source replacement/migration
policy、server-owned routing／authorization、A/B/session handoff／lifecycle tests 和獨立 CE evidence
plan。此 child 不修改程式碼、CE、gate、traffic、P7.5 或 P8。

## 外部模型狀態

2026-08-13 architecture run：Gemini 45 秒後僅有 partial output；Claude quota/session blocked。
記錄為「雙模型未完成」，不當成完整 dual-model review；結論由 source tracing 與既有 contract 驗證。
