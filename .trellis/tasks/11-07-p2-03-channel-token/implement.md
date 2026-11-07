# P2-03 實作計畫

目標：依design.md完成雙目標加密與白名單寫入工具。
技術：.NET net462／net10、xUnit雙宿主、既有Dataverse SDK。
由主代理在既有隔離worktree依executing-plans inline執行；
使用者已授權規劃後直接實作，不另等待開始指令。

## 一、加密核心（可獨立驗證）

檔案：Core/Security兩檔；Tests/Security/ChannelTokenProtectorTests.cs、
Golden/channel-token-vectors.json、Tests.csproj的fixture複製設定。

- [x] 先寫RoundTrip_Net462AndNet10_Compatible、篡改IV／密文／MAC、錯key／版本、
  統一例外、長度／Unicode／base64、隨機IV、key不變與併行隔離案例。
- [x] `dotnet test LineMessagingProcessor.Tests -c Release --filter FullyQualifiedName~ChannelTokenProtectorTests`
  保存缺型別紅燈，最小API骨架後重跑行為紅燈。
- [x] 用AES.Create／CBC／PKCS7、RNG、HMACSHA256完成Protect／Unprotect；
  `if (!FixedTimeEquals(actual, expected)) throw new ChannelTokenProtectionException();`
  必須在任何解密器建立前執行。
- [x] 依design.md有界驗證，finally清零key、明文byte、MAC輸入及中間陣列，
  using釋放所有crypto owner，不新增依賴或跨呼叫狀態。
- [x] 雙宿主完整回歸，取兩端合成封包加入共同fixture，再雙向重跑；
  反射核對實際產品framework，不以net48宿主名猜測。

## 二、Deployment寫入工具（依賴加密核心）

檔案：Channels三檔、DeploymentCli.cs、Program.cs及Deployment.Tests新案例。

- [x] 先寫新指令parser拒絕、stdin上限、缺key、最小欄位／alternate-key Upsert、
  教會lookup ID、共享寫鎖、取消與釋放後report紅燈。
- [x] 新路由消費既有connector／write lock／report；stdin只讀兩個有界秘密行，
  Base64 key解碼後64 bytes，不接受命令列秘密參數。
- [x] Service以`new Entity(\"new_linechannel\", \"new_channelid\", channelId)`建立UpsertRequest，
  填入兩個加密欄位，不Create後自行去重；assign只更新查得的Channel資料庫ID。
- [x] 重跑Deployment原契約、Schema CLI、Program共用診斷與新案例；
  未具必要實機授權／前置則保留外部待驗證。

## 三、品質及交付

- [x] 執行file documentation、UTF-8無BOM／CRLF／末尾CRLF、I18n verify、
  duplicate、每目標Core獨立coverage及相關架構。
- [x] 審查先MAC後解密、固定時間分支、例外保密、輸入配置上限、buffer／owner清理、
  跨user／church／org不可變狀態及部署唯一性；故障修復後重跑。
- [x] 更新同一phase-2-summary及P2-03需求／證據／缺口，時間使用Asia/Taipei。
- [ ] 保存回報及本地提交；只有全項驗收通過才封存，否則保持可接續狀態。

既有P2-01的R2與五份原始證據編碼問題仍追蹤，不降低門檻、不阻擋本獨立核心工作。

## 實際補充 2026-11-07T12:53:53+08:00

- 核心缺型別與23失敗／6通過行為紅燈保留；CLI缺入口、CA1068／CA1861、
  14失敗／9通過行為紅燈，以及cleanup原故障被覆蓋的1案紅燈均保留。
- 所有生產協定測試與原157案已重新執行；另加真stdin pipe owner7案，
  包含暖機後128輪不靠GC的handle回基線。Debug／Release最新各193案。
- 新增stdin owner為必要生命週期修正；沒有第三方套件、正式秘密或實機I/O。
- 完整Changed守門必須執行且如實保留五份原始證據問題，並非標記通過。
- [ ] 核准實機範圍、before／可驗證回復、安全欄位權限、原子唯一性與測試資料清理。
- [ ] 整包正式驗收與封存；不得以本離線檢查點替代。
