# CI 過渡：DevExtreme 來源待定

使用者 2026-11-04 核定。沒有嘗試 DevExpress 來源、帳號或金鑰，沒有升級 DevExtreme 或提交套件檔。

## 範圍與行為

26 專案逐一解析 ProjectReference，沒有未解析變數；直接依賴 DevExtreme 的只有 ChurchReport。
傳遞依賴閉包正好三個：SpeechMessageProducts.ChurchReport、ChurchReport.MemberInfo.Tests、ToolUtility.Dataverse.Tests。
SpeechMessageProducts.CI.slnf 保留其餘 23 專案；Restore、Build、Test 改用此檔。
Debug tracer 暫停但保留原 run 區塊與 TRX 守門；來源核定並恢復完整 CI 後解除條件。
六個合成 tracer 案例仍啟用。其餘模組覆蓋率、前端 JS 及 repo 守門不縮減。
always 摘要從篩選檔計算排除專案，明列過渡原因及恢復條件，不能代表完整方案已驗證。

移除 ChurchReport NuGet.config 失效的 19.1 本機來源，繼承使用者來源。
新電腦設定方式見 solution-ci-contract.md：安裝已授權的 DevExpress 23.1，
由使用者在自身 NuGet 設定加入該機器的本機套件來源，實際路徑不進 repo。

## 驗證

- 修正前：repo 設定＋空白套件快取還原，exit 1／NU1301，符合使用者的 GitHub 稽核。
- 五項範圍／命令／摘要測試：0／5 紅燈→PS 5.1 與 7 各 5／5 綠燈。
- 新篩選方案：repo 外空快取、僅使用 nuget.org，還原 exit 0，無 NU 錯誤碼。
- 原 tracer 合成測試仍 6／6。ChurchReport 既有快取還原與 Release 建置 exit 0（0 錯誤、1 個既有 CS1572）；篩選方案 Release 建置／測試 exit 0。
- 本機完整 00 §8 維持全方案，收尾統一執行；不能用冷還原或假 dotnet 測試取代。

## 風險與待決

使用者查證 GitHub 前 12 次都停 Restore，Six Labors 準備與清理步驟已成功。
此修改尚未推送，過渡 CI 的建置／測試結果待使用者推送後確認；階段 0 結束前須完整 ChurchReport CI 全綠，
或使用者另行明確接受過渡狀態。目前尚無該核定。

冷還原暫存目錄刪除被自動核准審查拒絕（blocked by policy，未提供細因）；
只含公開 NuGet 套件、無授權或金鑰。目錄名稱 speechmessage-filter-restore-0940c118e5d84a60bbbf40e1f229fe69，
位於使用者暫存目錄，保留待使用者清理；沒有改用其他方法刪除。
