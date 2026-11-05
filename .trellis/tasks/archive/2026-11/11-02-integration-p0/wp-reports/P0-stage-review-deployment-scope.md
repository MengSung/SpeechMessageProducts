# 階段 0 審查修正：佈署 verify 明列驗證範圍

05b-C1 的假綠燈風險成立：既有 ConfigurationMatches 僅比較公開欄位，User Secrets 同模板輪替不會出現操作；
CLI 卻可能輸出完整一致。原 deployment-contract 及 P0-12 回報已明訂 plan 不解析秘密，也不是輪替工具。
採審查提出的「明列限制」方向並加非零未完成碼，保留原秘密責任分工；沒有新增秘密讀取、秘密 fingerprint 或輪替指令。

- 比較方法改名 NonSecretConfigurationMatches，排除未比較欄位，不再以舊值回填 expected。
- plan CLI／Markdown 含佔位時明列不比較秘密、不支援輪替。
- verify 公開 metadata 有差異為 1；公開相同但含秘密佔位為 3（驗證未完成）；無秘密且完整相同才為 0。
  未完成是預期結果，不發例外告警。這不證明實際秘密相同，也不宣稱輪替已被套用。
- 同步 3.3 的 01、附錄 G 與 deployment-contract，不改設計副本或歷史 WP 回報。

先做不變行為的輸出方法抽取，原 42／42 通過；四個新案例取得 3 失敗／1 通過的功能紅燈，
修正後整個 Deployment 46／46。新案例涵蓋假秘密相同與輪替兩種來源、零讀取／零寫入、未完成碼與固定警示，
以及 trace 一致成功／差異失敗。沒有連 D365／LINE，輸出與 Markdown 不含兩組假秘密值。
原始本機輸出在 ignored artifacts/p0-stage-review-20261104/deployment-scope-*.log。

新發現其餘 Warning／Info 列主回報；秘密設定的 rollback 範圍與完整輪替仍按既有設計，由使用者另外核定處理。
完整 §8 與第 09 份複審另行收尾；目前不能用本報告宣稱外部複審已通過。
