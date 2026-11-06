using System.Buffers.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using OrderManagement.Application.Interfaces;

using OrderManagement.Application.Services;

using OrderManagement.Infrastructure.Data;

using OrderManagement.Infrastructure.Repositories;
using OrderManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(
    (serviceProvider, options) =>
    {
        var configuration = serviceProvider
            .GetRequiredService<IConfiguration>();

        var connectionString = configuration
            .GetConnectionString("DefaultConnection");

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
builder.Services.AddOpenApi();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();


public partial class Program
{
}