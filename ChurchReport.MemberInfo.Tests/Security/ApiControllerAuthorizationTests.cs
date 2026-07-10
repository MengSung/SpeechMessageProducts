using System;
using System.Linq;
using ChurchReport.Controllers;
using ChurchReport.Controllers.ApiControllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

public sealed class ApiControllerAuthorizationTests
{
    [Fact]
    public void ApiControllersServingPrivateDataRequireAuthorization()
    {
        var controllerTypes = new[]
        {
            typeof(SchedulerDataController),
            typeof(SpiritLeaderLookupController)
        };

        foreach (var controllerType in controllerTypes)
        {
            controllerType
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .Should()
                .NotBeEmpty($"{controllerType.Name} returns or mutates ChurchReport user-scoped data");
        }
    }

    [Fact]
    public void SpiritLeaderLookup_AllowsRequestedActiveList()
    {
        var activeListId = Guid.NewGuid();

        SpiritLeaderLookupController.CanAccessRequestedListForTesting(
                activeListId.ToString(),
                Array.Empty<string>(),
                activeListId.ToString())
            .Should()
            .BeTrue();
    }

    [Fact]
    public void SpiritLeaderLookup_AllowsRequestedLoadedMultiGroupList()
    {
        var requestedListId = Guid.NewGuid();
        var records = new[]
        {
            Guid.NewGuid().ToString(),
            requestedListId.ToString()
        };

        SpiritLeaderLookupController.CanAccessRequestedListForTesting(
                Guid.NewGuid().ToString(),
                records,
                requestedListId.ToString())
            .Should()
            .BeTrue();
    }

    [Fact]
    public void SpiritLeaderLookup_DeniesRequestedListOutsideCurrentScope()
    {
        var records = new[]
        {
            Guid.NewGuid().ToString()
        };

        SpiritLeaderLookupController.CanAccessRequestedListForTesting(
                Guid.NewGuid().ToString(),
                records,
                Guid.NewGuid().ToString())
            .Should()
            .BeFalse();
    }
}
