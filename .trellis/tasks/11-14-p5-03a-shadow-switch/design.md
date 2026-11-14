# P5-03a 技術設計

## 邊界
以 `SpeechMessage.Dynamics.Deployment` 的 CLI／核心服務為唯一修改邊界；測試使用 operation-local 合成 SDK 與固定時鐘。CLI 負責參數、組織白名單、User Secrets 解析及 command lease；核心負責 manifest 驗證、狀態規劃、步驟互斥與 shadow orchestration。

## 資料流
`manifest v2 / scenarios.json` → 嚴格 schema 驗證 → `export` 快照 → `switch` 或 `shadow` 產生 plan → apply／verify → 讀取 bounded trace／observe → parser／comparer → 清理 seed 與還原步驟、trace、lease。

## 不變條件
- 舊輪與新輪永不同時啟用；新輪只能 Shadow，不能 Live。
- 任何錯誤或取消都以 finally 走 `off`、trace 還原、資料清理及 lease Dispose；原始例外不被清理例外覆蓋。
- secrets 只在同一程序由 User Secrets 讀取，不能進命令列、檔案、輸出、trace 或比較報告；報告只寫鍵存在／缺失與安全摘要。
- 比對以 operation／entity／stable ID／fields／push projection 的明確 schema 進行，overflow 一律不可比對。

## 風險與退路
- 若實機舊入口沒有可追蹤的欄位，依 05A 改以型別＋主要實體 ID＋時間窗；仍不能猜測步驟 ID。
- 若 User Secrets 缺少 `ShadowTestChurchId` 或 `ShadowTestContactId`，先從既有 evidence 或唯讀權威查詢確認後設定；查不到才向使用者詢問，不能按名稱猜。
- 若某模組 D5-18 失敗，停止該模組，不以離線結果替代實機兩輪，並保留安全拒絕證據。

## 受控 legacy 公開命令接線（2026-11-15）

`switch --to legacy` 保留 05A 的三個既有選項，不新增秘密或 seed ID 參數。
由固定模組 Golden 目錄選取 liveShadow 案，僅允許 legacy-live／legacy-live-only
基準，沿用 shadow 的整批準備、即時註冊、權威 Shadow 身分與固定 token 盤點。
runner 共用同一 RunCore／RunRound，在 legacyOnly 分支只執行第一輪；每個出口
仍停用、清理、核對及還原 trace。報告明列 scope=legacy-round-only，只判定
舊輪對期望的欄位比較，不假造新輪、推播送達或整包驗收。

CLI 測試先紅後綠，驗 token／無 token 舊輪、不同期望、失敗及取消收尾、
D34 和不安全盤點前置拒絕、非實機基準零寫入、既有兩輪命令回歸。

## 平台映像情境接線（2026-11-15）

情境的 trigger.images 只描述要驗證的前後映像，不注入 SDK context。CLI 將已核對的
新步驟清單及本次 export-legacy 的映像以正式 step ID 接到 factory；舊映像使用
EntityAlias 而非顯示名稱，ImageType=2 拆成同別名前後兩個宣告。

factory 對實際要跑的每個步驟核對別名、前後種類及欄位涵蓋；前像值必須與 target
seed 相符，後像值依 trigger 覆蓋 seed；Create 前像、Delete 後像、不符的值／型別、
缺舊註冊或重複別名都拒絕。值的比較在測試名稱前綴及暫時 lookup 抽離前執行，
兩輪仍由 runtime 建立不同真實 ID，沒有將規劃映像或規劃 GUID 送入 SDK。

僅接受能由明列可寫 seed 欄位重現的映像值；唯讀／系統欄值、平台預設值或其他
前置 Plugin 改寫後的值，未有獨立證據前仍拒絕。此核對是情境可重現性的前置檢查，
不能宣稱量測了實機 context；正式映像仍由平台產生，實機驗收另列。

## 外部既有記錄摘要接線（2026-11-15）

固定受審入口盤點另明列 externalWrites（entity、churchAttribute）；空陣列表示
來源審查未宣告外部寫入，缺欄位不是零筆。CLI 以正式 metadata 核對教會 lookup
指向 account 及 versionnumber 型別，再將正式主鍵接到命令區域 observer。

observer 在 seed 前及舊輪完成後，以權威測試教會 ID 精確篩選，僅取正式主鍵、
教會 lookup 與 versionnumber；每表最多64筆、最多16表。分頁、超量、身分重複、
缺版本及錯教會皆失敗；只對觸發前已存在的記錄計算版本變更或離開範圍的筆數，
不讀訊息、名稱或其他業務值，不還原或清除既有列。新增測試列仍由原owned／related
清冊清理，不能把此摘要當成新增列清冊。並行外部寫入可能影響版本，因此報告說明
是受審範圍在測試期間的變動，不冒稱具有Dataverse全域寫入攔截或唯一因果證據。

快照只存活一輪，owner Dispose清除ID／版本；報告只輸出實體及筆數，無盤點範圍
明標none-declared，尚未接線明標unavailable，兩者不能以預設空集合假造零變動。
