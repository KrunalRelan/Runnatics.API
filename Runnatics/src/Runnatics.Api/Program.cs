using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Runnatics.Configuration;
using Runnatics.Data.EF;
using Runnatics.Repositories.EF;
using Runnatics.Repositories.Interface;
using FluentValidation;
using Runnatics.Services;
using Runnatics.Services.Interface;
using Runnatics.Services.Mappings;
using Runnatics.Services.Validators;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add controllers + JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Runnatics API",
        Version = "v1",
        Description = "API for Runnatics race timing management system",
        Contact = new OpenApiContact
        {
            Name = "Runnatics Support",
            Email = "support@runnatics.com"
        }
    });

    c.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.'));

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// EF Core
builder.Services.AddDbContextPool<RaceSyncDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("RunnaticsDB"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(60);
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null
        );
    });

    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    options.EnableServiceProviderCaching();
    options.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
    options.EnableDetailedErrors(builder.Environment.IsDevelopment());
}, poolSize: 128);

// JWT Auth
var jwtSettings = builder.Configuration.GetSection("JWT");
var secretKey = jwtSettings["Key"];
var issuer = jwtSettings["Issuer"];
var audience = jwtSettings["Audience"];

if (string.IsNullOrWhiteSpace(secretKey))
{
    throw new InvalidOperationException("JWT:Key is not configured.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Environment.IsDevelopment()
            ? new[] { "http://localhost:3000", "http://localhost:5173" }
            : new[]
            {
                builder.Configuration["AppSettings:FrontendUrl"] ?? "https://racetik.com",
                "https://racetik.com",
                "https://www.racetik.com",
                "https://runnatics.com",
                "https://www.runnatics.com",
                "https://victorious-flower-0a6608b1e.2.azurestaticapps.net"
            };

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });

    options.AddPolicy("SignalR", policy =>
    {
        var origins = builder.Environment.IsDevelopment()
            ? new[] { "http://localhost:3000", "http://localhost:5173" }
            : new[]
            {
                builder.Configuration["AppSettings:FrontendUrl"] ?? "https://racetik.com",
                "https://racetik.com",
                "https://www.racetik.com",
                "https://runnatics.com",
                "https://www.runnatics.com",
                "https://victorious-flower-0a6608b1e.2.azurestaticapps.net"
            };

        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    // Public site — no credentials needed
    options.AddPolicy("PublicSite", policy =>
    {
        var publicOrigins = builder.Configuration
            .GetSection("PublicSite:AllowedOrigins")
            .Get<string[]>()
            ?? (builder.Environment.IsDevelopment()
                ? new[] { "http://localhost:5173", "http://localhost:5174", "http://localhost:5173/login" }
                : new[]
                {
                    "https://runnatics.com",
                    "https://www.runnatics.com",
                    "https://racetik.com",
                    "https://www.racetik.com"
                });

        policy.WithOrigins(publicOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// HttpContext accessor
builder.Services.AddHttpContextAccessor();

// Encryption Singletons
builder.Services.AddSingleton<IdEncryptor>();
builder.Services.AddSingleton<IdDecryptor>();
builder.Services.AddSingleton<IdListEncryptor>();
builder.Services.AddSingleton<IdListDecryptor>();
builder.Services.AddSingleton<NullableIdEncryptor>();
builder.Services.AddSingleton<NullableIdDecryptor>();

// User context
builder.Services.AddScoped<IUserContextService, UserContextService>();

// Repositories
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped(typeof(IUnitOfWork<>), typeof(UnitOfWork<>));
builder.Services.AddScoped(typeof(IUnitOfWorkFactory<>), typeof(UnitOfWorkFactory<>));

// Services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IEventsService, EventsService>();
builder.Services.AddScoped<IEventOrganizerService, EventOrganizerService>();
builder.Services.AddScoped<IRacesService, RaceService>();
builder.Services.AddScoped<IParticipantImportService, ParticipantImportService>();
builder.Services.AddScoped<ICheckpointsService, CheckpointService>();
builder.Services.AddScoped<IDevicesService, DevicesService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICertificatesService, CertificatesService>();
builder.Services.AddScoped<IRFIDImportService, RFIDImportService>();
builder.Services.AddScoped<IRFIDDiagnosticsService, RFIDDiagnosticsService>();
builder.Services.AddScoped<IResultsService, ResultsService>();
builder.Services.AddScoped<IPublicResultsService, PublicResultsService>();
builder.Services.AddScoped<IResultsExportService, ResultsExportService>();
builder.Services.AddScoped<IBibMappingService, BibMappingService>();
builder.Services.AddScoped<ISupportQueryService, SupportQueryService>();
builder.Services.AddHttpClient<ISmsService, Msg91SmsService>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<CreateBibMappingValidator>();

// RFID Reader Background Service
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<MockRfidReaderService>();
}
// In Production, only register if explicitly enabled via config
else if (builder.Configuration.GetValue<bool>("R700Settings:Enabled", false))
{
    builder.Services.AddHostedService<RfidReaderService>();
}

// Add Encryption Service
builder.Services.AddEncryptionService(builder.Configuration);

// RFID Online Integration (SignalR hubs + online tag ingestion)
builder.Services.AddRfidOnlineIntegration(builder.Configuration);

// AutoMapper
builder.Services.AddAutoMapper(typeof(AutoMapperMappingProfile));

// Logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();
});

// Rate limiting — protects public (unauthenticated) endpoints, partitioned per remote IP
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 60 requests / 1 minute per IP
    options.AddPolicy<string>("PublicRead", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // 5 requests / 10 minutes per IP
    options.AddPolicy<string>("PublicWrite", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"error":{"code":429,"message":"Too many requests. Please try again later."}}""",
            cancellationToken);
    };
});

// Response caching (required by [ResponseCache(VaryByQueryKeys = ...)])
builder.Services.AddResponseCaching();

// Health checks & misc
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

var app = builder.Build();

// Swagger — enabled in all environments
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Runnatics API V1");
    c.RoutePrefix = "swagger";
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// X-Public-Key guard for all /api/public/* routes
// Skip OPTIONS — browser preflight requests never carry custom headers
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/public") &&
        !HttpMethods.IsOptions(context.Request.Method))
    {
        var expectedKey = app.Configuration["PublicApi:Key"];
        var providedKey = context.Request.Headers["X-Public-Key"].ToString();

        if (string.IsNullOrEmpty(providedKey) || providedKey != expectedKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":{"code":401,"message":"Missing or invalid X-Public-Key header."}}""");
            return;
        }
    }
    await next(context);
});

app.UseCors("AllowFrontend");

app.UseResponseCaching();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRfidHubs();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<RaceSyncDbContext>();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();