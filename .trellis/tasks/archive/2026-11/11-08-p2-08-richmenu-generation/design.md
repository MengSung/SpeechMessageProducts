# P2-08 設計

共用RichMenus新增RichMenuGenerateHandler實作Hosting IOutboundTaskHandler，由工作scope供應已授權TextContext、教會代碼、啟用語系及8個穩定組合/頁籤版面；不自行決定產品功能組合。每次先完整取字（含覆寫）、驗alias/14字chatBar、版面與正式provider ID集合，再查容量，不足在render/create/upload前失敗，由Worker既有告警入口處理。

ImageSharp3.2.0＋Drawing2.1.7（官方NuGet依賴ImageSharp>=3.1.11，與固定3.2.0相容）繪製；Host注入有相應字形的Font，不複製OS字型。無cache或背景task，像素池0，單張序列製作並Dispose Image/PNG；借入processor的HTTP timeout由scope限制，現有無token API必須完整await再清理，不用WaitAsync遺棄I/O。

alias只用ASCII英數底線連字號、qps-ploc縮qps且總長32內。每張完整上傳後才發佈alias；已存在且完整相同fingerprint的alias可重用。遇到不同版本既有alias先拒絕並交產品換綁workflow，不能暗中覆寫而遺留直接綁定者；本包產生新語系及重用完整同版本，只有本次自有新建ID在故障時補償刪除。不假稱已完成既有使用者重綁。正式provider集合重複ID拒絕，合法同名不同ID不去重。
