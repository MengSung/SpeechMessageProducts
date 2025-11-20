# ToolUtility.Tests

ToolUtility 專案的單元測試與整合測試專案。

## ?? 測試策略

本專案遵循 **TDD（測試驅動開發）** 原則：
```
?? Red（紅燈）  → 先寫失敗的測試
?? Green（綠燈）→ 寫最少的程式碼讓測試通過
?? Refactor（重構）→ 優化程式碼但保持測試通過
```

## ?? 測試金字塔

```
       /\
      /  \  E2E Tests（10%）
     /----\  
    /      \ Integration Tests（20%）
   /--------\
  /          \
 /____________\ Unit Tests（70%）
```

## ?? 測試覆蓋率目標

| 層級 | 目標覆蓋率 |
|------|----------|
| Utilities | 95%+ |
| Attribute Services | 90%+ |
| Entity Services | 85%+ |
| Business Services | 90%+ |
| Facade | 80%+ |
| **整體** | **85%+** |

## ??? 使用的工具

- **測試框架**: xUnit
- **Mock 框架**: Moq
- **斷言庫**: FluentAssertions
- **覆蓋率工具**: Coverlet
- **CI/CD**: GitHub Actions

## ?? 執行測試

### 本地執行所有測試
```bash
dotnet test
```

### 執行特定測試類別
```bash
dotnet test --filter "FullyQualifiedName~StringUtilityTests"
```

### 產生覆蓋率報告
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### 產生 HTML 覆蓋率報告
```bash
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:./TestResults/**/coverage.cobertura.xml -targetdir:./CoverageReport -reporttypes:Html
```

## ?? 專案結構

```
ToolUtility.Tests/
├── TestHelpers/
│   ├── MockLoggerFactory.cs       # ILogger Mock 工廠
│   ├── MockCrmClientFactory.cs    # ICrmClient Mock 工廠
│   └── TestEntityFactory.cs       # Entity 測試資料工廠
│
├── Utilities/
│   ├── StringUtilityTests.cs      # 字串工具測試
│   └── TraceUtilityTests.cs       # 追蹤工具測試
│
├── AttributeOperations/
│   ├── BoolAttributeServiceTests.cs
│   ├── IntAttributeServiceTests.cs
│   └── ... (其他屬性服務測試)
│
├── EntityOperations/
│   ├── EntityQueryServiceTests.cs
│   └── EntityCrudServiceTests.cs
│
├── ContactOperations/
│   └── ContactServiceTests.cs
│
└── Core/
    ├── ToolUtilityClassTests.cs          # Facade 單元測試
    └── ToolUtilityClassIntegrationTests.cs # Facade 整合測試
```

## ?? 撰寫測試的最佳實務

### 1. 命名規範
```csharp
[Fact]
public void MethodName_StateUnderTest_ExpectedBehavior()
{
    // Arrange
    // Act
    // Assert
}
```

### 2. Arrange-Act-Assert (AAA) 模式
```csharp
[Fact]
public void FilterDigit_WhenMixedString_ShouldReturnOnlyDigits()
{
    // Arrange（準備測試資料）
    string input = "電話: 0912-345-678";
    
    // Act（執行被測試的方法）
    var result = StringUtility.FilterDigit(input);
    
    // Assert（驗證結果）
    result.Should().Be("0912345678");
}
```

### 3. 使用 Theory 進行參數化測試
```csharp
[Theory]
[InlineData("身分證: A123456789", "123456789")]
[InlineData("電話：(02)2345-6789", "0223456789")]
[InlineData("金額：NT$ 1,234,567", "1234567")]
public void FilterDigit_WhenVariousFormats_ShouldExtractDigits(string input, string expected)
{
    var result = StringUtility.FilterDigit(input);
    result.Should().Be(expected);
}
```

### 4. 使用 FluentAssertions 讓斷言更易讀
```csharp
// ? 不推薦
Assert.Equal("expected", actual);
Assert.True(condition);

// ? 推薦
actual.Should().Be("expected");
condition.Should().BeTrue();
```

## ?? CI/CD 整合

每次推送到 `Sunny_MyPay_2.7_Utility_.Net10` 分支或建立 PR 時，都會自動執行：
1. 編譯專案
2. 執行所有測試
3. 產生覆蓋率報告
4. 檢查覆蓋率是否達到 85% 門檻

查看測試報告：
- GitHub Actions: `.github/workflows/toolutility-tests.yml`
- Codecov: https://codecov.io/gh/MengSung/ChurchReport

## ?? 相關文件

- [PR4_CLASS_REBUILD.md](../ChurchReport/文件/升級ToolUtility/PR4_CLASS_REBUILD.md) - 重構計畫
- [結論規劃.md](../ChurchReport/文件/升級ToolUtility/結論規劃.md) - 主升級計畫
- [xUnit 官方文件](https://xunit.net/)
- [Moq 官方文件](https://github.com/moq/moq4)
- [FluentAssertions 官方文件](https://fluentassertions.com/)

---

**維護者**: GitHub Copilot  
**最後更新**: 2024-01-XX  
**狀態**: ? 測試專案已建立
