namespace NetIntercom.Domain.Common;

/// <summary>Thrown when a domain invariant is violated. Mapped to HTTP 422 by the API.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
