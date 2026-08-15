# P7 下一個 capability 選擇審查

## 本機審查

- 權威 matrix：70 rows 皆為 `temporary-legacy`，67 consumers 未遷移。
- 00031/00032/00033 均因 Session、InMemoryContext 與保存 credential 的 ListManager graph 而 fail closed。
- 00063 有 paging-result 與 QR/attendance write adjacency；00064/00066 保留 `Entity`/mutable fee workflow；
  00055/00056 不可與 login/credential identity graph 分離。
- 既有 small-group app-named catalog 缺四個 assignment fields 與有效日，不能成為 authorization authority。

## 外部模型

使用專案 self-healing runner 並行嘗試 Gemini 與 Claude，最大 45 秒。Gemini timeout；Claude 無 usable
output。結果為「雙模型未完成」，不宣稱 dual-model review 成功，採上述本機 source/matrix evidence。

## 結論

建立 server-owned assignment source prerequisite，而非啟動任何 direct consumer cutover、CE、P7.5 或 P8。
