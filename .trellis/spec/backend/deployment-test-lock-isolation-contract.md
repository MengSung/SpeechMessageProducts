# 合成 shadow 測試的組織鎖隔離契約

2026-11-29：兩條線的合成 `ShadowCliTests` 曾因使用真 ProgramData 組織鎖，
與實機部署或另一個測試程序競爭而逾時。錯誤不能豁免，也不能刪除共享鎖。

- 正式 `DeploymentCli.Run`、`RunWithInput`、`RunWithCatalog` 固定使用
  `GetSharedOrganizationLockDirectory()`；呼叫端改 `artifactRoot` 不改變正式共享鎖。
- 合成 shadow／switch 測試明確呼叫 internal `RunSyntheticShadow`。
  接縫只接受正式組件之外的合成 connector，且輸出根必須是 Temp 的真子樹；
  相對路徑、Temp 本身、外部根及現存 link／junction 均拒絕。
- 合成共享鎖放在案例輸出根的固定 `.synthetic-shared-locks`；
  同一範圍的不同 artifacts 根仍依組織名互斥，不以略過共享鎖隔離測試。
- 接縫仍經同一 `ProductionGuard`、parser、原命令流程及例外回報。
  沒有 CLI 參數、環境變數、static override 或全域可變測試狀態。
  它是受信任的友元測試接縫，不是遠端寫入授權。
- 取得及釋放順序保持原契約：先 local、再 shared；SDK 歸還後先 shared、再 local。
  故障、取消與逾時不能留下部分 handle；不可依賴 GC 或程序退出。

驗證使用每案獨占合成 SDK、Temp 根及 writer，保留原有 shadow 的全部故障、取消、
敏感輸出、資料清理及租約斷言。新增案例必須證明跨範圍獨立、同範圍互斥、
逾時前零 Connect、釋放後可重試及立即再取鎖、產品 connector 拒絕、輸出根拒絕、
正式 argv 無法選取合成鎖。這些是離線證據，不能冒充實機部署或推播驗收。
