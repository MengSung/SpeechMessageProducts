# P2-10 技術設計

Hosting以固定已授權組織／產品、ITextChangeSource及P1快取組合；每30秒查>成功查詢開始時間-2分鐘，首輪以啟動時刻為基準。先完整驗證有界候選的正式ID、UTC及scope，再Invalidate指定教會；故障保留watermark及全部快照，真Exception交共用入口。單一迴圈不重疊，呼叫取消後drain再Dispose。ToolUtility的net10 adapter借用既有manager短命lease，最小ColumnSet完整分頁、錯誤淘汰、不保存SDK或個資。

所有測試離線、固定UTC與合成ID，不連goodshepherdback；實機集中P2-12。外部審查只階段末一次，主會話inline實作與本地檢查。
