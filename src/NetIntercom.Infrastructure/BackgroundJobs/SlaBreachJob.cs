using MediatR;
using NetIntercom.Application.Sla;

namespace NetIntercom.Infrastructure.BackgroundJobs;

/// <summary>
/// Executed by Hangfire on a one-minute recurring schedule (registered in Program).
/// Hangfire persists the schedule in PostgreSQL, so checks resume after restarts.
/// </summary>
public class SlaBreachJob
{
    private readonly ISender _sender;

    public SlaBreachJob(ISender sender) => _sender = sender;

    public async Task RunAsync() => await _sender.Send(new CheckSlaBreachesCommand());
}
