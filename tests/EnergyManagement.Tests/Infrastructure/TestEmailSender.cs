using System.Collections.Concurrent;
using EnergyManagement.Api.Modules.Identity;

namespace EnergyManagement.Tests.Infrastructure;

public sealed record TestEmail(string Recipient, string Subject, string Body);

public sealed class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<TestEmail> _messages = new();

    public IReadOnlyCollection<TestEmail> Messages => _messages.ToArray();

    public Task SendAsync(string recipient, string subject, string body,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _messages.Enqueue(new TestEmail(recipient, subject, body));
        return Task.CompletedTask;
    }
}
