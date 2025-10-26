using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Runnatics.Data.EF;
using Runnatics.Repositories.EF;
using Runnatics.Repositories.Interface;
using Runnatics.Services;
using Runnatics.Services.Interface;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
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

// Add Entity Framework with connection pooling optimizations
builder.Services.AddDbContextPool<RaceSyncDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("RunnaticsDB"), sqlOptions =>
    {
        // Command timeout for long-running queries
        sqlOptions.CommandTimeout(30);
    });
    
    // Performance optimizations
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // Default to no tracking for better performance
    options.EnableServiceProviderCaching(); // Cache service providers
    options.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
    options.EnableDetailedErrors(builder.Environment.IsDevelopment());
}, poolSize: 128); // DbContext pool size for better performance

// Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JWT");
var secretKey = jwtSettings["Key"];
var issuer = jwtSettings["Issuer"];
var audience = jwtSettings["Audience"];

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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins(
            "https://app.runnatics.com",
            "https://admin.runnatics.com",
            "http://localhost:3000",
            "http://localhost:3001"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

// Register repositories
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped(typeof(IUnitOfWork<>), typeof(UnitOfWork<>));
builder.Services.AddScoped(typeof(IUnitOfWorkFactory<>), typeof(UnitOfWorkFactory<>));

// Register services
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Add AutoMapper
builder.Services.AddAutoMapper(typeof(AutoMapperMappingProfile));

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();
});

// Add health checks
builder.Services.AddHealthChecks();
    // .AddDbContextCheck<RaceSyncDbContext>(); // Requires Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore package

// Add memory cache
builder.Services.AddMemoryCache();

// Add HTTP client
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Runnatics API V1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    });
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors("AllowSpecificOrigins");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Add health check endpoint
app.MapHealthChecks("/health");

// Seed database on startup (optional - remove in production)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<RaceSyncDbContext>();
            // Uncomment the next line if you want to ensure database is created
            // context.Database.EnsureCreated();
            
            // You can add seed data here if needed
            // await SeedDatabase(context);
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while seeding the database.");
        }
    }
}

app.Run();

// Optional: Add seed data method
// static async Task SeedDatabase(RaceSyncDbContext context)
// {
//     // Add seed data logic here
//     if (!context.Organizations.Any())
//     {
//         // Add sample organizations, users, etc.
//     }
//     
//     await context.SaveChangesAsync();
// }
