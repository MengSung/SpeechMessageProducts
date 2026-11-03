# 階段 0 審查修正：授權檔忽略規則

依使用者 2026-11-04 核定補上 `sixlabors.lic`，套用根目錄與任意子目錄。
沒有建立、讀取或複製任何真實授權檔。

先擴充既有 SDK／忽略測試：紅燈 1／2，明確列出根與子目錄兩條路徑未被忽略；
補規則後綠燈 2／2。原四條忽略斷言保留。只執行 git check-ignore 的假路徑探測。
原始本機輸出保存在 ignored artifacts/p0-stage-review-20261104/ignore-red.log、ignore-green.log。

GitHub Six Labors 授權準備與清理依使用者稽核已成功；過去建置與測試仍因 Restore 失敗未執行。
