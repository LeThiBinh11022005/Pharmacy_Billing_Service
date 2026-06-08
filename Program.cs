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
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgresConnection") 
        ?? "Host=localhost;Port=5432;Database=medicare_full;Username=postgres;Password=YourPassword"));

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

// Automatically apply migrations and seed data at startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
    try
    {
        dbContext.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PharmacyBillingService] Database.EnsureCreated failed or database already exists: {ex.Message}. Continuing...");
    }

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
    
    // Seed 10 real medications if empty
    if (!dbContext.Medicines.Any())
    {
        var defaultMedicines = new List<PharmacyBillingService.Models.Medicine>
        {
            new PharmacyBillingService.Models.Medicine { Name = "Paracetamol 500mg", ActiveIngredient = "Paracetamol", Unit = "Viên", Price = 1500, StockQuantity = 520, ExpiryDate = DateTime.UtcNow.AddYears(2) },
            new PharmacyBillingService.Models.Medicine { Name = "Amoxicillin 500mg", ActiveIngredient = "Amoxicillin", Unit = "Viên", Price = 2500, StockQuantity = 180, ExpiryDate = DateTime.UtcNow.AddYears(2) },
            new PharmacyBillingService.Models.Medicine { Name = "Vitamin C 500mg", ActiveIngredient = "Ascorbic Acid", Unit = "Viên", Price = 1200, StockQuantity = 65, ExpiryDate = DateTime.UtcNow.AddYears(1) },
            new PharmacyBillingService.Models.Medicine { Name = "Omeprazole 20mg", ActiveIngredient = "Omeprazole", Unit = "Viên", Price = 3200, StockQuantity = 240, ExpiryDate = DateTime.UtcNow.AddYears(2) },
            new PharmacyBillingService.Models.Medicine { Name = "Cefixime 200mg", ActiveIngredient = "Cefixime", Unit = "Viên", Price = 4500, StockQuantity = 40, ExpiryDate = DateTime.UtcNow.AddYears(2) },
            new PharmacyBillingService.Models.Medicine { Name = "Clorpheniramin 4mg", ActiveIngredient = "Chlorpheniramine", Unit = "Viên", Price = 900, StockQuantity = 310, ExpiryDate = DateTime.UtcNow.AddYears(3) },
            new PharmacyBillingService.Models.Medicine { Name = "Dung dịch NaCl 0.9%", ActiveIngredient = "Sodium Chloride", Unit = "Chai", Price = 8000, StockQuantity = 90, ExpiryDate = DateTime.UtcNow.AddYears(1) },
            new PharmacyBillingService.Models.Medicine { Name = "Metformin 500mg", ActiveIngredient = "Metformin", Unit = "Viên", Price = 2800, StockQuantity = 150, ExpiryDate = DateTime.UtcNow.AddYears(2) },
            new PharmacyBillingService.Models.Medicine { Name = "Ibuprofen 400mg", ActiveIngredient = "Ibuprofen", Unit = "Viên", Price = 2000, StockQuantity = 80, ExpiryDate = DateTime.UtcNow.AddYears(2) },
            new PharmacyBillingService.Models.Medicine { Name = "Acetylcistein 200mg", ActiveIngredient = "Acetylcysteine", Unit = "Hộp", Price = 75000, StockQuantity = 30, ExpiryDate = DateTime.UtcNow.AddYears(2) }
        };
        dbContext.Medicines.AddRange(defaultMedicines);
        Console.WriteLine("[PharmacyBillingService] Seeded 10 real medicines successfully!");
    }

    // Seed sample bills for patient (PatientId=4) if empty
    if (!dbContext.Bills.Any())
    {
        var sampleBills = new List<PharmacyBillingService.Models.Bill>
        {
            new PharmacyBillingService.Models.Bill
            {
                PatientId = 4,
                ExaminationFee = 150000,
                MedicineFee = 85000,
                TotalAmount = 235000,
                Status = "Paid",
                CreatedAt = DateTime.UtcNow.AddDays(-15)
            },
            new PharmacyBillingService.Models.Bill
            {
                PatientId = 4,
                ExaminationFee = 200000,
                MedicineFee = 320000,
                TotalAmount = 520000,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow.AddDays(-7)
            },
            new PharmacyBillingService.Models.Bill
            {
                PatientId = 4,
                ExaminationFee = 0,
                MedicineFee = 175000,
                TotalAmount = 175000,
                Status = "Paid",
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            },
            new PharmacyBillingService.Models.Bill
            {
                PatientId = 4,
                ExaminationFee = 300000,
                MedicineFee = 0,
                TotalAmount = 300000,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new PharmacyBillingService.Models.Bill
            {
                PatientId = 4,
                ExaminationFee = 150000,
                MedicineFee = 450000,
                TotalAmount = 600000,
                Status = "Paid",
                CreatedAt = DateTime.UtcNow.AddHours(-12)
            }
        };
        dbContext.Bills.AddRange(sampleBills);
        Console.WriteLine("[PharmacyBillingService] Seeded 5 sample bills for patient successfully!");
    }

    // Seed sample prescription event logs for patient if empty
    if (!dbContext.EventLogs.Any())
    {
        var sampleEvents = new List<PharmacyBillingService.Models.EventLog>
        {
            new PharmacyBillingService.Models.EventLog
            {
                EventType = "prescription.created",
                Payload = "{\"PrescriptionId\":3102,\"PatientId\":4,\"Medicines\":[{\"MedicineId\":1,\"Quantity\":10},{\"MedicineId\":3,\"Quantity\":5}]}",
                Status = "Processed",
                Timestamp = DateTime.UtcNow.AddDays(-5)
            },
            new PharmacyBillingService.Models.EventLog
            {
                EventType = "prescription.created",
                Payload = "{\"PrescriptionId\":3148,\"PatientId\":4,\"Medicines\":[{\"MedicineId\":2,\"Quantity\":20},{\"MedicineId\":4,\"Quantity\":15},{\"MedicineId\":6,\"Quantity\":30}]}",
                Status = "Processed",
                Timestamp = DateTime.UtcNow.AddDays(-2)
            },
            new PharmacyBillingService.Models.EventLog
            {
                EventType = "prescription.created",
                Payload = "{\"PrescriptionId\":3205,\"PatientId\":4,\"Medicines\":[{\"MedicineId\":5,\"Quantity\":12},{\"MedicineId\":9,\"Quantity\":8}]}",
                Status = "Success",
                Timestamp = DateTime.UtcNow.AddHours(-6)
            }
        };
        dbContext.EventLogs.AddRange(sampleEvents);
        Console.WriteLine("[PharmacyBillingService] Seeded 3 sample prescription events for patient successfully!");
    }

    dbContext.SaveChanges();
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
