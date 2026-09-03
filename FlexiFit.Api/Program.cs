using FirebaseAdmin;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

// =========================================
builder.Services.AddLogging();

// =======================================================
// 🛡️ 1. CONTROLLERS & SERIALIZATION CONFIGURATION
// =======================================================
builder.Services.AddControllers() // Tinanggal natin yung semicolon dito
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// =======================================================
// 🌐 2. DYNAMIC CORS POLICY (SECURED FOR CREDENTIALS)
// =======================================================
var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>();
if (corsOrigins == null || corsOrigins.Length == 0)
{
    corsOrigins = new[] { "http://localhost:5100" }; // default para sa development
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAdminPanel", policy =>
    {
        policy.WithOrigins("https://flexifit-adminpanel-b3f3csb2hqgjaufx.japaneast-01.azurewebsites.net") // Port ng Admin Panel mo
                .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS") // Explicitly named para sa credentials validation
                .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With") // Proteksyon laban sa dynamic wildcard block
                .AllowCredentials();
    });
});

// =======================================================
// 🔑 3. DUAL AUTHENTICATION SCHEME (JWT + COOKIES)
// =======================================================
// Custom JWT setup
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key is missing in appsettings.json");
var jwtIssuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is missing in appsettings.json");
var jwtAudience = jwtSection["Audience"] ?? throw new InvalidOperationException("Jwt:Audience is missing in appsettings.json");

builder.Services.AddAuthentication(options =>
{
    // Ginagawa nating fallback default scheme ang Bearer
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ClockSkew = TimeSpan.Zero
        };

        // 👇 Add this to see detailed validation errors
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                
                logger.LogError("JWT validation failed: {ExceptionMessage}", context.Exception.Message);
                
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// =======================================================
// 🗄️ 4. DATABASE & INFRASTRUCTURE SERVICES
// =======================================================
// DbContext
builder.Services.AddDbContextFactory<FlexiFitDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("FlexifitDb")
    )
); 

builder.Services.AddMemoryCache();
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true; // <---

builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<FirebaseTokenVerifier>();
builder.Services.AddScoped<DeviceTokenService>();
builder.Services.AddScoped<IUserService, UserService>(); // <-- idagdag ito
// Added Azure Blob Storage
builder.Services.AddScoped<IBlobService, BlobService>();

// =======================================================
// 🔥 5. FIREBASE ADMIN INITIALIZATION
// =======================================================
if (FirebaseApp.DefaultInstance == null)
{
    try
    {
        // ✅ 1. UNAHIN ANG ENVIRONMENT VARIABLE (Para sa Azure)
        string? firebaseJson = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT");

        if (!string.IsNullOrEmpty(firebaseJson))
        {
            FirebaseApp.Create(new AppOptions()
            {
                Credential = GoogleCredential.FromJson(firebaseJson)
            });
            Console.WriteLine("✅ Firebase initialized using environment variable.");
        }
        else
        {
            // ✅ 2. FALLBACK: BASAHIN MULA SA FILE (Para sa Local Development)
            var serviceAccountPath = Path.Combine(
                builder.Environment.ContentRootPath,
                "Credentials",
                "firebase-service-account.json");

            if (File.Exists(serviceAccountPath))
            {
                var credential = CredentialFactory.FromFile<ServiceAccountCredential>(serviceAccountPath)
                                                  .ToGoogleCredential();

                FirebaseApp.Create(new AppOptions
                {
                    Credential = credential
                });
                Console.WriteLine("✅ Firebase initialized using file: {serviceAccountPath}");
            }
            else
            {
                // ⚠️ 3. KUNG WALA, MAG-LOG NG WARNING AT HUWAG I-INITIALIZE
                Console.WriteLine("⚠️ Firebase credentials not found. Skipping Firebase initialization.");
                Console.WriteLine("   Set FIREBASE_SERVICE_ACCOUNT environment variable or add Credentials/firebase-service-account.json file.");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Firebase initialization failed: {ex.Message}");
        // Optional: throw kung kailangan talaga ang Firebase
        // throw;
    }
}

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FlexiFit.Api",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
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

// =======================================================
// 🚀 6. MIDDLEWARE EXECUTION PIPELINE
// =======================================================
var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); // Para lang sa development
}

app.UseSwagger();
app.UseSwaggerUI();

// --- ILAGAY ITO DITO ---
app.UseCors("AllowAdminPanel");
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization(); 

app.MapControllers();
app.Run();