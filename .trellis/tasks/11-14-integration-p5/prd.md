# 階段 5 Plugin Workflow 重建

## Goal

依實作計畫 3.5 與 05A 執行 P5-00、P4 審查修補、P5-01～P5-13、階段審查；階段審查完成後停下，不建立階段 6。

## Requirements

- 固定順序：P5-00 → P4 審查修補 → P5-01 → 更新回復前置 → P5-02 → P5-03a →
  P5-03b → P5-04 → P5-05 → P5-06 → P5-07 → P5-08 → P5-09 → P5-10 → P5-11 →
  P5-12 → P5-13 → 階段審查。
- 本階段起點為 `9395d2ca`，分支為 `1.0.0.11.Integration.P5.Worktree`；不切換到其他
  工作樹、分支或 main。
- 依 05A §2 的 D5-1～D5-18 執行；舊 Plugin 步驟／舊流程只能經由 `switch --to legacy` 或 `shadow` 指令，在第一輪期間啟用，且必須先通過 D5-18；結束（含失敗、取消）一律回到停用。新步驟不得以 Live 啟用；舊、新步驟不得同時啟用。
- 舊程式只讀；秘密、正式組織、正式 LINE channel、真實付款與計畫正文均不得接觸或修改。
- 每包先測試紅燈、再實作、綠燈、文件與編碼檢查、固定 guard、附錄 F 一頁回報；
  驗收後推送本階段分支並核對遠端 SHA。

## Acceptance Criteria

- [ ] P5-00、更新回復前置、P5-01～P5-13 依序完成，進度頁 13 包均為 `done`。
- [x] P4 審查 H-1 已修正；Medium／Low 僅登錄提案或「之後再說」。
- [ ] 兩個 DLL、註冊清單第 2 版、影子比對、golden／`legacy-live-only`／reading／deferred-to-P7 基準、
  測試與回報均有可追溯證據。
- [ ] `Test-PluginAssembly.ps1`、I18nCheck、BannedApi、Find-DuplicateCode、
  固定 guard §8.1 與 goodshepherdback `verify=0` 均有結果。
- [ ] 遠端 `origin/1.0.0.11.Integration.P5.Worktree` SHA 等於本機 HEAD。
- [ ] 完成階段審查並回報後停止；不建立階段 6 任務或分支。

## Notes

- 依 05A §5 建立每包子任務與一頁 `wp-reports`；原始 TRX、log、審查全文只留本機 evidence。
