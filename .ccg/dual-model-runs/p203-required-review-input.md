請以 reviewer 角色只讀審查 P2-03 已提交實作，勿修改任何檔案、勿啟動子代理、勿執行部署或網路測試。
本輪高優先執行設定要求 Gemini 與 Claude 雙模型審查；只審下列來源：
LineMessagingProcessor.Core/Security/ChannelTokenProtector.cs 與同目錄相關型別；SpeechMessage.Dynamics.Deployment/Channels；SpeechMessage.Dynamics.Deployment/DeploymentCli.cs、Program.cs；相應已提交離線測試與 .trellis/spec/backend/channel-token-contract.md。
檢查認證後解密、固定時間比較、秘密清除、有限輸入、白名單、組織隔離、穩定資料庫身分、SDK lease/lock/drain、真實Exception及敏感輸出。
不得讀取 User Secrets、任何 credentials、artifacts、evidence、原始 log、trace、環境變數內容或 repo 外的檔案。不審本輪未完成的 P2-07 stubs，不以已封存3.3的實機條件否定3.4的P2-03離線完成定義。
輸出 Critical/High/Medium/Low 分級、檔案行號、可重現原因；沒有問題寫無。不得將離線結果冒稱實機。
