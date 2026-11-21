# P8-02 執行計畫

## 開始前

- 只依 08A §4.2、§5 P8-02；遵守固定順序，P8-01c 暫維持未驗收並依使用者後續更正回頭補做。先前 verify 失敗根因為 Plugin build number 未指定造成 FileVersion 不符；更正後以 build 20 重打包、通過 `Test-PluginAssembly.ps1` 再唯讀 verify，屬條件改變的新操作，不沿用「不得重試」結論。
- 完成一次 metadata 來源安全性檢查。若缺少明確可驗證的 goodshepherdback-only 設定，跳過連線並記錄，不讀取任何秘密值、不嘗試其他組織。
- 建立並核對 metadata 黃金檔時只保存 contact.customertypecode option value／label，沒有會友列或認證資料。
- 取得登入者教會必須沿用伺服器已驗證 contact 的 `parentcustomerid`；缺少 account scope 時保留原始標籤，不建立無效 TextContext。
- ChurchReport 的服務註冊必須使用 `GoodShepherdTextCatalog`。P8-03a 開啟覆寫時，依設計先移除空 provider/catalog，再註冊覆寫 owner，最後以同一 snapshot provider 建回 GoodShepherd 產品 catalog。

## 實作順序

1. 新增／調整測試，精確建立 08A 指定的六個 Core 測試與三個 ChurchReport 測試，先執行並保存預期失敗結果。
2. 依可取得 metadata 的證據建立 `customertypecode-options.json`；不可取得時不捏造 option value，留下待查資料及 fail-closed 解析行為。
3. 實作 `RoleCode`、`RoleCatalog` 與 11 個字典鍵；使用 I18nCheck 產生資源，不手改 `.g.cs`。
4. 在 ChurchReport 接上空覆寫 provider 的 `ITextCatalog`，更新名單 API `items` 投影並保留 `data`；確認存檔仍只使用原 D365 label。
5. 更新 `_GeneralGroupGrids.cshtml` 的兩個 lookup 與伺服器序列化 fallback，確保指定兩檔不再包含 11 個角色中文字面值。
6. 搜尋並登錄 `P8-PROP-004` 所指其餘中文判斷位置，不在本單元修改它們。
7. 逐項重跑指定測試並確認變綠；執行 Core、ChurchReport、I18nCheck、文件說明與位元組檢查。需要時執行 trellis-check。
8. 寫一頁 P8-02 回報，列出 metadata 對照／未對應角色、修改及未修改測試期望、守門證據和所有未能執行步驟。
9. 單元符合 08A 驗收條件後提交單元變更、在 `C:\smv\guard-p8` 執行完整守門、補齊回報與驗收標記，推送分支並核對遠端 SHA；進度頁只改 DATA。

## 驗收命令

- `dotnet test SpeechMessageProducts.GoodShepherd.Core.Tests -c Release`
- `dotnet test ChurchReport.MemberInfo.Tests -c Release`
- `dotnet run --project .\tools\I18nCheck -- generate --check`
- `dotnet run --project .\tools\I18nCheck -- verify`
- `python tools/RepoQuality/check_file_documentation.py`
- 若進度 DATA 有改，執行 `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs`
- 完整 00 §8.1 守門於 `C:\smv\guard-p8`；編碼結果另外以位元組檢查確認 UTF-8 無 BOM、CRLF 與檔尾 CRLF。

## 回復點

角色新增均為 additive；若驗收發現相容回歸，以本單元變更提交回復 Core catalog、DI 註冊、API 投影及 View lookup，保留既有 `data` 與 D365 標籤存檔行為。metadata 黃金檔不含組織資料以外的個資；若內容驗證不符，刪除本單元生成的黃金檔並將 metadata 對照標記為待查，不改寫遠端歷史。
