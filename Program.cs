using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PharmacyBillingService.Data;
using PharmacyBillingService.Models;
using System.Text;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

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
var pharmConnString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("PostgresConnection")
    ?? "Host=localhost;Port=5436;Database=PharmacyDB;Username=postgres;Password=Medicare@2024";
builder.Services.AddDbContext<PharmacyDbContext>(options =>
    options.UseNpgsql(pharmConnString));

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
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ClinicAuthService",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ClinicUsers",
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
            
            // Self-healing check: Ensure the Suppliers, ImportBills, and ImportBillMedications tables exist in the Postgres database
            var conn = dbContext.Database.GetDbConnection();
            using (var cmd = conn.CreateCommand())
            {
                if (conn.State != System.Data.ConnectionState.Open) conn.Open();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""Suppliers"" (
                        ""Id"" SERIAL PRIMARY KEY,
                        ""Code"" TEXT NOT NULL,
                        ""Name"" TEXT NOT NULL,
                        ""Phone"" TEXT NOT NULL,
                        ""Email"" TEXT NOT NULL,
                        ""Address"" TEXT NOT NULL,
                        ""Group"" TEXT NOT NULL,
                        ""Status"" TEXT NOT NULL,
                        ""CreatedDate"" TIMESTAMP WITH TIME ZONE NOT NULL
                    );
                    
                    CREATE TABLE IF NOT EXISTS ""ImportBills"" (
                        ""Id"" SERIAL PRIMARY KEY,
                        ""SupplierCode"" TEXT NOT NULL,
                        ""SupplierName"" TEXT NOT NULL,
                        ""Date"" TIMESTAMP WITH TIME ZONE NOT NULL,
                        ""Creator"" TEXT NOT NULL,
                        ""Note"" TEXT NOT NULL,
                        ""GoodsTotal"" DECIMAL(18,2) NOT NULL,
                        ""DiscountTotal"" DECIMAL(18,2) NOT NULL,
                        ""VatTotal"" DECIMAL(18,2) NOT NULL,
                        ""FinalTotal"" DECIMAL(18,2) NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS ""ImportBillMedications"" (
                        ""Id"" SERIAL PRIMARY KEY,
                        ""ImportBillId"" INTEGER NOT NULL,
                        ""Code"" TEXT NOT NULL,
                        ""Name"" TEXT NOT NULL,
                        ""Batch"" TEXT NOT NULL,
                        ""ExpiryDate"" TIMESTAMP WITH TIME ZONE NOT NULL,
                        ""Qty"" INTEGER NOT NULL,
                        ""Unit"" TEXT NOT NULL,
                        ""Price"" DECIMAL(18,2) NOT NULL,
                        ""Total"" DECIMAL(18,2) NOT NULL
                    );
                    
                    ALTER TABLE ""Bills"" ADD COLUMN IF NOT EXISTS ""DoctorName"" TEXT NULL;";
                cmd.ExecuteNonQuery();
            }

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
            // Seed default medicines only if the table is empty
            if (!dbContext.Medicines.Any())
            {
                var defaultMedicines = new List<Medicine>
                {
                    new Medicine { Name = "Paracetamol 500mg", ActiveIngredient = "Paracetamol", Unit = "Viên", Price = 2000, StockQuantity = 1000, ExpiryDate = DateTime.UtcNow.AddYears(2) },
                    new Medicine { Name = "Amoxicillin 500mg", ActiveIngredient = "Amoxicillin", Unit = "Viên", Price = 5000, StockQuantity = 500, ExpiryDate = DateTime.UtcNow.AddYears(2) },
                    new Medicine { Name = "Ibuprofen 400mg", ActiveIngredient = "Ibuprofen", Unit = "Viên", Price = 3000, StockQuantity = 800, ExpiryDate = DateTime.UtcNow.AddYears(2) },
                    new Medicine { Name = "Cetirizine 10mg", ActiveIngredient = "Cetirizine", Unit = "Viên", Price = 1500, StockQuantity = 1200, ExpiryDate = DateTime.UtcNow.AddYears(2) },
                    new Medicine { Name = "Metformin 500mg", ActiveIngredient = "Metformin", Unit = "Viên", Price = 4000, StockQuantity = 600, ExpiryDate = DateTime.UtcNow.AddYears(2) }
                };
                dbContext.Medicines.AddRange(defaultMedicines.ToArray());
                dbContext.SaveChanges();
                Console.WriteLine("[PharmacyBillingService] Seeded default medicines successfully.");
            }
            else
            {
                Console.WriteLine("[PharmacyBillingService] Medicines already seeded. Skipping seeding.");
            }
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
