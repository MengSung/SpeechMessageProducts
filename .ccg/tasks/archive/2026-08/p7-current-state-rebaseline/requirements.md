# 需求

依目前工作樹重建 P7 的 70-row 權威差距矩陣、校正 Gateway parent 文件，並選擇下一個安全且可獨立驗收的 capability child。不得重做封存 P3–P7.3、重播 Slice C、執行 CE mutation、開 gate、切流、啟動 P7.5 removal 或 P8 deployment。

完成必須證明 matrix 的有限狀態正確區分 implementation、consumer、CE、host 與 rollout evidence；P7.5／P8 必須繼續 fail closed。
