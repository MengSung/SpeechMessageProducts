using System;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// Shared fail-closed validator for the worker startup probe and every later identity
/// operation. It keeps target identity and version rules SDK-free and deterministic.
/// </summary>
public static class OfficialCrmIdentityValidator
{
    public static bool IsValid(
        Guid userId,
        Guid businessUnitId,
        Guid organizationId,
        Guid expectedOrganizationId,
        Version? connectedOrganizationVersion,
        string expectedCeVersion)
    {
        if (userId == Guid.Empty ||
            businessUnitId == Guid.Empty ||
            organizationId == Guid.Empty ||
            expectedOrganizationId == Guid.Empty ||
            organizationId != expectedOrganizationId ||
            connectedOrganizationVersion is null)
        {
            return false;
        }

        return expectedCeVersion switch
        {
            "8.2" => connectedOrganizationVersion.Major == 8 &&
                     connectedOrganizationVersion.Minor == 2,
            "9.1" => connectedOrganizationVersion.Major == 9 &&
                     connectedOrganizationVersion.Minor == 1,
            _ => false
        };
    }
}
