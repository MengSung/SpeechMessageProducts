# 階段 5 Plugin Workflow 重建

## Goal

依實作計畫 3.5 與 05A 執行 P5-00、P4 審查修補、P5-01～P5-13、階段審查；階段審查完成後停下，不建立階段 6。

## Requirements

- 固定順序：P5-00 → P4 審查修補 → P5-01 → 更新回復前置 → P5-02 → P5-03a →
  P5-03b → P5-04 → P5-05 → P5-06 → P5-07 → P5-08 → P5-09 → P5-10 → P5-11 →
  P5-12 → P5-13 → 階段審查。
- 本階段起點為 `9395d2ca`，分支為 `1.0.0.11.Integration.P5.Worktree`；不切換到其他
  工作樹、分支或 main。
- 依 05A 修訂 2 的 D5-1～D5-20：退役舊程式只用離線黃金或 reading，不在任何 D365 組織執行；舊輪命令保留不用。新步驟只用 Shadow；追蹤管線採 D5-19 的 live=deferred 退路時，仍完成停用部署與 verify=0。工作包內不做外部審查，階段結束才做一次。
- 舊程式只讀；秘密、正式組織、正式 LINE channel、真實付款與計畫正文均不得接觸或修改。
- 每包先測試紅燈、再實作、綠燈、文件與編碼檢查、固定 guard、附錄 F 一頁回報；
  驗收後推送本階段分支並核對遠端 SHA。

## Acceptance Criteria

- [x] P5-00、更新回復前置、P5-01～P5-13 依序完成，進度頁 13 包均為 `done`。
- [x] P4 審查 H-1 已修正；Medium／Low 僅登錄提案或「之後再說」。
- [x] 兩個 DLL、註冊清單第 2 版、影子比對、legacy-golden／reading／deferred-to-P7／new 與 live=deferred 基準、
  測試與回報均有可追溯證據。
- [x] `Test-PluginAssembly.ps1`、I18nCheck、BannedApi、Find-DuplicateCode、
  固定 guard §8.1 與 goodshepherdback `verify=0` 均有結果。
- [x] 遠端 `origin/1.0.0.11.Integration.P5.Worktree` SHA 等於本機 HEAD。
- [x] 完成階段審查並回報後停止；不建立階段 6 任務或分支。

## Notes

- 依 05A §5 建立每包子任務與一頁 `wp-reports`；原始 TRX、log、審查全文只留本機 evidence。
