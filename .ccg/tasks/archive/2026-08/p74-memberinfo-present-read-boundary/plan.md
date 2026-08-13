# 實作計畫

1. 先為 registry/wire、Data8 bounded query、ProductClient/service defensive-copy 與 controller gate/authorization
   寫 failing contract tests，並記錄每一個 RED。
2. 實作 fixed operation 的 abstraction、Data8 allowlist、independent ProductClient、deployment factory/service
   和 controller typed path；所有 checked-in gates 維持 false。
3. 跑 focused 與完整品質檢查，確認 no fallback/retry、A/B isolation、cancellation、encoding、scope 和
   resource ownership；限時 external review 未完成時標示「雙模型未完成」。
4. 如實更新 matrix/parent、scope-only commit 與 archive；不啟動 P7.5/P8。
