# 編碼測試樣本

Test-TextEncoding.Tests.ps1 在 .runtime/ 下為每個案例建立獨立 Git 目錄，
於執行時產生 BOM、LF、CP950、替代字元及無法無損轉換等故障輸入。
每個案例完成後刪除自己的目錄；不提交任何不合法編碼樣本。
