using System;
using ChurchReport.Controllers;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

public sealed class PersonalImageAuthorizationTests
{
    [Fact]
    public void CanViewPersonalContactImage_AllowsLoginContact()
    {
        var loginContactId = Guid.NewGuid();
        var loginContact = new Entity("contact", loginContactId);

        PersonalController.CanViewPersonalContactImageForTesting(loginContact, loginContactId)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void CanViewPersonalContactImage_DeniesDifferentContact()
    {
        var loginContact = new Entity("contact", Guid.NewGuid());

        PersonalController.CanViewPersonalContactImageForTesting(loginContact, Guid.NewGuid())
            .Should()
            .BeFalse();
    }

    [Fact]
    public void BuildPersonalContactImageCacheKey_IncludesViewerContact()
    {
        var viewerId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var key = PersonalController.BuildPersonalContactImageCacheKeyForTesting(viewerId, contactId, 80);

        key.Should().Contain(viewerId.ToString("N"));
        key.Should().Contain(contactId.ToString("N"));
        key.Should().Contain(":80");
    }
}
