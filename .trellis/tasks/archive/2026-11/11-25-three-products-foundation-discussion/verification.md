# 三產品共同底層文件審查記錄

本任務只交付討論文件，不改產品程式、不連線 VM／D365／LINE，不啟動任何新實作包。

## 外部分析與審查實際結果

- 20261125-103720-three-products-foundation-analyzer：Claude 完成；Gemini 403／probe 503。
- 20261125-104026-three-products-foundation-retry-analyzer：Claude 完成；Gemini 503。
- 20261125-105246-three-products-foundation-documents-reviewer：Claude 完成且未發現 Critical；Gemini 未完成。
- 各次 dualModelComplete=false，quotaBlocked=false，degradedFallback=false。沒有完整雙模型分析／審查，也不是配額降級成功。
- 未繞過自我修復入口、未直接呼叫後端；這是文件討論交付，不是產品 release 核准。

## 意見處置

- W1 已補 R6 身分綁定、R7 隔離及告警正文引用。
- W2 已解釋九個 manifest 項目包含既有實體欄位延伸；new_saved_flag 明標為欄位。
- W3 HTML 已明寫 test 名稱限制、host 唯一，以及同主機不同路徑目前不能直接套用。
- W4 不採納「來源一定是正式資料複本」的未查證說法；改為可能含真實個資，盤點前按敏感資料處理。
- W5 補充 second-hop 是否需要轉送身分取決於工具實際認證。
- W6 明確記錄 export-metadata 也先核定唯讀目標。
- W7 由 LINE 官方 Markdown 原文查證1000選單及建立每小時100次；啟用前仍需核對限速範圍。
- W8 審查限制保留在附錄及頁尾，縮短工具細節。
- 不採納 clone=秘密外洩、GUID必變、協會=教會、在VM執行可保證消除second-hop、無條件整台VM還原等說法。

## 文件驗證

- Word 16 節、15 表、16 頁；16 頁已全部轉圖並逐頁檢視，無空白頁、裁切或表格越界。
- canonical render_docx.py 因缺少 bundled soffice.exe 失敗；改用既有 Microsoft Word 的獨立唯讀 COM instance 匯出，再用 bundled pypdfium2 逐頁轉圖。COM 文件及 instance 已關閉。
- DOCX與HTML逐段／逐表格比對內容模型，官方超連結8個，ZIP驗證成功。
- HTML 在1440px桌面與390px手機檢查；產品切換、七步導覽、容量正常／超量／剛好／空值／小數、報告展收、環境情境及內部錨點均通過。無JavaScript錯誤與自動外連。
- 文字產物 UTF-8 無 BOM、CRLF、檔尾CRLF通過。
- 全庫 check_file_documentation.py：checked=1902, failures=16，全為既有封存3.5/P7情境JSON的MissingCompanionExplanation；未修改封存資料或擴張排除清單。
- 該檢查器未納管此路徑HTML（範圍check=0）；另做明確的位元組與檔首說明查核，不宣稱全庫通過。
- 未執行產品建置、產品測試、實機驗收。

## 任務檔案契約

task.json保存此文件任務的識別、完成狀態、分級及下一步；不代表產品實作獲准。plan.md保存範圍及流程，review.md保存審查限制與驗證。本任務完成只指文件產出完成。
