using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NetIntercom.Api.Infrastructure;
using NetIntercom.Api.Realtime;
using NetIntercom.Application;
using NetIntercom.Application.Common;
using NetIntercom.Infrastructure;
using NetIntercom.Infrastructure.Auth;
using NetIntercom.Infrastructure.BackgroundJobs;
using NetIntercom.Infrastructure.Persistence;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

// ----- application + infrastructure -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ----- realtime -----
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<IPresenceService>(sp => sp.GetRequiredService<PresenceTracker>());
builder.Services.AddSingleton<IRealtimeNotifier, RealtimeNotifier>();
builder.Services.AddHostedService<PresenceSweeper>();

// ----- auth: one JWT scheme, role-based policies for agents/admins/visitors -----
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            // SignalR clients pass the JWT via query string on hub endpoints.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Agent", p => p.RequireRole("agent"))
    .AddPolicy("Admin", p => p.RequireRole("admin"))
    .AddPolicy("Visitor", p => p.RequireRole("visitor"));

// ----- API conventions -----
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1.0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ----- CORS: inbox app (configured origins) and the embeddable widget (any site) -----
var inboxOrigins = builder.Configuration.GetSection("Cors:InboxOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("inbox", p => p
        .WithOrigins(inboxOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
    options.AddPolicy("widget", p => p
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// ----- rate limiting on public endpoints -----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("widget", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// ----- health + tracing -----
var connectionString = builder.Configuration.GetConnectionString("Default")!;
builder.Services.AddHealthChecks().AddNpgSql(connectionString);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("netintercom-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation());

// ----- background jobs (Hangfire, PostgreSQL storage so schedules survive restarts) -----
var hangfireEnabled = builder.Configuration.GetValue("Hangfire:Enabled", true);
if (hangfireEnabled)
{
    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));
    builder.Services.AddHangfireServer();
}

var app = builder.Build();

// ----- database migrate + seed -----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.GetMigrations().Any())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    if (app.Configuration.GetValue("Seed:Enabled", false))
    {
        await DbSeeder.SeedAsync(db,
            scope.ServiceProvider.GetRequiredService<IPasswordService>(),
            app.Logger);
    }
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("inbox");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<AgentHub>("/hubs/agent").RequireCors("inbox");
app.MapHub<VisitorHub>("/hubs/visitor").RequireCors("widget");
app.MapHealthChecks("/healthz");

if (hangfireEnabled)
{
    app.MapHangfireDashboard("/hangfire").RequireAuthorization("Admin");
    RecurringJob.AddOrUpdate<SlaBreachJob>("sla-breach-check", j => j.RunAsync(), Cron.Minutely);
}

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program { }
