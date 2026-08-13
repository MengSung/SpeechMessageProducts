# P7.4 MemberInfo basic-info consumer boundary

以 source-only evidence 判定 `ORG-CALL-00030` 是否可安全從四欄 legacy `UpdateContactInfo` action 遷移至現有二欄 typed contact basic-info capability。不得修改 runtime、feature gate、CE、fixture、traffic、P7.5 或 P8；若不能完整取代 action，必須 fail closed 並記錄精確恢復條件。
