namespace ANDS.RulesEngine.Web.Logging;

internal static partial class Log
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "No initial administrator configured; set AdminUser:Email and AdminUser:Password to create one.")]
    public static partial void NoInitialAdministrator(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Created initial administrator {Email}.")]
    public static partial void CreatedInitialAdministrator(ILogger logger, string email);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error,
        Message = "Unable to create initial administrator: {Errors}")]
    public static partial void InitialAdministratorFailed(ILogger logger, string errors);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "User {Email} signed in.")]
    public static partial void SignedIn(ILogger logger, string email);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Sign in for {Email} was blocked by lockout.")]
    public static partial void SignInLockedOut(ILogger logger, string email);
}
