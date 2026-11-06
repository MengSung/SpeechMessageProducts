# P1-06 正式需求與依賴

依據：正式 02 的 P1-06、90 的 C.1、主 Trellis prd/design/implement。
P1-05 已以 2bbf8850／051b8d36 驗收；P1-06、07、09 彼此沒有需求依賴，不等待 P1-08。
環境明定 inline，主會話實作及檢查；獨立讀取／診斷可並行，但不冒稱多代理實作。

## 必要驗收

1. 雙目標 IClock.UtcNow（DateTimeOffset）、IChurchClock 四入口；net10 TimeProvider 轉接。
2. UTC 只接受 Utc Kind，教會 local 只接受 Unspecified；未知 IANA 拒絕，不落伺服器時區。
3. local DST invalid／ambiguous 拒絕，不猜 offset；UTC→local 回 Unspecified。
4. Taipei +8；London 2027-03-28／10-31、LA 2027-03-14 轉換；八個指定時區兩產品一致。
5. 生日純日期不視為 UTC 瞬間，不因時區移日。
6. Framework 使用內建 IANA→Windows 表；CLDR 原始 XML、版本、授權及 deterministic CLI 生成／check。
7. net10 ICU 啟動守門接入現有 ChurchReport，後續 Hosting 沿用；實際 ICU／NLS／Invariant 子程序及真實主機拒絕驗證。
8. 同一 clock 跨時區並行、失敗後正常重試、借用 clock 不 Dispose；無動態租戶快取、timer 或背景工作。
9. net48 宿主反射 net462、strict、各產品 coverage≥80%、完整方案及適用 00§8 守門。
10. 繁中 XML、UTF-8 無 BOM／CRLF／final CRLF，正式進度頁「一般資訊」歷程、驗收矩陣、本地提交與子任務封存。

未改正式計畫需求，無 Word 修正報告需求。P1-08 僅研究尚未寫程式，因此改先 P1-06 不丟失實作。
