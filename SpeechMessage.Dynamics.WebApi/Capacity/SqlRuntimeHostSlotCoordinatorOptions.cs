using Microsoft.Data.SqlClient;

namespace SpeechMessage.Dynamics.WebApi.Capacity;

public sealed class SqlRuntimeHostSlotCoordinatorOptions
{
    public const string RequiredDatabaseName = "SpeechMessageDynamicsControlPlane";

    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 5;
    public int QuarantineSeconds { get; set; } = 180;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("Dynamics control-plane SQL connection string is required.");
        }

        var builder = new SqlConnectionStringBuilder(ConnectionString);
        if (!string.Equals(
                builder.InitialCatalog,
                RequiredDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Dynamics coordinator must use the standalone {RequiredDatabaseName} database.");
        }

        if (CommandTimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("CommandTimeoutSeconds must be between 1 and 30.");
        }

        if (QuarantineSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException("QuarantineSeconds must be between 1 and 3600.");
        }
    }
}
