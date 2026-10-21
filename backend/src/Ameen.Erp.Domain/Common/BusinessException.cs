namespace Ameen.Erp.Domain;

public sealed class BusinessException : Exception
{
    public string Code { get; }
    public ErrorKind Kind { get; }
    public BusinessException(string code, string message, ErrorKind kind = ErrorKind.Validation) : base(message)
    {
        Code = code;
        Kind = kind;
    }
}
