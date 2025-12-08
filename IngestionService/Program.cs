using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Registrace naší RabbitMQ služby (Dependency Injection)
// Singleton = připojí se jednou při startu a spojení drží otevřené (efektivní)
builder.Services.AddSingleton<RabbitMqProducer>();

var app = builder.Build();

// --- DEFINICE API ENDPOINTU ---

app.MapPost("/api/ingest", (LogEntry log, RabbitMqProducer producer) =>
{
    // 1. Přijmeme data (ASP.NET je automaticky deserializuje z JSONu do objektu LogEntry)
    
    // 2. Pošleme je do fronty (asynchronní komunikace)
    producer.SendMessage(log);

    // 3. Okamžitě vracíme 202 Accepted. 
    // Nečekáme, až se data uloží do databáze. To je úzké hrdlo, které obcházíme.
    return Results.Accepted();
});

app.Run();

// --- POMOCNÉ TŘÍDY ---

// 1. Služba pro komunikaci s RabbitMQ
public class RabbitMqProducer : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private const string QueueName = "siem-logs-queue";

    public RabbitMqProducer()
    {
        // Tady předpokládáme, že RabbitMQ běží na localhostu (pro Docker to pak změníme)
        var factory = new ConnectionFactory { HostName = "localhost" };
        
        try 
        {
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Deklarace fronty. Pokud neexistuje, vytvoří se.
            // durable: false (pro školní projekt stačí, data v RAM)
            _channel.QueueDeclare(queue: QueueName,
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CRITICAL: Could not connect to RabbitMQ: {ex.Message}");
            throw; // Pokud nejede Rabbit, nemá smysl spouštět službu
        }
    }

    public void SendMessage(LogEntry log)
    {
        var json = JsonSerializer.Serialize(log);
        var body = Encoding.UTF8.GetBytes(json);

        // Odeslání zprávy do fronty
        _channel.BasicPublish(exchange: "",
                             routingKey: QueueName,
                             basicProperties: null,
                             body: body);
        
        Console.WriteLine($"[Sent] Log {log.Id} to RabbitMQ");
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}

// 2. Stejný model jako v Generátoru (zkopírováno)
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