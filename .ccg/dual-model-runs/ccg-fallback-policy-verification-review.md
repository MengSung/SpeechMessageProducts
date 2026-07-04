# CCG Fallback Policy Verification

請以 reviewer 角色回覆一個很短的 review，用 Critical / Warning / Info 三段輸出即可。

本次目標不是審查程式碼，而是驗證 CCG self-healing runner 在其中一個 backend quota/session blocked 時，是否能讓另一個成功 backend 的輸出保留下來並讓任務繼續。
