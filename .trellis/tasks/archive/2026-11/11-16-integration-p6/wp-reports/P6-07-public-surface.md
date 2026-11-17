# P6-07 公開型別前後與四類統計

2026-11-17 09:29 Asia/Taipei (UTC+08:00)：由最終ValidateSource同次Roslyn語意分類輸出，不另維護人工允許清單。

| 組態 | 原清冊口徑（排除Program） | 最終同口徑 | 實際公開型別（含Program） | 框架入口 | 控制器／檢視元件 | 公開簽章必要 | 明列例外 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Debug | 337 | 128 | 129 | 2 | 32 | 95 | 0 |
| Release | 315 | 118 | 119 | 2 | 30 | 87 | 0 |

337是起點Debug舊清冊的不同型別數，原規則已豁免Program，並非全部public宣告數；故同口徑為337→128，Release315→118。
含Program的實際起點為338／316，最終129／119。下降包含P6-03刪除22個不同型別，以及P6-07實際內部化187／175個型別；不能把分類規則改變算成刪除型別。

控制器只將BaseChurchController的InMemoryContext欄位與建構式共2個protected成員改成private protected；所有public控制器成員不動。框架入口為Program／Startup；其餘分類由真正MVC組件身分與遞迴公開簽章閉包決定。

明列例外0筆，沒有需要列理由的例外。Razor探針成功，未加入第五類。Debug多出的公開型別由條件編譯造成，兩組態分開評估。兩組態Validate／ValidateSource都零違規；清冊141→0（Debug75→0、Release66→0），status resolved。

四黃金SHA與P6-07-golden.md一致，未重生；本機分類原始證據r2-final-b-surface-Debug.json、r2-final-b-surface-Release.json及p6-07b-source-final.log。此統計不是頁面驗收或整包guard通過的替代證據。
