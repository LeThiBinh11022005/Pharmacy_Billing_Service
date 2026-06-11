using System.Collections.Generic;

namespace PharmacyBillingService.DTOs
{
    public class PrescriptionCreatedEvent
    {
        public string PrescriptionId { get; set; }
        public int PatientId { get; set; }
        public string? DoctorName { get; set; }
        public List<PrescriptionMedicineDto> Medicines { get; set; }
    }

    public class PrescriptionMedicineDto
    {
        public int MedicineId { get; set; }
        public string? MedicineName { get; set; }
        public int Quantity { get; set; }
    }
}
