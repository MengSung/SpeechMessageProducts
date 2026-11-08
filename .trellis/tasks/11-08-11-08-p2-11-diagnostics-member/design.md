# P2-11 設計

## 資料流與信任邊界
SDK IPluginExecutionContext 的 task 正式 ID → 以 UserId 借用 OrganizationService → 最小 task regardingobjectid Contact → 既有 LineRecipientQueries → MemberNotificationService → 既有 FlexTemplateEngine → 唯一 LinePushClient。
來源教會由部署管理的 sourceChurchId 註冊設定提供；組織由 context.OrganizationId 提供，不能由 Contact 偏好語言授權。secure JSON 僅 tokenKey，與 mode=push 的 raw test token 嚴格區分。

## 相容與資源
既有 trace/push 建構及執行契約不變。member 每次解碼自有64-byte key，finally清零；SDK由D365擁有，adapter使用using只清參照。所有執行資料留在Execute局部；SDK同步準備不進自建執行緒，Core HTTP所有await不捕捉沙箱內容，十秒有界並同步等待排空。只有固定結果、HTTP狀態、GUID RequestId、範本key與resolved culture入追蹤，不記內文/身分/密文。

## 範本與相依
GoodShepherd通知增加診斷Flex入口，沿用已內嵌Diagnostics.json；原文字入口ABI不變，共用同一準備/查詢/解密/sender/傳輸流程。
Plugin需要GoodShepherd的net462閉包。先以實際PE相依與既有打包測試確認必要閉包；不得只加ProjectReference就假稱可部署、不得bin萬用字元合併、不得放寬metadata檢查。原四輸入實測失敗；完整七輸入一次探測也被metadata拒絕（Microsoft.CSharp），且ToolUtility有ValueTuple閉包。不得使用premerge假裝仍只有四輸入；已提出PROP-004等待核定明列七輸入與包裝專用來源子集，不自行放寬安全規則。

## 回復
未連實機；改動可由下一提交回復。P2-12才遞增FileVersion、備份部署與收尾。
