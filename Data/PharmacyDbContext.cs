using Microsoft.EntityFrameworkCore;
using PharmacyBillingService.Models;

namespace PharmacyBillingService.Data
{
    public class PharmacyDbContext : DbContext
    {
        public PharmacyDbContext(DbContextOptions<PharmacyDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Medicine> Medicines { get; set; }
        public DbSet<Bill> Bills { get; set; }
        public DbSet<EventLog> EventLogs { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<ImportBill> ImportBills { get; set; }
        public DbSet<ImportBillMedication> ImportBillMedications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, Name = "Admin" },
                new Role { Id = 2, Name = "Doctor" },
                new Role { Id = 3, Name = "Receptionist" },
                new Role { Id = 4, Name = "Pharmacist" },
                new Role { Id = 5, Name = "Patient" }
            );

            // Configure decimal properties
            modelBuilder.Entity<Medicine>()
                .Property(m => m.Price)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Bill>()
                .Property(b => b.ExaminationFee)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Bill>()
                .Property(b => b.MedicineFee)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Bill>()
                .Property(b => b.TotalAmount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<ImportBill>()
                .Property(ib => ib.GoodsTotal)
                .HasColumnType("decimal(18,2)");
            modelBuilder.Entity<ImportBill>()
                .Property(ib => ib.DiscountTotal)
                .HasColumnType("decimal(18,2)");
            modelBuilder.Entity<ImportBill>()
                .Property(ib => ib.VatTotal)
                .HasColumnType("decimal(18,2)");
            modelBuilder.Entity<ImportBill>()
                .Property(ib => ib.FinalTotal)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<ImportBillMedication>()
                .Property(ibm => ibm.Price)
                .HasColumnType("decimal(18,2)");
            modelBuilder.Entity<ImportBillMedication>()
                .Property(ibm => ibm.Total)
                .HasColumnType("decimal(18,2)");
        }
    }
}
