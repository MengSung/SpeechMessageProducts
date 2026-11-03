# P0-12A B 段：Six Labors 一般金鑰 CI

## 結果
A 段圖片防護已提交 eb7ff61f。本提交將使用者已備妥的一般金鑰接入 CI；授權 secret 缺失或空白即清楚失敗，僅在 runner 暫存檔保存授權並以 SixLaborsLicenseFile 傳遞路徑。獨立 always 步驟在成功或失敗後清除，只有覆蓋率 XML 上傳。CI 檔已建立，未在 GitHub 實跑（依規定不推送）。

## 紅燈 → 綠燈
- 直接擷取並執行 solution-ci.yml 的兩個真實 run 區塊，以合成授權文字測試，不建立另外一份模擬實作。
- 實作前 22 個斷言有 15 個功能紅燈：缺少必要授權步驟、缺失不失敗、沒有發布路徑及清理防護。沒有把初次換行尚未正規化造成的解析錯誤當成功能紅燈。
- 實作後再加故障輸出保密斷言，Windows PowerShell 5.1 與 PowerShell 7 各 23／23。
- 涵蓋完整多行內容、UTF-8 無 BOM、僅發布路徑、不輸出任一合成敏感片段、清除準備程序 secret、缺失與空白拒絕、發布失敗回收、下游故障後清理、重複清理，以及拒絕刪除 runner 外或非本 workflow 的檔案。
- 摘要與程式 SHA256 見 wp-evidence/P0-12A/license-ci-summary.json；原始紅綠輸出在 ignored artifacts/p0-12a-license/。

## 生命週期與保密
- secret 僅透過準備步驟 env 取得，沒有插值進腳本文字。以 CreateNew 建立專屬 GUID 檔，writer 與 stream 都有 finally Dispose；發布失敗先清掉已建立檔案，再給固定錯誤。
- GITHUB_ENV 只含 runner 路徑，沒有授權內容。本機授權路徑與內容都未寫入 repo、回報或證據；測試從不讀真實授權檔，只使用合成文字。
- 清理驗證直接父目錄為 RUNNER_TEMP，且檔名符合本 workflow 的專屬格式；沒有遞迴刪除或任意路徑清理。測試 finally 還原所有程序環境，不修改使用者層級設定。
- 本次 repository secret SIXLABORS_LICENSE 是使用者明確核定的一般金鑰；與將來 Plugin 正式金鑰要求 GitHub Environment 的規則分開，後者不變。

## 驗證
- 完整 §8 再執行：Release 建置 0 錯誤／1 個既有 CS1572；Release 1089／1089、ChurchReport 579／579、Debug tracer 7／7。
- 重複掃描 26 專案／879 檔，groups／new／stale=0。I18nCheck verify 通過，實查兩個生成檔；編碼與 git diff --check 通過。
- 修改的 CI 區塊已在本機逐步執行，使用合成授權，不讀／複製正式授權。真實一般金鑰的 Release 授權檢查已在 A 段及本段本機建置通過。

## 風險與待決
- GitHub 執行結果尚未驗證；使用者回報 secret 已建立，不以此宣稱 hosted workflow 通過。
- 接續 73b6e6cf..HEAD 八批 ClaudeOnly 階段審查。入口失敗或逾時先備好審查包再停；Critical／High 修完並重跑 §8 才能進 P0-13。
- goodshepherdback 清理未回報；沒有任何 D365／LINE 連線。沒有推送、合併、改寫歷史或搬移其他分支檔案。
