using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Melodee.Tests.Common.TestHelpers;

internal sealed class RecordingLogEventSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public string Output => string.Join(
        Environment.NewLine,
        _events.Select(x => x.RenderMessage()));

    public void Emit(LogEvent logEvent)
    {
        _events.Enqueue(logEvent);
    }
}
