// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/Package01OperationRegistryTests.cs
// 目的：鎖定 Package 0/1 操作註冊表，避免首發範圍漂移。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

public sealed class Package01OperationRegistryTests
{
    [Fact]
    public void Package01_registry_contains_expected_runtime_and_fee_read_operations()
    {
        var ids = Package01OperationRegistry.All.Select(x => x.CapabilityOperationId).OrderBy(x => x).ToArray();

        ids.Should().Contain(new[]
        {
            OperationIds.RuntimeHealthWhoAmI,
            OperationIds.RuntimePoolValidateConnection,
            OperationIds.MetadataOptionSetByAttribute,
            OperationIds.FeeDedicationRetrieveByContact,
            OperationIds.FeeDedicationRetrieveByContactDateRange,
            OperationIds.FeesRetrieveByDedicationPeriod,
            OperationIds.FeesEditorLoadByDiscipleLesson,
            OperationIds.LessonsStorRetrieveByContact,
            OperationIds.LessonsStorRetrieveByDiscipleLesson
        });

        // 首發只應有這 9 個，避免不小心把 temporary-legacy 操作塞進 registry。
        ids.Should().HaveCount(9);
    }

    [Theory]
    [InlineData(OperationIds.FeeDedicationRetrieveByContact, "contactId")]
    [InlineData(OperationIds.FeeDedicationRetrieveByContactDateRange, "startDate")]
    [InlineData(OperationIds.FeesRetrieveByDedicationPeriod, "paidPeriod")]
    public void Fee_read_operations_require_expected_parameters(string operationId, string requiredParameter)
    {
        Package01OperationRegistry.TryGet(operationId, out var definition).Should().BeTrue();
        definition!.Parameters.Should().Contain(p => p.Name == requiredParameter && p.Required);
    }

    [Fact]
    public void Capability_operation_ids_match_required_pattern()
    {
        foreach (var definition in Package01OperationRegistry.All)
        {
            definition.CapabilityOperationId.Should().MatchRegex("^[a-z0-9]+(\\.[a-z0-9]+)+$");
            definition.TemplateHash.Should().HaveLength(64);
        }
    }
}
