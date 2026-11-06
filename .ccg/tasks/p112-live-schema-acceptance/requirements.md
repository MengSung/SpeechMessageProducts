# P1-12 必要需求與證據清單

來源是正式02 P1-11/12、90附錄A/G、既有主P1 PRD及使用者最新自主授權；歷史D24人工分工不代表新的權限限制。只處理唯一測試組織，不可把同VM其他組織當可改。

| 需求 | 實作／來源 | 決定性證據 | 目前 |
| --- | --- | --- | --- |
| R01 合法VM/組織ID/SQL映射 | Hyper-V標準提升、PowerShell Direct/既有connector | readiness.json與兩VM只讀證據 | 通過 |
| R02 備份與真正獨立復原 | COPY_ONLY/CHECKSUM、VERIFYONLY、暫存DB/DBCC/清理 | recovery-executed.json；原DB未還原 | 通過 |
| R03 真plan逐項只新增審核 | 正式SchemaPlanner/Engine | 88項plan與原hash、不重放失效plan | 通過 |
| R04 36欄/6表/4key及附錄政策 | schema、SDKmetadata、官方solution export | 兩Lookup audit受限補正exit0，export獨立XML | 通過 |
| R05 已發布verify與四Active | Engine.Verify | live-verify-first-exit.json=0 | 通過 |
| R06 冪等第二輪零寫入 | 新plan0operations/0pending後apply | second-plan-reviewed/zero-apply=0，before/after待hash核對 | 通過；完整before/after相等 |
| R07 真唯一鍵阻擋第二筆 | 正式smoke/SchemaAcceptanceProbe | 第一筆成功，第二筆D3659.1回0x80040237；實際SQL唯一索引已只讀確認 | 通過；最終smoke=0並有差分實驗/實體唯一索引證據 |
| R08 自身合成資料精確清理 | ownership/Create候選ID/marker/finally | 首輪3候選只讀0；診斷輪Delete/Read0 | 通過；5輪16候選ID另行唯讀重查0 |
| R09 例外保真/取消/不確定Create/回收 | Probe/CLI/Engine tests | 157/157及原錯碼/取消/逾時/清理故障/同名異ID/根包含反例 | 通過，待最終變更回歸 |
| R10 全方案/coverage/strict/回歸/編碼 | 既有品質入口 | 4412/4412、coverage89.11%、strict0/0、Debug7及其他gates | 通過，最後功能修改後已重驗 |
| R11 審查與計畫同步 | 主會話inline、CCG自癒入口、Word | 外部200653雙後端皆無輸出，使用者免等外審；不得冒稱通過 | 通過主會話自審，Word3頁已逐頁QA，無外審成功聲明 |
| R12 本地commit/歷程/封存 | 主代理精確stage | 一般資訊保留全部歷史；不push/正式部署 | 待所有必要項目通過 |

後台元件已由真實官方SDK匯出未受管理solution並獨立XML核對，不是Mock。實體Esc後停止所有UI操作，沒有後台畫面或人工點選證據；依自主授權把元件確認與資料庫約束改為可重現服務端驗收，會明寫計畫及Word，不把SDK結果宣稱UI測試。
原始證據保留在忽略的artifacts，不公開秘密/個資/備份內容；可追蹤摘要另存主P1 evidence/p112-final。全部必要項目通過才可標完成。
