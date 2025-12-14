using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks; // Přidáno pro Task
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using Elastic.Clients.Elasticsearch;

namespace ProcessorService
{
    public class LogEntry
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string ServiceName { get; set; }
        public string LogLevel { get; set; }
        public string EventType { get; set; }
        public string SourceIp { get; set; }
        public string Message { get; set; }
        public int ResponseTimeMs { get; set; }
    }
    
    class Program
    {
        private const string QueueName = "siem-logs-queue";
        
        private static ConnectionMultiplexer _redis;
        private static IDatabase _redisDb;
        private static ElasticsearchClient _esClient;

        static async Task Main(string[] args)
        {
            Console.Title = "SIEM Processor (Redis + Elastic)";
            Console.WriteLine("--- SIEM Processor Started ---");

            // 1. PŘIPOJENÍ K REDISU
            try 
            {
                _redis = ConnectionMultiplexer.Connect("redis");
                _redisDb = _redis.GetDatabase();
                Console.WriteLine("[Init] Connected to Redis.");
            }
            catch { Console.WriteLine("[Error] Redis connection failed."); return; }

            // 2. PŘIPOJENÍ K ELASTICSEARCH (OPRAVENO)
            try
            {
                // ZDE JE ZMĚNA: Vytvoříme nastavení a definujeme DefaultIndex
                var settings = new ElasticsearchClientSettings(new Uri("http://elasticsearch:9200"))
                    .DefaultIndex("siem-logs"); // <--- TOTO VYŘEŠÍ TU CHYBU

                _esClient = new ElasticsearchClient(settings);
                Console.WriteLine("[Init] Connected to Elasticsearch.");
            }
            catch { Console.WriteLine("[Error] Elastic connection failed."); return; }

            // 3. RABBITMQ CONSUMER
            var factory = new ConnectionFactory() { HostName = "rabbitmq" };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.QueueDeclare(queue: QueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
                var consumer = new EventingBasicConsumer(channel);

                consumer.Received += async (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);

                    try 
                    {
                        var log = JsonSerializer.Deserialize<LogEntry>(message);
                        
                        AnalyzeSecurity(log);

                        await IndexToElastic(log);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] Processing failed: {ex.Message}");
                    }
                };

                channel.BasicConsume(queue: QueueName, autoAck: true, consumer: consumer);
                Console.WriteLine("Waiting for logs... Press [enter] to exit.");
                await Task.Delay(-1);
            }
        }

        // --- B) UKLÁDÁNÍ DO ELASTICSEARCH ---
        private static async Task IndexToElastic(LogEntry log)
        {
            // Díky nastavení .DefaultIndex("siem-logs") nahoře už nemusíme index psát sem.
            // Klient ho použije automaticky.
            var response = await _esClient.IndexAsync(log);

            if (response.IsValidResponse)
            {
                // Úspěch - malé zelené E
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("E"); 
                Console.ResetColor();
            }
            else
            {
                 Console.ForegroundColor = ConsoleColor.Red;
                 Console.WriteLine($"[Elastic Error] {response.DebugInformation}");
                 Console.ResetColor();
            }
        }

        // --- A) RTAP LOGIKA (REDIS) ---
        private static void AnalyzeSecurity(LogEntry log)
        {
            if (log.EventType == "login_failed" && log.LogLevel == "WARN")
            {
                string redisKey = $"suspicious_ip:{log.SourceIp}";
                long count = _redisDb.StringIncrement(redisKey);

                if (count == 1)
                {
                    _redisDb.KeyExpire(redisKey, TimeSpan.FromSeconds(10));
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"-> [Redis Watch] IP {log.SourceIp} failures: {count}/5");
                Console.ResetColor();

                if (count == 5)
                {
                    TriggerSecurityAlert(log.SourceIp, count);
                }
            }
        }

        private static void TriggerSecurityAlert(string ip, long count)
        {
            Console.WriteLine();
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"************************************************");
            Console.WriteLine($"[RTAP ALERT] BRUTE FORCE DETECTED!");
            Console.WriteLine($"Target IP: {ip}");
            Console.WriteLine($"Reason: {count} failed logins in < 10 seconds");
            Console.WriteLine($"************************************************");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}