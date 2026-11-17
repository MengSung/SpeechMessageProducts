# P6-07a 可見性改動前黃金鎖定

2026-11-17 08:03 Asia/Taipei (UTC+08:00)：所有產品.cs可見性仍與67ac88da相同；只新增計畫指定的DynamicProxyGenAssembly2友元。
兩個指定測試先因缺黃金紅燈；Release與Debug各產生並在移除產生開關後比對通過。
本表不是P6-07a驗收，後續不得重生黃金來接受可見性差異。

| 黃金檔 | 路由行／型別筆數 | SHA-256 |
| --- | ---: | --- |
| r2-route-table.debug.txt | 305 | `c47e010a0a17c4ccba134a98b38340068e636f6d789b619a9fc18b7da446021f` |
| r2-route-table.txt | 294 | `b4158e474d04ba3d8e06201f6e65b4a8fabb91e738262ed101f34c8560210307` |
| r2-serialization-shape.debug.json | 868 | `3e876c5e96e24a44757f44f5e4585494652ce23b14869cf511565ad70dce6e43` |
| r2-serialization-shape.json | 845 | `f362f873eedf28649198d495fefee1585ab68b51af54dfd957c1258c5a2a1c9a` |

只反射公開控制器宣告、產品基底公開方法與雙序列化器metadata，不建立產品物件或執行getter；不含秘密、真實資料或可見性欄位。
Debug另存.debug檔，不以Release掩蓋Debug條件型別。後續每批使用正常比對模式，環境變數SPEECHMESSAGE_R2_WRITE_GOLDEN不得啟用。

本機證據artifacts/p6：

- p6-07-golden-red.log
- p6-07-golden-release-generate.log
- p6-07-golden-debug-generate.log
- p6-07-golden-release-compare.log
- p6-07-golden-debug-compare.log
