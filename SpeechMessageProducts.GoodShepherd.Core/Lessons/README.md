# Lessons

本模組是課程連結、簽到、進階評估及設備狀態規則的預定位置；P2-02 不包含這些業務行為。

## 階段 5 搬移範圍

- P5-07：`DiscipleLessonsPluIn`、`QrCodeWorkflow`、`StorLessonSignningPlugIn`、`StorLessonSigningWorkflow`、`StorLessonSigningLibrary`、`TwoWingsPlugIn`、`YangMeillcStorLessonsPlugIn`、`EquipmentWorkflow`。
- 新外殼為 `LessonLinkPlugin`、`StorLessonSigningPlugin`、`StorLessonSigningActivity`、`LessonProgressionPlugin`、`EquipmentStatusActivity`。

兩套舊進階評估需在黃金測試確認差異後合成單一引擎，差異設定依計畫寫入教會設定，不以組織代碼分支。資料存取、時鐘與通知分別依賴共用核心契約；搬移前不得假設舊副本等價。
