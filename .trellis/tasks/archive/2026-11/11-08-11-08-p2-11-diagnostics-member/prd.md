# P2-11 推播整條鏈骨架驗證

唯一需求來源：`文件/規劃設計書/實作計畫-3.4/03_階段2-4_共用送訊_Webhook_金流.md` 的 P2-11；固定完成定義依 00 §7.1。

## 先寫的測試
- Plugins.Tests 假 IOrganizationService ＋真 Core/語言/解密/範本：正常、教會不符、未綁定、偽語系。
- 觸發為 task，「關於」必須 Contact；Retry Key 為 task ID。

## 完成定義
1. 上列測試存在且通過；既有 trace/push 模式與雙目標通知測試保留。
2. 提交後短路徑乾淨複本完成 00 §8.1。
3. 原通知鏈沒有新專案；PROP-004核定兩個net462包裝來源子集，對實際投影來源核對覆蓋率至少80%。
4. 本包不需實機，實機只在 P2-12。
5. 一頁回報、進度頁依真實狀態更新。

## 邊界
不改 AssemblyVersion、不改公開 enum、不放寬 smoke、不在本包連線 D365。

## 2026-11-08 PROP-004 核定
明列七輸入；Line.Messaging與LineRecipients以包裝專用子集專案Compile Include／Link連結原始碼，AssemblyName／AssemblyVersion保持原樣，同一步加入方案。原來源的分析器設定由匯入原專案繼承，不新增警告抑制或套件，不複製程式碼／建立trimmer。時間盒起點見task.json，兩小時或子集metadata不通過即停；保留全部沙箱限制。
