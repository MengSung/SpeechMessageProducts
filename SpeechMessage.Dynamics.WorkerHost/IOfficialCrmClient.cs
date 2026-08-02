using System;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerHost;

public interface IOfficialCrmClient : IDisposable
{
    bool IsReady { get; }

    WorkerValue Execute(WorkerRequestV1 request);
}
