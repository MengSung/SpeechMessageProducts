namespace SpeechMessage.Dynamics.WorkerHost;

public interface IOfficialCrmClientFactory
{
    IOfficialCrmClient Create(string profileGenerationId);
}
