# 執行計畫：完成 MemberInfo 小組樹授權來源安全決策

1. [x] 讀取 active goal、AGENTS.md、P7 parent、P7.4 parent、權威 matrix、MemberInfo tree contract、
   cross-user isolation contract 與既有 source。
2. [x] 對照 00031／00032 的 matrix row、`LoadDistrictTree`／`SearchDistrictTree`／`LoadGroupMembers`、
   `GetAccess`、Church／Shepherd branch、`EnsureShepherdListsLoaded` 與 legacy SDK path。
3. [x] 建立 PRD、source audit、design、context manifest 及本文件；範圍僅限 task／CCG 記錄。
4. [x] 透過 CCG self-healing runner 發起一次 bounded architecture analysis；每個 backend 最多等待 45 秒。
   runner health check 已成功，但 45 秒窗口內尚未產生任何 backend 的 usable output；依使用者限制停止等待、
   記錄「雙模型未完成」並採本機 source evidence，不重試等待。
5. [x] 將本機與可用審查 finding 寫入 `check.md` 及 CCG review，完成 JSON／UTF-8／CRLF、
   `git diff --check`、scope 檢查；兩次 45 秒 CCG run 都沒有 usable output，明確記錄「雙模型未完成」；
   確認沒有 runtime、matrix、gate、CE、traffic、P7.5 或 P8 變更。
6. [ ] 更新 P7/P8 parent 的 PRD、design、implement、roadmap 與 task metadata；將 00031／00032 記為
   independent source-only no-go，並選擇下一個不相依 capability family。
7. [ ] 執行 scope-only commit，封存 Trellis／CCG child；不改動其他既有工作樹變更。

## 驗證命令

```powershell
python -m json.tool .trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/task.json > $null
python -m json.tool .ccg/tasks/p74-memberinfo-smallgroup-tree-authorization-audit/task.json > $null
python ./.trellis/scripts/task.py validate .trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit
git diff --check
git diff -- .trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit .ccg/tasks/p74-memberinfo-smallgroup-tree-authorization-audit
```

## 風險與停止點

- 若稽核發現額外的 shared state、credential、cache、SDK bridge 或 write adjacency，只修正 task 記錄；
  不擴大為 runtime implementation。
- 若 CCG provider 未在 45 秒內完成，採本機驗證並記錄降級；不得反覆等待。
- 這個 child 不授權 CE、feature gate、traffic、P7.5 或 P8 行動。
