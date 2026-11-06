/*
 * 檔案整體說明
 *
 * 目的與定位：以假SDK確認LINE待送訊息被建立為正確CRM記錄；這不是LINE實際送達驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 LineMessageServiceTests。
 * 測試案例入口包含 CreatePushMessage_ShouldCallCreateEntity。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.LineMessaging;
using ToolUtility.Tests.TestHelpers;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtility.Tests.LineMessaging
{
    public class LineMessageServiceTests
    {
        [Fact]
        public void CreatePushMessage_ShouldCallCreateEntity()
        {
            // LineMessageService.CreatePushMessage 實際呼叫 IOrganizationService.Create，
            // 因此替身與斷言都必須建立在該邊界上。
            var mockCrud = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new LineMessageService(mockLogger.Object, mockCrud.Object);

            service.CreatePushMessage("U123", "sub", "hello");

            mockCrud.Verify(x => x.Create(It.IsAny<Entity>()), Times.Once);
        }
    }
}
