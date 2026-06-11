using System;
using System.Collections.Generic;

namespace PharmacyBillingService.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        
        public int RoleId { get; set; }
        public Role Role { get; set; }
    }

    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; }
        
        public ICollection<User> Users { get; set; }
    }

    public class Medicine
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ActiveIngredient { get; set; }
        public string Unit { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public DateTime ExpiryDate { get; set; }
    }

    public class Bill
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public string? DoctorName { get; set; }
        public decimal ExaminationFee { get; set; }
        public decimal MedicineFee { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty; // e.g., "Pending", "Paid"
        public DateTime CreatedAt { get; set; }
    }

    public class EventLog
    {
        public int Id { get; set; }
        public string EventType { get; set; } = string.Empty; // e.g. "prescription.created"
        public string Payload { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // e.g. "Success", "Failed"
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class Supplier
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty; // e.g., 'Dược phẩm', 'Vật tư y tế'
        public string Status { get; set; } = "active";    // 'active' or 'inactive'
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
