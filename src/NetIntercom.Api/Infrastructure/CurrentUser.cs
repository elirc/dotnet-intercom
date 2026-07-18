using System.Security.Claims;
using NetIntercom.Application.Common;

namespace NetIntercom.Api.Infrastructure;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? AgentUserId =>
        Principal?.IsInRole("agent") == true
        && Guid.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public Guid? VisitorId =>
        Guid.TryParse(Principal?.FindFirstValue("visitor_id"), out var id) ? id : null;

    public bool IsAdmin => Principal?.IsInRole("admin") == true;
}
