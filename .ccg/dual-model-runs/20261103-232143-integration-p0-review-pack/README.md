# 階段 0 ClaudeOnly 審查包

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509。八個邏輯批次，依區域拆成 11 份；每份 diff 與 prompt 同名，SHA256 記在 manifest.json。

排除歷次 CCG 輸出、WP 證據、research 原始紀錄、基準、進度頁、封存任務、生成 C# 與二進位。敏感欄位及可能的長 token 已遮罩，不含金鑰／授權內容。第八批加使用者指定的未變更 JS 測試上下文。

只透過 Start-CcgDualModelRun.ps1 -Role reviewer -ClaudeOnly，逐份執行；第一份失敗或逾時即停，不把前幾份成功稱為全階段通過。Critical／High 修完並重跑 §8 才可 P0-13。

- 01a：守門工具與 CI，2838 行，SHA256 09965e58dc8f9096624b2306e75ae017775e2968ea6006b9f4dd4324bc4a8a3a
- 01b：守門工具與 CI，2684 行，SHA256 9d98223da51abc2e2e98d4dea3a4f9aaccd1340a9c187d3d13d66ab1fc97ad36
- 01c：守門工具與 CI，1589 行，SHA256 10bd5bd8f8b445161cc106e7dbb70034eef03155bf8048365c2c3aa0aa81d2f3
- 02：SpeechMessage.Localization，660 行，SHA256 25bbda0716d6693a1ce5888ebf2031049c669a4b77563a72c18ae04de6ce386d
- 03：LineMessagingProcessor.Core，982 行，SHA256 b78bb09d737f1031680c421c2c260e851ce1ca3acc8ef2c85326863e6a3bab56
- 04：SpeechMessage.Dynamics.Plugins，713 行，SHA256 bf25a8bd0d6832c596052434fc286490fd2172c65183d9dea4054071760f037b
- 05a：SpeechMessage.Dynamics.Deployment，1974 行，SHA256 1277646b09a38d8075dc1de02fc766fb5a8677cba0829986939377cbbc78bb20
- 05b：SpeechMessage.Dynamics.Deployment，1877 行，SHA256 9e81cc064ff22aecaecb6ff2e3fe78d6e31d6497a748ed04c0e4355efe9829bf
- 06：Development 防護 P0-SEC／P0-ENV，1046 行，SHA256 7b353856af4a01e9d46a8b2a9e4a71523436c7b1a0b31b6c3a753a1c1674bb77
- 07：套件補修與 P0-12A 圖片防護，2099 行，SHA256 1c3c6fe7e066983e7346aded36fe30209c6341241c53bb6d1bf45f4610ba35c9
- 08：小組回報修正與前端守門，866 行，SHA256 647e0086a791a2ff4f30a357969ecde2890db4f436b1cba7767c195de89b228d

## 停止狀態（2026-11-03）

01a、01b、01c、02 已完成；03 因 Claude 工作階段額度限制兩次皆無輸出而停止，04～08 未呼叫。總計 4／11 份有結果，不代表全階段通過。完整狀態見 STOP-REPORT.md 與 manifest.json；results/ 為原始模型輸出的逐位元組副本，不改寫原紀錄。

使用者恢復額度並指示繼續後，從 03-prompt.md 接續；人工轉交則依 manifest 的待執行順序逐份交付。Razor 候選修正尚未過完整 verify，不在固定至 ae29cb40 的差異內，後續修正需另備差異及 SHA256 複審。小組回報維持 review，P0-13 尚未開始。
