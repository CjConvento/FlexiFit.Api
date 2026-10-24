using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlexiFit.Api.Extensions;

public static class PipelineExtensions
{
    public static async Task ConfigurePipelineAsync(this WebApplication app, IWebHostEnvironment environment)
    {
        // =======================================================
        // DATABASE CONNECTION HEALTH CHECK (Safe)
        // =======================================================
        using (var scope = app.Services.CreateScope())
        {
            var logger2 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<FlexiFitDbContext>();

            try
            {
                var canConnect = await dbContext.Database.CanConnectAsync();

                if (canConnect)
                {
                    logger2.LogInformation(" DATABASE CONNECTION: SUCCESS");
                    
                    try
                    {
                        var userCount = await dbContext.UsrUsers.CountAsync();
                        var foodCount = await dbContext.NtrFoodItems.CountAsync();
                        var workoutCount = await dbContext.WrkWorkouts.CountAsync();

                        logger2.LogInformation("📊 Users: {Users} | Foods: {Foods} | Workouts: {Workouts}", 
                            userCount, foodCount, workoutCount);
                    }
                    catch (Exception ex)
                    {
                        logger2.LogWarning("⚠️ Database connected but query failed: {Message}", ex.Message);
                    }
                }
                else
                {
                    logger2.LogError("❌ DATABASE CONNECTION: FAILED");
                }
            }
            catch (Npgsql.NpgsqlException npgsqlEx)
            {
                logger2.LogError("❌ PostgreSQL Error: {Message}", npgsqlEx.Message);
                
                if (npgsqlEx.Message.Contains("Network is unreachable"))
                {
                    logger2.LogError("💡 HINT: IPv6 issue. Use IPv4 pooler: aws-0-[region].pooler.supabase.com:5432");
                }
                else if (npgsqlEx.Message.Contains("password authentication failed"))
                {
                    logger2.LogError("💡 HINT: Wrong password. Check Supabase database password.");
                }
            }
            catch (Exception ex)
            {
                logger2.LogError("❌ Database Error: {Message}", ex.Message);
            }
        }

        var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
        appLogger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage(); // Para lang sa development
        }

        // =======================================================
        // MIDDLEWARE PIPELINE CONFIGURATION
        // =======================================================
        app.UseSwagger();

        // CDNs ROUTING: Pinapabilis ang rendering ng Swagger panel.
        // Sa halip na i-download ang 1.5MB assets, hihilahin ito sa pinakamalapit na node.
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "FlexiFit.Api v1");
            options.IndexStream = () => typeof(Program).Assembly.GetManifestResourceStream("FlexiFit.Api.wwwroot.swagger.index.html") 
                                 ?? new System.IO.FileStream(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "swagger", "index.html"), System.IO.FileMode.Open, System.IO.FileAccess.Read);
        });

        app.UseCors("AllowAdminPanel"); // --- Para makausap ang Admin Panel ---
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization(); 

        app.MapControllers();
        app.Run();
    }
}