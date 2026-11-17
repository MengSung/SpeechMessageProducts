# 前置B：切換分級與重複效果盤點

依據07A §5前置B、§4.2/4.3、D7-13；時間盒半天，不連D365。父任務integration-p7，起點f06d7596。

## 指定測試（同名）
- Manifest_LiveBlockMissingOrInvalid_Rejected
- Manifest_DormantWithoutReason_Rejected
- Manifest_EvidenceResultOtherThanPass_Rejected
- Manifest_DiagnosticsHasNoLiveBlock

## 完成條件
- 四測試同名存在並通過；非diagnostics每步驟/活動具live區塊；活動不得tokenKey，dormant必有提案理由。
- 每個預設live項目有重複效果盤點與來源檔行號；查到重複或無法確定即dormant，不能反向升級。
- wp-reports/P7-switch-tier-inventory.md完成；對應Trellis spec同步。
- Deployment.Release測試及固定guard完整00 §8.1通過，一頁回報、進度更新後驗收，首次推送P7並ls-remote核對。
