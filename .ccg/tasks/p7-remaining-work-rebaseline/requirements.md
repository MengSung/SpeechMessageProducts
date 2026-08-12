# P7 尚餘能力重新基準化需求

本 CCG task 與 Trellis child `08-12-p7-remaining-work-rebaseline` 是同一個 L+／high-risk 規劃及靜態分析交付。它必須從 P7.0 的 70 個原始 Dynamics call site 建立新的 authoritative gap matrix，逐列分開記錄 registry、Data8 executor、typed ProductClient、ChurchReport consumer、CE 8.2／9.1、Embedded／Dedicated、rollout／rollback、temporary legacy、P7.3 resource 與 P7.5 blocker。

P7.1 的六個 Data8 read 與 P7.2 local-only／historical CE no-go 是唯讀前置證據。不得把 local-only contract、disabled gate、declaration 或 unit test 誤寫成 consumer 或 CE 成功；不得執行任何 CE mutation、切流或雲端部署。
