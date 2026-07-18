# 合併驗證紀錄

## 範圍

- 目標分支：`1.0.0.1.MemberInfo`
- 來源分支：`1.0.0.1.WorkTreeMemberInfo`
- 合併提交：`6e15bc55900388c6eb965a98ee9728621e31e928`
- 外部審查：依使用者指示，Gemini 與 Claude 額度不足，本次不呼叫外部模型。

## 驗證結果

- 受影響的 14 個測試類別：109 通過、0 失敗。
- 排除 `ChurchReport.MemberInfo.Tests.Payments` 命名空間：207 通過、0 失敗。
- 完整 MemberInfo 測試：304 通過、22 失敗、0 略過；22 個失敗均與來源分支既有付款命名、抽取或舊路徑契約相同，沒有新增失敗。
- `SpeechMessageProducts.ChurchReport` Debug 隔離建置：0 警告、0 錯誤。
- `ChurchReport.MemberInfo.Tests` Debug 隔離建置：0 警告、0 錯誤。
- portable kit 來源與解壓 ZIP：73 個檔案、73 個 strict UTF-8、73 個 SHA-256、290 個相對 Markdown 連結全部通過。
- 新建 detached worktree 後再次執行 portable verifier：通過，且 verifier 未修改工作區。
- ZIP：單一 `member-info-portable-kit` 頂層目錄，74 個檔案項目，SHA-256 `6D95CC322FF107FF330BAD23601F4A392C31A269D89F4786F28D3361D08A0BEC`。

## 合併後修正

首次在目標工作區執行 portable verifier 時，`00-START-HERE.md` 的 manifest byte length 預期 11430、實際 11571。根因是來源 manifest 在 LF 檔案上產生，但專案 `.gitattributes` 會在 Windows checkout 強制 CRLF。已依既有建置手冊重新產生 manifest、重建 ZIP，並以新 worktree 證明全新 checkout 可通過驗證。

## 結論

來源提交已完整包含於目標分支；未發現由本次合併引入的程式碼測試或建置回歸。推送與遠端提交一致性確認完成後即可歸檔。
