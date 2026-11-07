請分析目前 P2-01 架構測試接續變更，唯讀，不修改檔案，不呼叫其他 agent，不讀機密設定。
目標仍為完整 P2，CCG 舊 P0-13 metadata 不取代本目標。主會話 inline 實作與審查。

已搬移六個來源至 SpeechMessage.Hosting.Tests/Architecture，移除 Localization.Tests linked Compile，
CI filter 補 Hosting/Hosting.Tests、collector 與獨立 net10 coverage case；CI 閉包 7/7。
未提交的初版 R2/R3/R9 scanner 有重要缺陷，準備依測試修正而非將綠燈當驗收：
- Regex／手寫詞法不能可靠處理 alias、escaped literal、raw string、間接公開型別等 C#。
- Reader Compile 所有來源未完整限制總保留量、組態、普通 assembly/framework reference。
- R9 reference 不能放在來源迴圈內，沒有來源的 Plugin 也須拒絕禁止相依。
- 舊 ChurchReport 全部 R2 薄殼缺口不能整產品放行；要精確違規識別、理由與移除條件，
  已知差距仍維持未驗收，不能把「只剩ChurchReport」當完整 R2 通過。
- 相容 organization literal 白名單必須精確路徑、穩定成員、literal、數量；不得 EndsWith 放寬。

請評估使用既有中央 Microsoft.CodeAnalysis.CSharp 套件的有界語法／語意分析方式，
提出能抓到上述繞過的 focused 正反例、資源生命週期與診斷保密限制，以及合理分階段交付。
請閱讀實際 ArchitectureRules/Reader/RuleTests 和 P2 計畫必要部分，不輸出來源全文、秘密或個資。
NuGet audit 連線暫時失敗，曾作停用audit的離線診斷，不能當正式品質通過；
後續必須恢復啟用與有效證據，或如實保留阻塞。僅回傳 Critical/Warning/Info 與建議。
