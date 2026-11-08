# P2-00 Debug 中繼檔建置診斷（原始根因待確認）

2026-11-08 08:15 Asia/Taipei (UTC+08:00)。已在原工作樹使用 Visual Studio 隨附 MSBuild 執行還原、建置及重建，正常重新產生中繼檔，沒有補空白檔案或修改產品。GoodShepherd.Core.Tests 的 net48 與 net10.0 各 41 個測試全部通過；不是乾淨複本或 P2 整包驗收。IDE 原兩筆錯誤及其 08:11 重建開始已觀察到，但視窗隨後最小化，IDE 最終結果尚未核對，不宣稱根因已修復。

| 項目 | 實際結果／證據 | 狀態 |
|---|---|---|
| 原兩條路徑 | 完整長度 269、266 個字元；調查時檔案及父目錄都存在 | 已確認 |
| 長路徑假說 | LongPathsEnabled=1；VS MSBuild 與 dotnet MSBuild 均可直接讀取 FileListAbsolute | 未證實根因 |
| 原路徑 Debug 建置／重建 | VS 2026 amd64 MSBuild 序列執行，兩個命令 exit 0；vs-msbuild-before.log、vs-msbuild-rebuild.log | CLI 通過 |
| 雙宿主回歸 | net48、net10.0 各 41 個測試全過，0 失敗／略過；trx 及 debug-tests.log | 82 個離線測試通過 |
| IDE／程序競爭 | 原錯誤清單仍在；觀察到重建啟動，後續視窗最小化。沒有可證明競爭的失敗重現／刪除紀錄，未停止使用者程序 | 待驗證 |
| 原況與後續 | 不改 csproj、方案、Directory.Build.props、registry 或服務。P2 驗證優先短路徑乾淨複本；同輸出不得並行 Clean/Rebuild。IDE 若再失敗，保存該次完整建置輸出，不盲目縮短路徑／清空 obj | 已記錄 |

本機證據：`.trellis/tasks/11-07-p2-00-plan34-transition/evidence/debug-path-20261108/summary.json`，SHA-256 `6DFC8C257FE504E24F14B080657DB81A943135FE1C48D8112C6BEE644DBBA8BD`。摘要含逐份 log／TRX SHA-256；原始證據不提交。此次未部署、未連 D365／LINE／金流、未建立背景作業，P2 驗收狀態不變。

補記：本輪進度頁兩套共 27 個測試、1,295 份檔案說明及此次四份文件 UTF-8 無 BOM／CRLF 位元組檢查均通過。唯讀診斷目錄 `C:\Users\Administrator\AppData\Local\Temp\p2-pathprobe-23d84a9689544ef5ae33c4c0e02fdaf9` 的正常 PowerShell 清理遭平台政策拒絕；未繞過，保留 read.proj，沒有程序或秘密。診斷尚未完成 IDE 驗證，因此不提交／不歸檔。
