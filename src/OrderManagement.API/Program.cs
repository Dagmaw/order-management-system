using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Interfaces;
using OrderManagement.Application.Services;
using OrderManagement.Infrastructure.Data;
using OrderManagement.Infrastructure.Repositories;
using OrderManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using FluentValidation;
using OrderManagement.Application.Validators;
using OrderManagement.API.ExceptionHandling;
using Serilog;
using Serilog.Formatting.Compact;
using OrderManagement.API.Middleware;

var builder = WebApplication.CreateBuilder(args);

var useDatabaseEnvironment = builder.Configuration
    .GetValue<bool>("Database:UseEnvironmentVariables");

var useReverseProxy = builder.Configuration
    .GetValue<bool>("ReverseProxy:Enabled");

if (useReverseProxy)
{
    var trustedNetworks = builder.Configuration
        .GetSection("ReverseProxy:KnownNetworks")
        .Get<string[]>() ?? [];

    if (trustedNetworks.Length == 0)
    {
        throw new InvalidOperationException(
            "Configure trusted proxy networks before enabling the reverse proxy.");
    }

    var parsedNetworks = trustedNetworks
        .Select(System.Net.IPNetwork.Parse)
        .ToArray();

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var network in parsedNetworks)
        {
            options.KnownIPNetworks.Add(network);
        }
    });
}

builder.Services.AddHealthChecks();

builder.Services.AddSerilog((services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "OrderManagement.API")
        .WriteTo.Console(new RenderedCompactJsonFormatter());
});

builder.Services.AddDbContext<ApplicationDbContext>(
    (serviceProvider, options) =>
    {
        var configuration = serviceProvider
            .GetRequiredService<IConfiguration>();

        var connectionString = useDatabaseEnvironment
            ? BuildDatabaseConnection(configuration)
            : configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is missing or empty.");
        }

        options.UseNpgsql(connectionString);
    });

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 15;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();
// Add services to the container.

builder.Services.AddScoped<IProductRepository, ProductRepository>();

builder.Services.AddScoped<IProductService, ProductService>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
OrderManagement.API.Documentation.ApiDocumentation.AddApiDocumentation(builder.Services);

builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddScoped<IOrderRepository, OrderRepository>();

builder.Services.AddScoped<IOrderService, OrderService>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Issuer),
        "Jwt:Issuer is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Audience),
        "Jwt:Audience is required.")
    .Validate(
        options => options.AccessTokenMinutes is >= 1 and <= 60,
        "Jwt:AccessTokenMinutes must be between 1 and 60.")
    .Validate(
        options => options.HasValidSigningKey(),
        "Jwt:SigningKeyBase64 must contain a Base64-encoded key of at least 32 bytes.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>(
        (bearerOptions, jwtOptions) =>
        {
            var settings = jwtOptions.Value;

            bearerOptions.MapInboundClaims = false;
            bearerOptions.IncludeErrorDetails = false;

            bearerOptions.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,

                    ValidateAudience = true,
                    ValidAudience = settings.Audience,

                    ValidateLifetime = true,
                    RequireExpirationTime = true,

                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,

                    IssuerSigningKey = new SymmetricSecurityKey(
                        Convert.FromBase64String(
                            settings.SigningKeyBase64)),

                    ValidAlgorithms = new[]
                    {
                        SecurityAlgorithms.HmacSha256
                    },

                    NameClaimType = "sub",
                    RoleClaimType = "role",

                    ClockSkew = TimeSpan.FromSeconds(30)
                };
        });

builder.Services.AddAuthorization();

builder.Services.AddValidatorsFromAssemblyContaining<
    CreateProductDtoValidator>();

builder.Services.AddProblemDetails();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();


var app = builder.Build();


await using (var scope = app.Services.CreateAsyncScope())
{
    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole>>();

    foreach (var roleName in new[] { "Customer", "Admin" })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(
                new IdentityRole(roleName));

            if (!result.Succeeded &&
                !await roleManager.RoleExistsAsync(roleName))
            {
                throw new InvalidOperationException(
                    $"Unable to initialize the {roleName} role.");
            }
        }
    }

    if (app.Environment.IsDevelopment() &&
        app.Configuration.GetValue<bool>("BootstrapAdmin:Enabled"))
    {
        var email = app.Configuration["BootstrapAdmin:Email"];

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                "BootstrapAdmin:Email is required.");
        }

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        var account = await userManager.FindByEmailAsync(
            email.Trim().ToLowerInvariant());

        if (account is null)
        {
            throw new InvalidOperationException(
                "Register the designated development admin account first.");
        }

        if (!await userManager.IsInRoleAsync(account, "Admin"))
        {
            var result = await userManager.AddToRoleAsync(
                account,
                "Admin");

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Unable to assign the development Admin role.");
            }
        }
    }
}

if (useReverseProxy)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSerilogRequestLogging();

app.UseExceptionHandler();


if (!app.Environment.IsDevelopment())
{
    // ALB health checks reach the target over HTTP.
    app.UseWhen(
        context => !context.Request.Path.Equals(new PathString("/health")),
        branch => branch.UseHttpsRedirection());
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue<bool?>("ApiDocs:Enabled") ?? app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("../openapi/v1.json", "Order Management API v1");
        options.DocumentTitle = "Order Management API";
        options.ConfigObject.PersistAuthorization = false;
        options.ConfigObject.AdditionalItems["validatorUrl"] = null;
    });
}

app.MapHealthChecks("/health").AllowAnonymous();

app.MapControllers();

app.Run();


static string BuildDatabaseConnection(IConfiguration configuration)
{
    static string Required(IConfiguration settings, string name)
    {
        var value = settings[name];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required database setting '{name}' is missing.");
        }

        return value;
    }

    var rootCertificate = Required(configuration, "PGSSLROOTCERT");

    if (!File.Exists(rootCertificate))
    {
        throw new InvalidOperationException("The RDS CA certificate file is missing.");
    }

    return new NpgsqlConnectionStringBuilder
    {
        Host = Required(configuration, "PGHOST"),
        Port = 5432,
        Database = Required(configuration, "PGDATABASE"),
        Username = Required(configuration, "PGUSER"),
        Password = Required(configuration, "PGPASSWORD"),
        SslMode = SslMode.VerifyFull,
        GssEncryptionMode = GssEncryptionMode.Disable,
        RootCertificate = rootCertificate,
        Timeout = 30,
        CommandTimeout = 30,
        IncludeErrorDetail = false
    }.ConnectionString;
}

public partial class Program
{
}
