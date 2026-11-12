# P4-01 設計

## 邊界

本包只新增永豐行為盤點、合成黃金檔及測試專案 companion 說明，不改付款產品程式。舊方案只讀；其正式金鑰、商店代號及個資不複製。

## 測試資料與流程

`SinopacLegacyCharacterizationTests` 使用固定的假金鑰、固定 nonce、合成請求／回應及黃金檔。HashCode、簽章與 AES 對照值由獨立小腳本依核定演算法產生，避免把舊產品組件當作測試 oracle。網路與真實商店設定均不參與測試。

`payment-inventory.md` 對照主方案、ChurchReport 與 04A 指定的舊方案來源；只記錄能力位置、行為差異，以及 ATM、信用卡各自回寫欄位。`allot-legacy-cases.json` 以合成輸入表達舊撥款比對與回寫結果。

## 失敗邊界

Golden 不一致、測試名稱缺漏或 companion 不完整即不驗收。故意不同於舊程式的空白收費日區間及空白訂單編號行為，記錄為差異，不在本包修改產品實作。
