namespace MementoMori;

public class AccountInfo
{
    public string Name { get; set; }
    public long UserId { get; set; }
    public string ClientKey { get; set; }
    public bool AutoLogin { get; set; }
    public long AutoLoginWorldId { get; set; }

    // null = not paused, DateTimeOffset.MaxValue = until resumed; blocks scheduled jobs and auto-login
    public DateTimeOffset? PauseUntil { get; set; }

    // ortegauuid device id, kept per account like the client's stored UUID (the server never issues one)
    public string OrtegaUuid { get; set; }
}