using FluentValidation;
using Harbourline.Api.Data;
using Harbourline.Api.Imports;
using Harbourline.Api.Infrastructure;
using Harbourline.Api.Repositories;
using Harbourline.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Harbourline")
    ?? throw new InvalidOperationException("Connection string 'Harbourline' is missing.");

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<HarbourlineDbContext>(o =>
        o.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));
}

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNameCaseInsensitive = true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<IRegionRepository, RegionRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IHandlerRepository, HandlerRepository>();
builder.Services.AddScoped<IPolicyholderRepository, PolicyholderRepository>();
builder.Services.AddScoped<IPolicyRepository, PolicyRepository>();
builder.Services.AddScoped<IClaimRepository, ClaimRepository>();
builder.Services.AddScoped<IClaimNoteRepository, ClaimNoteRepository>();
builder.Services.AddScoped<IImportBatchRepository, ImportBatchRepository>();
builder.Services.AddScoped<IReportRepository, ReportRepository>();

builder.Services.AddScoped<IRegionService, RegionService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IHandlerService, HandlerService>();
builder.Services.AddScoped<IPolicyholderService, PolicyholderService>();
builder.Services.AddScoped<IPolicyService, PolicyService>();
builder.Services.AddScoped<IClaimService, ClaimService>();
builder.Services.AddScoped<IClaimNoteService, ClaimNoteService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IImportBatchService, ImportBatchService>();
builder.Services.AddScoped<IBordereauImportService, BordereauImportService>();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
    DatabaseMigrator.Run(connectionString);

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.MapControllers();
app.Run();

public partial class Program { }
