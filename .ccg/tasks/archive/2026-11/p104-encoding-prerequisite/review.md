# 編碼修復直接審查

六份來源在轉換前 git diff HEAD 全無異動，沒有覆蓋他人未提交內容。既有轉換器預覽六份及套用六份皆零失敗，所有 stringLiteralsUnchanged／chineseStringLiteralsUnchanged 為 true；conversion.json 記錄實檔轉換前後 SHA。另以 git show HEAD 逐檔重建 CRLF 候選與實檔完全相等；兩份 JSON 解析後完全相等。Git blob 與工作樹受行尾轉換影響，不能把兩者雜湊相同當成必要條件；最初的該斷言已據此修正，沒有改動產品驗收。

只改行尾與檔尾，不改任務狀態、業務文字或時間戳原始執行證據；未變更編碼工具、基準或排除規則。使用者已免除雙模型，由主會話直接審查。正規化後正式完整 Changed -Base 73b6e6cf 已通過：974 checked、853 raw excluded、0 issues；P1-04 新增檔精確暫存後仍須再跑。本前置任務沒有已知阻斷缺陷。
