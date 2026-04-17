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
                "https://victorious-flower-0a6608b1e.2.azurestaticapps.net"
            };

        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    // Public site — restricted to GET/POST, no credentials needed
    options.AddPolicy("PublicSite", policy =>
    {
        var publicOrigins = builder.Configuration
            .GetSection("PublicSite:AllowedOrigins")
            .Get<string[]>()
            ?? (builder.Environment.IsDevelopment()
                ? new[] { "http://localhost:5173", "http://localhost:5174" }
                : new[] { "https://runnatics.com", "https://www.runnatics.com" });

        policy.WithOrigins(publicOrigins)
              .WithMethods("GET", "POST", "OPTIONS")
              .WithHeaders("Content-Type", "Accept");
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
builder.Services.AddScoped<IResultsService, ResultsService>();
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

// Rate limiting — protects public (unauthenticated) endpoints
builder.Services.AddRateLimiter(options =>
{
    // Global 429 response
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Policy: public GET endpoints — 30 requests / 60-second sliding window per IP
    options.AddSlidingWindowLimiter("PublicRead", opt =>
    {
        opt.PermitLimit = 30;
        opt.Window = TimeSpan.FromSeconds(60);
        opt.SegmentsPerWindow = 6;         // 10-second segments
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;                // reject immediately, don't queue
    });

    // Policy: public POST (contact form) — 5 requests / 60 seconds per IP
    options.AddSlidingWindowLimiter("PublicWrite", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromSeconds(60);
        opt.SegmentsPerWindow = 6;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    // Partition all rate-limit policies by remote IP
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"error":{"code":429,"message":"Too many requests. Please try again later."}}""",
            cancellationToken);
    };
});

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

app.UseCors("AllowFrontend");

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