# F3 審查與覆蓋紀錄

更新：2026-11-27 13:22 Asia/Taipei (UTC+08:00)。狀態：離線閘門通過；實機待依02A補做，未完成整體目標。

## 外部單模型（使用者指定ClaudeOnly）

- 首輪：`.ccg/dual-model-runs/20261127-124042-3p-f3-lane-a-reviewer/`；有可用輸出、無Critical／High。SHA-256 `17C40D174E054A3E8D538BA816827132B3C6F24DEB68D3F4D95503B51C96CD45`。只涵蓋實際讀取的程式及部分diff，不代表全測試／設定全文。
- Channels補查：`20261127-124611-3p-f3-cover-channels-reviewer/`；七測試檔全文，無Critical／High。SHA-256 `D42849796F6140D1D63166C3AD1D06D17321621979262204E3C4551BEE9464E7`。兩項低風險另記之後再說。
- contracts／deployment／config同時段三組runner各兩次無可用輸出：probe顯示5小時額度；summary仍quotaBlocked=false／failureReason=no-usable-output。不修無故障PATH、不第三次原樣重試、不轉Gemini；這三組未取得外部審查。
- 登錄修正複審：`20261127-130856-3p-f3-registration-only-lane-a-reviewer/`；ok=true、reviewMode=claude-only、dualModelComplete=false；無Critical／High。SHA-256 `F6C31E1B90DBA09F8CA673DAEBEF109CDCE44D138564B7D12A57468F8BADCC4A`。全文讀PoolData／ChannelCreatedPlugin／SlotRequestPlugin，兩測試只讀變更段；未執行測試、未核對Live helper，不冒稱已涵蓋這些。

## 02 §4 本地補查及修復

三個唯讀本地代理分讀規則／Plugin／架構／持久診斷9測試全文、schema／registration／packaging／renderer／task等13檔全文與sln／companion完整diff、部署／身分／Live helper／packaging9測試全文。前兩組無Critical／High；第三組找到1 High。三代理均已關閉；本地代理不能算外部Claude審查。

High：真Plugin把核定僅登錄的音訊科技Channel當產品池。先真入口11案3 RED／8通過，再修復精確type＋status分流，83/83 GREEN；完整方案9,205/9,205、0失敗／0略過，Release建置0錯誤。修復 `8be872b2a7af2d8a751e34e1fb97b4d6cf5af3b2`，已由上列ClaudeOnly複審無Critical／High。RED、GREEN、完整守門SHA見F3報告。

自審按AGENTS核：精確伺服器產品映射；未改caller授權或SDK owner；所有候選本次建立、完整分頁與正式ID唯一驗證；登錄slots不發布為配發候選；故障trace不含憑證／個資、原例外保留、平台回滾；無新task／timer／subscription／connection或全域可變狀態。現有有限快取、租約、取消及排空測試由完整閘門覆蓋；離線不證明SQL實機列鎖。

`.trellis/spec/guides/cross-user-isolation-and-performance-review.md`及對應backend隔離專檔不存在；沒有偽造已遵循缺檔，改依AGENTS實際永久條款核對，將外部未覆蓋及實機限制交終點整體審查。

## 非阻斷觀察

外產品TopCount告警遮蔽、故障注入精確位置、登錄型別／狀態變更導致整池fail closed、既有登錄slot異常案例未逐一注入，均另記之後再說。沒有放寬任何已知停止放行條件。
