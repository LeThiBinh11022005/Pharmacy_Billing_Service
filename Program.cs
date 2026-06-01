using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PharmacyBillingService.Data;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
});

// Configure Swagger with JWT
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PharmacyBillingService API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter JWT with Bearer into field (e.g. 'Bearer [token]')",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
    {
        new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        },
        new string[] { }
    }});
});

// Configure EF Core
builder.Services.AddDbContext<PharmacyDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? "Host=localhost;Database=PharmacyDB;Username=postgres;Password=Medicare@2024"));

// Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SuperSecretKeyForJwtAuthenticationInPharmacyBillingService123!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "PharmacyBillingService",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "PharmacyBillingService",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("DoctorOnly", policy => policy.RequireRole("Doctor"));
    options.AddPolicy("ReceptionistOnly", policy => policy.RequireRole("Receptionist"));
    options.AddPolicy("PatientOnly", policy => policy.RequireRole("Patient"));
});
builder.Services.AddHostedService<PharmacyBillingService.Consumers.PrescriptionCreatedConsumer>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || true) // Enable swagger even if not development in docker
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Automatically apply migrations and seed data at startup with a robust retry loop
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
    int maxRetries = 15;
    int delaySeconds = 3;
    bool success = false;

    for (int i = 1; i <= maxRetries; i++)
    {
        try
        {
            dbContext.Database.EnsureCreated();
            success = true;
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PharmacyBillingService Retry {i}/{maxRetries}] Failed to connect to DB: {ex.Message}. Retrying in {delaySeconds}s...");
            System.Threading.Thread.Sleep(delaySeconds * 1000);
        }
    }

    if (success)
    {
        try
        {
            // Seed default users for each role if they don't exist
            var defaultUsers = new[]
            {
                new { Username = "admin",        Password = "Admin@123",        RoleId = 1 },
                new { Username = "doctor",       Password = "Doctor@123",       RoleId = 2 },
                new { Username = "receptionist", Password = "Receptionist@123", RoleId = 3 },
                new { Username = "patient",      Password = "Patient@123",      RoleId = 4 },
            };

            foreach (var u in defaultUsers)
            {
                if (!dbContext.Users.Any(x => x.Username == u.Username))
                {
                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                    var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(u.Password));
                    var hash = Convert.ToBase64String(bytes);

                    dbContext.Users.Add(new PharmacyBillingService.Models.User
                    {
                        Username     = u.Username,
                        PasswordHash = hash,
                        RoleId       = u.RoleId
                    });
                }
            }
            dbContext.SaveChanges();
            Console.WriteLine("[PharmacyBillingService] Successfully ensured DB & seeded users.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PharmacyBillingService Seeding Error] {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("[PharmacyBillingService FATAL] Could not connect to Pharmacy DB after all retries.");
    }
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
