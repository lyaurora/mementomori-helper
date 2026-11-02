namespace MementoMori.Exceptions;

public class ApiErrorException : Exception
{
    public ApiErrorException(ErrorCode errorCode, string? path = null) : base(TextResourceTable.GetErrorCodeMessage(errorCode))
    {
        ErrorCode = errorCode;
        Path = path;
    }

    public ErrorCode ErrorCode { get; }

    /// <summary>API path that returned the error; kept out of <see cref="Exception.Message"/>, which is shown to the player as is.</summary>
    public string? Path { get; }

    public override string ToString() => Path == null ? base.ToString() : $"{Path}: {base.ToString()}";
}