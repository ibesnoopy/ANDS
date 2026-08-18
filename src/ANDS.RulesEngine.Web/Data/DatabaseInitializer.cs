using ANDS.RulesEngine.Web.Logging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ANDS.RulesEngine.Web.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies migrations and creates the configured initial user when it is absent.
    /// The initial credentials come from configuration (for example
    /// <c>AdminUser__Email</c> and <c>AdminUser__Password</c> environment variables)
    /// and no user is created when they are not supplied.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        var context = provider.GetRequiredService<RulesDbContext>();
        await context.Database.MigrateAsync(cancellationToken);

        var configuration = provider.GetRequiredService<IConfiguration>();
        var email = configuration["AdminUser:Email"];
        var password = configuration["AdminUser:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Log.NoInitialAdministrator(logger);
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();
        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
            Log.CreatedInitialAdministrator(logger, email);
        else
            Log.InitialAdministratorFailed(logger,
                string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
