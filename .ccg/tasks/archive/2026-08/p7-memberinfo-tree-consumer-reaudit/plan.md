# 稽核計畫

1. 讀取 Phase-0 matrix row、legacy controller call graph 與既有 source-only audits。
2. 對每個 row 建立分離的 trust boundary 與 lifecycle 判定。
3. 執行一次不超過 45 秒的 CCG 雙模型 architecture analysis；逾時時採本機驗證並記錄降級。
4. 產出 `audit.md` 與 `review.md`，明確指出 00031/00032 能否獨立建立 child，以及 00033 的 no-go 前置條件。
5. 驗證 metadata、task artifacts 與 git diff scope；只提交本 task 所屬檔案。
