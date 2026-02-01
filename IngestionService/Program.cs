using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Registrace RabbitMQ producenta jako Singleton služby.
builder.Services.AddSingleton<RabbitMqProducer>();

var app = builder.Build();

// API endpointy

app.MapPost("/api/ingest", (LogEntry log, RabbitMqProducer producer) =>
{
    // 1. Validace a přijetí dat
    // 2. Odeslání zprávy do fronty pro asynchronní zpracování
    producer.SendMessage(log);

    // 3. Okamžité vrácení HTTP 202 Accepted.
    return Results.Accepted();
});

app.Run();

// Třída zapouzdřující komunikaci s RabbitMQ message brokerem.
public class RabbitMqProducer : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private const string QueueName = "siem-logs-queue";

    public RabbitMqProducer()
    {
        // Konfigurace připojení
        var factory = new ConnectionFactory { HostName = "rabbitmq" };
        
        try 
        {
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Deklarace fronty
            _channel.QueueDeclare(queue: QueueName,
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CRITICAL: Chyba při připojení k RabbitMQ: {ex.Message}");
            throw;
        }
    }

    public void SendMessage(LogEntry log)
    {
        var json = JsonSerializer.Serialize(log);
        var body = Encoding.UTF8.GetBytes(json);
        
        _channel.BasicPublish(exchange: "",
                             routingKey: QueueName,
                             basicProperties: null,
                             body: body);
        
        Console.WriteLine($"[Sent] Log ID: {log.Id} odeslán do RabbitMQ.");
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}

// Datový model pro logy
public class LogEntry
{
    public string Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ServiceName { get; set; }
    public string LogLevel { get; set; }
    public string EventType { get; set; }
    public string SourceIp { get; set; }
    public string UserId { get; set; }
    public string HttpMethod { get; set; }
    public string Endpoint { get; set; }
    public int ResponseTimeMs { get; set; }
    public string Message { get; set; }
}