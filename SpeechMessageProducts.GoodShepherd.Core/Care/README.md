# Care

本模組是新朋友關懷、週年祝福及相關流程規則的預定位置；P2-02 不含背景排程或通知實作。

## 階段 5 搬移範圍

- P5-10：盤點舊 `NewPersonCareWorkflow`、`HappyBirthdayWorkflow`、`RecursiveWorkflow`。
- 新外殼為 `NewPersonCareActivity` 與 `ContactAnniversaryActivity`；D365 遞迴流程依核定設計維持原呼叫方式。

日期中的「今天」須依教會時區判定；祝福通知 Retry Key 須由「類型＋會友 ID＋當地日期」產生固定 GUID。這些是後續驗收契約，不是目前骨架已具備的保證；時間依賴 `IChurchClock`，通知依賴共用服務。
