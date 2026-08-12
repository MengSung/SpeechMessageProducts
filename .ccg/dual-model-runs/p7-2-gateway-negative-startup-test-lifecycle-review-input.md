請審查目前未提交的兩個測試檔變更：
- SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs
- SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs

背景：.NET 10 WebApplicationFactory 對預期 Host startup 失敗存在 lifecycle race；top-level app.Run() 在啟動例外時處置 host，而 DeferredHost 可能隨後讀取已處置 IServiceProvider，導致 ObjectDisposedException 遮蔽原本應有的 InvalidOperationException/OptionsValidationException。變更將純設定驗證的負向測試改成直接建構 ConfigurationGatewayOperationAuthorizer 或呼叫 GatewayRequestBodyLimitOptions.BindAndValidate；保留所有正向 HTTP/TestHost/Kestrel 整合測試。

請檢查：
1. 是否仍覆蓋正式啟動 validator 實際依賴的 fail-closed 契約；
2. 是否有測試 fixture、configuration、資源或跨測試狀態洩漏；
3. 是否不當削弱 integration coverage；
4. C# / 繁中 XML 文件品質；
5. 僅回報可由程式碼驗證的 Critical / Warning / Info。

禁止：執行 CE、讀取 credentials、輸出 endpoint、CRM ID、token 或原始外部例外。
