# P2-12 執行清單

- [x] 以本檔與 `prd.md` 啟動 Trellis 子任務。
- [x] 先紅後綠補 Deployment `member` 設定契約與測試；建立／更新安全 JSON 兩案先失敗，修正後全套 200 案通過。
- [x] 補 `驗收/P2_推播鏈驗收步驟.md`，只含不敏感指令、備份、清理與判定。
- [x] 完成 P2-08 階段審查修正測試，確認 alias 容量及補償刪除行為；既有 58 案通過證據保留。
- [ ] 只在必要 User Secrets 可用後執行 goodshepherdback P2-12 第 1～7 步，留一頁本機操作紀錄。
- [ ] 執行相關測試、文件／編碼檢查；驗收後固定重用 `C:\smv\guard` 跑 00 §8.1。
- [ ] 更新 P2-12 回報、進度 DATA、M-P2-REVIEW 與計畫修正提案；不啟動階段 3。
- [ ] 更新 Trellis 狀態、提交一個 P2-12 收尾提交，守門通過後推送目前 P2 分支。

## 本輪修復（2026-11-09）

- [x] 確認所有實機程序退出，四輪操作各留一頁紀錄，D365／LINE殘留0。
- [x] 秒精度認領3案先紅，修正後雙宿主各23案通過，保留所有版本／scope守門。
- [x] 部分信任Flex先紅，明確converter後bubble／carousel兩案通過；會員套件net10 139案、net48 100案通過。
- [x] 簽章v4（AssemblyVersion仍1.0.0.0）、metadata全過、合併六案通過；尚未部署v4。
- [ ] 本修正版提交後固定guard全套§8.1；不因守門通過而推送未驗收的P2-12。
- [ ] 依§9-7取得修正版實機續跑核定，再執行member（含核定同GUID重播）與雙Worker；先備份、精確AllColumns DPAPI錯誤備份、必做清理。

2026-11-09 07:34 Asia/Taipei (UTC+08:00)：修正版已提交 `e2fd52d3`。guard fetch成功，但checkout連續兩次被舊追蹤 `.turns.json` 阻擋；位元組與舊HEAD完全相同，restore無效，已DPAPI備份驗證。未執行clean/reset或§8.1，先依§9-7請核定精確單檔清理，不強制checkout、不改命令順序。
