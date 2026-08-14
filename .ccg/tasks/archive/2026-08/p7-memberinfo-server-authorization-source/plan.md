# 實作計畫索引

依 Trellis `implement.md` 的四階段執行：先寫 red contract/isolation tests，再建 Abstractions/Data8/ProductClient
data plane，接著建立 ChurchReport security adapter，最後完成 full quality/review/archive。任何 Session、
InMemoryContext、legacy ListManager、無界 list query、partial response、fallback 或 consumer wiring 均屬 scope violation。
