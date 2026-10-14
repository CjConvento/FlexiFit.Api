using FlexiFit.Api.Extensions;

// ============================================================
// 1. GLOBAL SETTINGS
// ============================================================
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// =======================================================
// 2. BUILD WEB APPLICATION
// =======================================================
var builder = WebApplication.CreateBuilder(args);

// =======================================================
// 3. REGISTER SERVICES VIA EXTENSION METHODS
// =======================================================
builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);
builder.Services.AddIdentityServices(builder.Configuration);

// =======================================================
// 4. BUILD AND CONFIGURE PIPELINE
// =======================================================
var app = builder.Build();

// =======================================================
// 5. CALL ASYNC PIPELINE CONFIGURATION METHOD
// =======================================================
await app.ConfigurePipelineAsync(builder.Environment);