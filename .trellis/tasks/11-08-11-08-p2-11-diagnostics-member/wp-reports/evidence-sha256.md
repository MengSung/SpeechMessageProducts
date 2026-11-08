# P2-11 本機原始證據索引

此索引只保存檔名與SHA-256，不提交原始證據或機密。

| 本機evidence檔名 | SHA-256 |
|---|---|
| red.log | `35114bf65d76c1a7dfb52f966b4aef921a0567a6f41705dafeaba35ef56673fb` |
| plugin-final.log | `1febaa0705839a8fc54b18cd7f2fc96364b4d71e37748ae6b15c993d837d3f33` |
| core-final.log | `dc1ceb7aba4baed46f360afd3347cc7779f71c06baf01f18a5297025a9d8662b` |
| package-current-check.log | `ad75dac8c5844cc754839594d65dd592c2fb24e4c259ccdb28f57f4c0d057688` |
| seven-input-check.log | `ee2e54399439ebfc9543ac589b47cafda4f58804fcad96ea0b4d031f0c41a420` |
| architecture.log | `db453b9cc14c07cc6c8bb497f0f25ed398c802dded5f7e1e04c9159ee6b66d41` |

## PROP-004核定後的實測證據

| 本機evidence檔名 | SHA-256 |
|---|---|
| package-subsets-check.log | `3a37a4d250b54d49c7d72b63d4a4f64d4bdc594bdff7579e06728bf8526dd0df` |
| merged-member-chain-worker-fixed.log | `03bc933ea24259adc9c2f04a0b27c7fe5139baf5be39223310d595e914d2d7c7` |
| subset-architecture-and-root-green.log | `3da9b9ee40366df1e19e1a3f7bb8d4fa176d987fd9861eae6568ff82fd1d56ba` |
| coverage-line.log | `50a4d15ca11ad62a2c93ed0d764c683cf02cea46802eda89b361edc32f20b66e` |

coverage-line.log 是缺收集器的失敗收集證據，不是覆蓋率通過；其餘通過證據不代表完整包驗收。
