# P7.1 App-named 名單目錄強型別讀取

實作 `ORG-CALL-00014` 的固定 `list.catalog.retrieve.app.named` 能力。此能力必須是零 caller parameter、
bounded、DTO-only、request-local 的 Data8 / ProductClient read；不修改 ChurchReport consumer、feature gate、
CE、P7.5 或 P8。`ORG-CALL-00065`、歷史 P7.2 Slice C 與共享 EntityCollection cache 均不在範圍。
