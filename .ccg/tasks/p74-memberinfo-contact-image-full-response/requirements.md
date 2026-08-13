# P7.4 MemberInfo 完整聯絡人頭像回應邊界

完成 `ORG-CALL-00028` 的 local-only、disabled-by-default、server-authorized、DTO-only 完整圖片回應。
三個 closed branches 為 image、精確 allowlist 的 LINE HTTPS redirect、default avatar。不得修改舊 route、
不得使用 ToolUtility/CRM SDK fallback、cache、retry 或 caller-owned routing；所有 CE、traffic、P7.5/P8
操作均不在範圍內。

驗收依 Trellis child `08-14-08-14-p74-memberinfo-contact-image-full-response` 的 PRD/design/implement/check；
必須保留 checked-in gates=false，並完成 local tests、Release build、encoding、scope、isolation/lifecycle checks。
