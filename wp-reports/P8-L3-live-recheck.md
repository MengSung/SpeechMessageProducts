# 遺留 L3：移至好牧人 9.1 上線前作業

**結論：依 2026-11-21 核定的 goal-objective.md 修訂 1，L3 本階段不執行 Live 補驗；處置完成並移至好牧人 9.1 上線前作業。** 這不是 Live 通過或組織狀態驗收。

## 需求 → 證據 → 狀態

| 項目 | 證據／理由 | 狀態 |
|---|---|---|
| localization、weeklyreports、lessons、appointments、fees、qrcodes、care 步驟 Live 補驗 | 修訂 1 明確指定 L3 不做。沒有 Live evidence 的項目受部署工具保護，不能切換至正式組織；延後不會使無證據項目取得正式部署資格 | 移至好牧人 9.1 上線前作業 |
| 七個 LIVE-PREP-09 Workflow 活動補驗 | L2 的 XAML 樣板與流程實機依修訂 1 移至上線前作業；七活動目前 dormant。L3 不再進行 live-check，也不推定 L2 工具可行 | 維持 dormant；與樣板工具一併移至上線前作業 |
| registration 清冊 | 本單元沒有對 `plugins.registration.json` 執行修改；`tier`、`evidence` 及 `reason` 保持原值 | 未變更；不代表補驗通過 |
| goodshepherdback 切換／回復 | 本單元沒有執行切換、apply、rollback、live-check 或資料寫入；不以舊證據推定本次單元起訖狀態 | 未執行；不宣稱本次 `off`、`verify=0` 或殘留 0 |

## 上線前處置

好牧人 D365 9.1 上線前，另依當時核准的作業安排 L3：核對起始狀態與殘留、逐模組執行、記錄 evidence 或原因碼，並確保每次實機單元結束回復 `off`、`verify=0`、殘留 0。七個 dormant Workflow 活動須先具備可驗證的樣板工具與完整輸入契約，再由上線前作業決定是否補驗；本報告不授權啟用或修改其分級。

## 決策與收尾

2026-11-21 22:45 Asia/Taipei (UTC+08:00)：依使用者核定的修訂 1，將 L3 實機工作移至上線前作業；理由是部署工具拒絕沒有 Live evidence 的項目切入正式組織，因此延後不會造成正式部署風險。清冊保持原狀，不新增或推測 Live evidence。L3 的本階段處置已記錄；實機驗收仍未完成。
