using NetIntercom.Application.Common;

namespace NetIntercom.Infrastructure.Services;

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
