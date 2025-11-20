using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.Utilities;

namespace ToolUtility.Tests.Utilities
{
    /// <summary>
    /// StringUtility 單元測試
    /// 遵循 TDD 原則：先寫測試（紅燈），再寫實作（綠燈），最後重構（藍燈）
    /// </summary>
    public class StringUtilityTests
    {
        #region DeleteLastComma 測試

        [Fact]
        public void DeleteLastComma_WhenStringEndsWithComma_ShouldRemoveIt()
        {
            // Arrange
            string input = "測試，項目，";
            
            // Act
            StringUtility.DeleteLastComma(ref input);
            
            // Assert
            input.Should().Be("測試，項目");
        }

        [Fact]
        public void DeleteLastComma_WhenStringDoesNotEndWithComma_ShouldNotChange()
        {
            // Arrange
            string input = "測試項目";
            string expected = input;
            
            // Act
            StringUtility.DeleteLastComma(ref input);
            
            // Assert
            input.Should().Be(expected);
        }

        [Fact]
        public void DeleteLastComma_WhenStringIsNull_ShouldNotThrow()
        {
            // Arrange
            string input = null;
            
            // Act
            Action act = () => StringUtility.DeleteLastComma(ref input);
            
            // Assert
            act.Should().NotThrow();
            input.Should().BeNull();
        }

        [Fact]
        public void DeleteLastComma_WhenStringIsEmpty_ShouldRemainEmpty()
        {
            // Arrange
            string input = string.Empty;
            
            // Act
            StringUtility.DeleteLastComma(ref input);
            
            // Assert
            input.Should().BeEmpty();
        }

        [Fact]
        public void DeleteLastComma_WhenStringIsOnlyComma_ShouldRemoveIt()
        {
            // Arrange
            string input = "，";
            
            // Act
            StringUtility.DeleteLastComma(ref input);
            
            // Assert
            input.Should().BeEmpty();
        }

        #endregion

        #region FilterDigit 測試

        [Fact]
        public void FilterDigit_WhenMixedString_ShouldReturnOnlyDigits()
        {
            // Arrange
            string input = "電話: 0912-345-678";
            
            // Act
            var result = StringUtility.FilterDigit(input);
            
            // Assert
            result.Should().Be("0912345678");
        }

        [Fact]
        public void FilterDigit_WhenOnlyDigits_ShouldReturnSame()
        {
            // Arrange
            string input = "1234567890";
            
            // Act
            var result = StringUtility.FilterDigit(input);
            
            // Assert
            result.Should().Be("1234567890");
        }

        [Fact]
        public void FilterDigit_WhenNoDigits_ShouldReturnEmpty()
        {
            // Arrange
            string input = "ABC測試XYZ";
            
            // Act
            var result = StringUtility.FilterDigit(input);
            
            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void FilterDigit_WhenNull_ShouldReturnEmpty()
        {
            // Arrange
            string input = null;
            
            // Act
            var result = StringUtility.FilterDigit(input);
            
            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void FilterDigit_WhenEmpty_ShouldReturnEmpty()
        {
            // Arrange
            string input = string.Empty;
            
            // Act
            var result = StringUtility.FilterDigit(input);
            
            // Assert
            result.Should().BeEmpty();
        }

        [Theory]
        [InlineData("身分證: A123456789", "123456789")]
        [InlineData("電話：(02)2345-6789", "0223456789")]
        [InlineData("金額：NT$ 1,234,567", "1234567")]
        public void FilterDigit_WhenVariousFormats_ShouldExtractDigits(string input, string expected)
        {
            // Act
            var result = StringUtility.FilterDigit(input);
            
            // Assert
            result.Should().Be(expected);
        }

        #endregion

        #region DeleteLastChar 測試（未來實作）

        [Fact]
        public void DeleteLastChar_WhenStringHasCharacters_ShouldRemoveLast()
        {
            // Arrange
            string input = "測試字串";
            
            // Act
            StringUtility.DeleteLastChar(ref input);
            
            // Assert
            input.Should().Be("測試字");
        }

        [Fact]
        public void DeleteLastChar_WhenStringIsNull_ShouldNotThrow()
        {
            // Arrange
            string input = null;
            
            // Act
            Action act = () => StringUtility.DeleteLastChar(ref input);
            
            // Assert
            act.Should().NotThrow();
            input.Should().BeNull();
        }

        [Fact]
        public void DeleteLastChar_WhenStringIsEmpty_ShouldRemainEmpty()
        {
            // Arrange
            string input = string.Empty;
            
            // Act
            StringUtility.DeleteLastChar(ref input);
            
            // Assert
            input.Should().BeEmpty();
        }

        [Fact]
        public void DeleteLastChar_WhenStringIsSingleChar_ShouldBeEmpty()
        {
            // Arrange
            string input = "A";
            
            // Act
            StringUtility.DeleteLastChar(ref input);
            
            // Assert
            input.Should().BeEmpty();
        }

        #endregion
    }
}
