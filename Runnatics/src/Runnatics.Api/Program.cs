using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Runnatics.Api.Hubs;
using Runnatics.Data.EF;
using Runnatics.Repositories.EF;
using Runnatics.Repositories.Interface;
using Runnatics.Services;
using Runnatics.Services.Interface;
using Runnatics.Services.Mappings;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add controllers + JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 102400; // 100KB
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

    // Ensure unique schema Ids for types that share the same simple name across namespaces
    // Use the full name (namespace + type) which prevents collisions like EventStatus in different assemblies
    c.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.'));

    // Add JWT Bearer token support in Swagger
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
        sqlOptions.CommandTimeout(30);
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

    // Configure JWT events for SignalR
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];

            // If the request is for our hub...
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                // Read the token out of the query string
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// CORS configuration for both API and SignalR
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });

    // SignalR CORS policy (allows credentials)
    options.AddPolicy("SignalRCors", policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true) // Allow any origin for dev
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
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
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IEventsService, EventsService>();
builder.Services.AddScoped<IEventOrganizerService, EventOrganizerService>();
builder.Services.AddScoped<IRacesService, RaceService>();
builder.Services.AddScoped<IParticipantImportService, ParticipantImportService>();
builder.Services.AddScoped<ICheckpointsService, CheckpointService>();
builder.Services.AddScoped<IDevicesService, DevicesService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICertificatesService, CertificatesService>();

// SignalR Notification Service
builder.Services.AddScoped<IRaceNotificationService, RaceNotificationService>();

// Add Encryption Service
builder.Services.AddEncryptionService(builder.Configuration);

// AutoMapper
builder.Services.AddAutoMapper(typeof(AutoMapperMappingProfile));

// Logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();
});

// Health checks & misc
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

var app = builder.Build();

// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Runnatics API V1");
        c.RoutePrefix = string.Empty;
    });
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// ❗ For now, disable HTTPS redirection to avoid preflight redirects in dev
// app.UseHttpsRedirection();

app.UseRouting();

// ✅ CORS: after routing, before auth
app.UseCors("DevCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<RaceHub>("/hubs/race").RequireCors("SignalRCors");

// (Optional) seeding
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<RaceSyncDbContext>();
        // context.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
