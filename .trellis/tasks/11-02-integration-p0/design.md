# 沿用既定階段 0 設計

本文件登錄既有設計，不變更已定案的 A1～A4、D15～D21、I1～I10。

## 邊界與契約
P0-02～P0-07 建立 SDK、CPM、分析器、編碼、重複程式與國際化守門工具。
P0-08 Localization 與 P0-09 LineMessagingProcessor.Core 只建立原計畫列出的雙目標骨架。
P0-10 Plugin 為 net48，僅併入指定共用組件與 Newtonsoft.Json，不併入 Xrm SDK。
P0-11 統整 Windows CI；P0-12 建立 net10 佈署專案及離線測試；使用者 2026-11-03 核定插入 P0-12A 圖片安全修補；之後才做 ClaudeOnly 階段審查，再由 P0-13 提供 goodshepherdback 人工驗收文件。

## 相容性與生命週期
維持 ChurchReport 既有行為；不得藉抑制新警告或降低守門標準通過建置。
推播憑證僅放單一請求標頭，所有請求與取消來源有界且確定釋放。
字典以不可變快照發布，不承載使用者、教會、HttpContext 或憑證。
具體契約與完整測試清單以階段 0 文件與附錄 C／E 為準。

## 驗收與回復
以逐包提交作為回復邊界。任何不可逆／跨教會操作交由使用者決策。
簽章金鑰僅讀 SPEECHMESSAGE_PLUGIN_KEYFILE，不能複製或產生到 repo。
完成本機驗證不代表 D365 Sandbox 已驗收；使用者回報前不得宣稱階段完成。

## 3.3 開發環境安全子集
P0-SEC 在原 11＋1 部署設定讀取點追加 Development 來源與安全啟動守門；Production provider／逐鍵值不變，部署設定不承載 request 狀態。P0-ENV 後續覆寫 CRM 與停用尚未驗證的 Embedded Web API。新 D22～D27 與附錄 G 以實作計畫-3.3 為準；不再建立舊三個 D365 工具目錄。
