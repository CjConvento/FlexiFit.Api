using FirebaseAdmin;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Google.Apis.Auth.OAuth2;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace FlexiFit.Api.Extensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services, 
        IConfiguration configuration, 
        IWebHostEnvironment environment)
    {

    // =======================================================
    // 🛡️ 1. CONTROLLERS & SERIALIZATION CONFIGURATION
    // =======================================================
    services.AddControllers() // Tinanggal natin yung semicolon dito
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

    // 2. CORS
    services.AddCors(options =>
    {
        options.AddPolicy("AllowAdminPanel", policy =>
        {
            policy.WithOrigins(
                    "https://flexifitadminpanel.onrender.com"   // ← ADMIN PANEL
                )
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });

    // =======================================================
    // 🗄️ 3. DATABASE & INFRASTRUCTURE SERVICES
    // =======================================================
    // DbContext
    services.AddDbContextFactory<FlexiFitDbContext>(options =>
        options.UseNpgsql(
            configuration.GetConnectionString("FlexifitDb"),
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure() // para sa retry on failure
        )
    ); 

    services.AddMemoryCache();
    Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true; // <---
    
    // =======================================================
    // 4. SCOPED SERVICES REGISTRATION
    // =======================================================
    services.AddScoped<JwtService>();
    services.AddScoped<FirebaseTokenVerifier>();
    services.AddScoped<DeviceTokenService>();
    services.AddScoped<IUserService, UserService>();
    services.AddScoped<IBlobService, AppwriteStorageService>();
    // services.AddScoped<IBlobService, BlobService>(); // if using azure blob 


    // =======================================================
    // 5. NUTRITION, WORKOUT, PROGRESSION LOGIC
    // =======================================================
    // Centralized nutrition logic
    services.AddScoped<NutritionCalculator>();
    services.AddScoped<MealPlanGenerator>();

    // Progression Logic
    services.AddScoped<ProgramProgressionService>();

    // Progress Tracking Logic
    services.AddScoped<ProgressTrackerService>();

    // Nutrition Service for meal group fetching and mapping
    services.AddScoped<NutritionService>();

    // Nutrition Target Adjustment Logic
    services.AddScoped<NutritionTargetService>();

    // Workout Plan Generation Logic
    services.AddScoped<WorkoutPlanGenerator>();

    // =======================================================
    // 6. FIREBASE ADMIN SDK INITIALIZATION
    // =======================================================
    InitializeFirebase(environment);
    
    return services;
    }

    private static void InitializeFirebase(IWebHostEnvironment environment)
    {
        if (FirebaseApp.DefaultInstance != null)
        {
            return;
        }

        try
        {
            //  1. UNAHIN: RENDER SECRET FILE (Production)
            var secretPath = "/etc/secrets/firebase-service-account.json";

            if (File.Exists(secretPath))
            {
                var credential = GoogleCredential.FromFile(secretPath);

                FirebaseApp.Create(new AppOptions()
                {
                    Credential = credential
                });
                Console.WriteLine("Firebase initialized using Render Secret File.");

                return; // exit the method after successful initialization
            }

            //  2. ENVIRONMENT VARIABLE (Azure/Render) — with Base64 support
            else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT")))
            {
                string? firebaseJson = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT");

                // Try to decode as Base64 first
                try
                {
                    byte[] data = Convert.FromBase64String(firebaseJson!);
                    firebaseJson = System.Text.Encoding.UTF8.GetString(data);
                    Console.WriteLine(" Firebase JSON decoded from Base64.");
                }
                catch
                {
                    Console.WriteLine("⚠️ FIREBASE_SERVICE_ACCOUNT is not Base64. Using as-is.");
                }

                FirebaseApp.Create(new AppOptions()
                {
                    Credential = GoogleCredential.FromJson(firebaseJson)
                });
                Console.WriteLine(" Firebase initialized using environment variable.");

                return;
            }

            // 3. NO CREDENTIALS FOUND (Default state if Render Secrets/Env Vars are missing)
            Console.WriteLine("⚠️ Firebase credentials not found. Skipping Firebase initialization.");
            Console.WriteLine("Set FIREBASE_SERVICE_ACCOUNT env var or add Credentials/firebase-service-account.json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Firebase initialization failed: {ex.Message}");
            // Optional: throw kung kailangan talaga ang Firebase
            // throw;
        }
 
    }
}