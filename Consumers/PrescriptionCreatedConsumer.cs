using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PharmacyBillingService.Data;
using PharmacyBillingService.DTOs;
using PharmacyBillingService.Models;

namespace PharmacyBillingService.Consumers
{
    public class PrescriptionCreatedConsumer : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PrescriptionCreatedConsumer> _logger;
        private IConnection _connection;
        private IChannel _channel;

        public PrescriptionCreatedConsumer(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<PrescriptionCreatedConsumer> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
            InitializeRabbitMQ();
        }

        private async void InitializeRabbitMQ()
        {
            var hostName = _configuration["RabbitMQ:HostName"] ?? "localhost";
            var factory = new ConnectionFactory { HostName = hostName };

            try
            {
                _connection = await factory.CreateConnectionAsync();
                _channel = await _connection.CreateChannelAsync();

                await _channel.QueueDeclareAsync(queue: "prescription.created",
                                     durable: true,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);

                _logger.LogInformation($"Connected to RabbitMQ at {hostName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to RabbitMQ.");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_channel == null) return;

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                _logger.LogInformation($"Received message: {message}");

                try
                {
                    var prescriptionEvent = JsonSerializer.Deserialize<PrescriptionCreatedEvent>(message);
                    if (prescriptionEvent != null)
                    {
                        await ProcessPrescriptionEvent(prescriptionEvent);
                    }
                    await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message.");
                    // In a real system, you might want to Nack or send to a Dead Letter Queue
                    await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            await _channel.BasicConsumeAsync(queue: "prescription.created", autoAck: false, consumer: consumer);
        }

        private async Task ProcessPrescriptionEvent(PrescriptionCreatedEvent ev)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();

            decimal totalMedicineFee = 0;
            bool hasInsufficientStock = false;

            // Optional: Start a transaction
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                foreach (var item in ev.Medicines)
                {
                    var medicine = await context.Medicines.FindAsync(item.MedicineId);
                    if (medicine == null)
                    {
                        _logger.LogWarning($"Medicine with ID {item.MedicineId} not found.");
                        continue;
                    }

                    if (medicine.StockQuantity < item.Quantity)
                    {
                        _logger.LogWarning($"Insufficient stock for Medicine {medicine.Name}. Required: {item.Quantity}, Available: {medicine.StockQuantity}");
                        hasInsufficientStock = true;
                        // Depending on business rules, we could break or continue. Let's assume we reject the whole order if any is out of stock.
                        break;
                    }

                    medicine.StockQuantity -= item.Quantity;
                    totalMedicineFee += medicine.Price * item.Quantity;
                }

                if (hasInsufficientStock)
                {
                    // Rollback if insufficient stock
                    await transaction.RollbackAsync();
                    _logger.LogError("Prescription processing failed due to insufficient stock. No bill created.");
                    throw new InvalidOperationException("Insufficient stock");
                }

                // Create Bill
                var bill = new Bill
                {
                    PatientId = ev.PatientId,
                    ExaminationFee = 0, // Assumption: Medical Record Service might handle this or it's default 0 for pharmacy only
                    MedicineFee = totalMedicineFee,
                    TotalAmount = totalMedicineFee,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                };

                context.Bills.Add(bill);
                
                // Add Event Log
                var eventLog = new EventLog
                {
                    EventType = "prescription.created",
                    Payload = JsonSerializer.Serialize(ev),
                    Status = "Success",
                    Timestamp = DateTime.UtcNow
                };
                context.EventLogs.Add(eventLog);

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation($"Successfully processed prescription {ev.PrescriptionId} and created Bill {bill.Id}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                
                // Try to log the failure in a new transaction
                try 
                {
                    var errorLog = new EventLog
                    {
                        EventType = "prescription.created",
                        Payload = JsonSerializer.Serialize(ev),
                        Status = "Failed",
                        ErrorMessage = ex.Message,
                        Timestamp = DateTime.UtcNow
                    };
                    context.EventLogs.Add(errorLog);
                    await context.SaveChangesAsync();
                } 
                catch { /* Ignore if it fails */ }
                
                throw;
            }
        }

        public override async void Dispose()
        {
            if (_channel != null)
                await _channel.CloseAsync();
            if (_connection != null)
                await _connection.CloseAsync();
            base.Dispose();
        }
    }
}
