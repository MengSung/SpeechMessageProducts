# P7 下一個 capability 選擇需求

## 目標

依 authoritative post-runtime-health 70-row matrix 與目前 source，選取下一個可安全推進的 P7 child，
或精確記錄沒有直接 candidate 的原因；不得把歷史 Slice C、local data-plane 或舊 checkpoint 當作完成證據。

## 結論

沒有 operation 通過 DTO-only、server-authorized、無 Session/InMemoryContext、credential-bearing
ListManager、stored query、無界 response 與 write adjacency 的直接 consumer gate。

`ORG-CALL-00031`、`00032`、`00033` 都在 `MemberInfoController` legacy graph 內；最新 immutable
target scope 只是 fail-closed seam，尚無 complete server-owned assignment source。已建立
`08-14-p7-memberinfo-server-authorization-source` 作為唯一 recovery prerequisite。

## 非範圍

不開啟 CE、feature gate、流量、P7.5 或 P8；不重播 Slice C。
