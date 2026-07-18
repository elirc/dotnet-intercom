using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetIntercom.Application.Reports;

namespace NetIntercom.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/reports")]
[Authorize(Policy = "Agent")]
public class ReportsController : ControllerBase
{
    private readonly ISender _sender;

    public ReportsController(ISender sender) => _sender = sender;

    [HttpGet("overview")]
    public Task<OverviewReportDto> Overview([FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
        _sender.Send(new GetOverviewReportQuery(
            from ?? DateTime.UtcNow.AddDays(-30), to ?? DateTime.UtcNow));

    /// <summary>SLA met/breached counts grouped by team and timer kind for the period.</summary>
    [HttpGet("sla")]
    public Task<IReadOnlyList<SlaTeamReportRow>> Sla([FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
        _sender.Send(new GetSlaReportQuery(
            from ?? DateTime.UtcNow.AddDays(-30), to ?? DateTime.UtcNow));
}
