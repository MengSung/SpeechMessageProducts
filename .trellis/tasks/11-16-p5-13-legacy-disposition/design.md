# P5-13 設計

以legacy-disposition.json作靜態去向清冊；project對應核定第一層目錄，projectFile保留Line.NotifyBot異名。ported條目列實際targets、來源型別與精確registrationLegacy複合身分（assembly/type），不得以專案名猜部署組件名。無舊註冊的來源仍可列ported，但必須有明確缺註冊說明，不偽造stepId。library僅代表共用功能拆入，不代表整個副本原樣移植；未承接副作用具體說明。

測試以獨立36名稱集合保護唯一性，雙向核對所有非空legacy到targets映射。暫緩兩項在頂層獨立清單，不混入核定36。source架構測試以git ls-files取得本工作樹受版控程式，不掃父目錄；子程序30秒取消、失敗kill並等待退出，雙管線排空、using Dispose，不建立Host或連線。
