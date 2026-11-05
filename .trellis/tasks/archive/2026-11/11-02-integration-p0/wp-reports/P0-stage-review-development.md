# 階段 0 審查修正：Development 別名與 HTTPS 邊界

第 06 份兩項 Critical 經合成設定重現。基底 token 全空時，原守門只從非空 token 別名挑選禁用預設，
因此正式別名可通過；端點只驗主機，HTTP 或錯誤 CRM 路徑也可通過。

先加 11 案：三個空 token 正式別名與八個同主機不安全 URI，全部取得功能紅燈（非編譯失敗）。
正式別名檢查改用基底全部別名；token 比較保留原非空篩選。
CRM／啟用 Embedded 強制 HTTPS、精確主機、預設埠及沒有 userinfo／query／fragment；
CRM 另強制完整服務路徑。有效 URI 從合併設定取得，User Secrets 同樣不能繞過。
原安全 fixture 的 CRM URL 補成契約規定的完整路徑，沒有刪除原斷言。

原 DevelopmentSafetyTests 與 DevelopmentOrganizationSettingsTests 加新案例共 43／43 綠燈。
沒有改任何實際 appsettings、帳密、provider 順序或 Production 行為；沒有 D365／LINE 連線。
本機原始紅／綠輸出在 ignored artifacts/p0-stage-review-20261104/development-red.log、development-green.log。
完整 §8 於所有修正完成後統一執行；本修正另列第 09 份複審。

第 06 份的獨立讀取點／主機根目錄與環境來源一致性 Warning 記錄保留，未在本次擴大改寫 11 個既有讀取點。
