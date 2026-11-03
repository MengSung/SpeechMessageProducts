# P0-12A 已核定執行計畫

來源為使用者 2026-11-03 核定插入；範圍是既有圖片上傳行為，採 inline 實作。

1. 計畫 01 加入 P0-12A，進度頁 DATA 標示實作中；P0-12 已提交 a6a20905。
2. 升級前，合成 JPEG/PNG/GIF/WebP 及 EXIF Orientation 圖片。直接執行既有 normalization sink；新人建立附圖及獨立上傳共用私有 sink，另外保護其呼叫關係。斷言解碼像素、尺寸、JPEG 輸出與大小範圍。鎖住三個既有縮圖方法，無 QR/其他 ImageSharp 呼叫。
3. 新增六個核定安全方法及邊界/生命週期案例，先功能紅燈。測試不存真實照片、不接任何網路。
4. 升 ImageSharp 3.1.12→3.2.0；用只註冊 JPEG/PNG/GIF/WebP 的 Configuration 先 Identify，再驗單邊 <=8192 及總像素 <=40000000，才 Load。每次使用獨立組態，不修改全域預設。
5. ChurchReport 內部共用處理器保留 MemberInfo 正方形白邊與其他上傳 JPEG90/AutoOrient 規則；四個路徑必經此方法。預期壞圖回原 JSON/附圖略過方式，不通知、不退回未解碼原始檔；非預期例外經既有共用通知邊界。
6. 全方案 §8、既有 ChurchReport 測試、弱點掃描與合成圖行為全部通過；更新 spec、回報、任務、進度頁 DATA，獨立提交後才做七批 ClaudeOnly 階段審查。

檔案：Directory.Packages.props；三支既有 controller；新增 Services/ContactAvatar/UploadedImageProcessor.cs；新增 ChurchReport.MemberInfo.Tests/Images/ 測試；相關 spec、計畫、回報與證據。既有檔案大小上限及授權檢查不變。不得增加套件或警告抑制。
