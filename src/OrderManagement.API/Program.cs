using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using OrderManagement.Application.Interfaces;

using OrderManagement.Application.Services;

using OrderManagement.Infrastructure.Data;

using OrderManagement.Infrastructure.Repositories;


var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
      "Connection string 'DefaultConnection' is missing or empty.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
// Add services to the container.

builder.Services.AddScoped<IProductRepository, ProductRepository>();

builder.Services.AddScoped<IProductService, ProductService>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}   


app.UseAuthorization();

app.MapControllers();

app.Run();
