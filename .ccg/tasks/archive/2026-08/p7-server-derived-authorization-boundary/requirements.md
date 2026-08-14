# 需求

依目前 70-row P7 matrix 與 source no-go audits，建立 server-derived、immutable、request-local authorization
boundary，作為未來 ChurchReport ProductClient consumer migration 的共同 prerequisite。所有 browser/route/Session/
InMemoryContext/saved-credential/CRM object 都不得成為 Gateway authority。此工作只建立 local prerequisite，
不執行 CE、consumer cutover、feature/traffic、P7.5 removal 或 P8。
