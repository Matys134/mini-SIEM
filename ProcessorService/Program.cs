using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks; 
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

            // 1. INICIALIZACE REDIS KLIENTA
            try 
            {
                _redis = ConnectionMultiplexer.Connect("redis");
                _redisDb = _redis.GetDatabase();
                Console.WriteLine("[Init] Připojeno k Redis.");
            }
            catch { Console.WriteLine("[Error] Nepodařilo se připojit k Redis."); return; }

            // 2. INICIALIZACE ELASTICSEARCH KLIENTA
            try
            {
                // Konfigurace klienta včetně výchozího indexu pro ukládání logů
                var settings = new ElasticsearchClientSettings(new Uri("http://elasticsearch:9200"))
                    .DefaultIndex("siem-logs"); 

                _esClient = new ElasticsearchClient(settings);
                Console.WriteLine("[Init] Připojeno k Elasticsearch.");
            }
            catch { Console.WriteLine("[Error] Nepodařilo se připojit k Elasticsearch."); return; }

            // 3. KONFIGURACE RABBITMQ CONSUMERA
            var factory = new ConnectionFactory() { HostName = "rabbitmq" };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.QueueDeclare(queue: QueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
                var consumer = new EventingBasicConsumer(channel);

                // Event handler pro příchozí zprávy
                consumer.Received += async (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);

                    try 
                    {
                        var log = JsonSerializer.Deserialize<LogEntry>(message);
                        
                        // Krok A: Real-time bezpečnostní analýza (Redis)
                        AnalyzeSecurity(log);

                        // Krok B: Archivace a indexace (Elasticsearch)
                        await IndexToElastic(log);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] Chyba při zpracování zprávy: {ex.Message}");
                    }
                };

                // Spuštění konzumu zpráv
                channel.BasicConsume(queue: QueueName, autoAck: true, consumer: consumer);
                
                Console.WriteLine("Služba běží a čeká na logy. Stiskněte [Enter] pro ukončení.");
                await Task.Delay(-1);
            }
        }
        
        // indexace logu do Elasticsearch pro vizualizaci v Kibaně.
        private static async Task IndexToElastic(LogEntry log)
        {
            var response = await _esClient.IndexAsync(log);

            if (response.IsValidResponse)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("E"); // Vizuální indikace úspěšné indexace
                Console.ResetColor();
            }
            else
            {
                 Console.ForegroundColor = ConsoleColor.Red;
                 Console.WriteLine($"[Elastic Error] {response.DebugInformation}");
                 Console.ResetColor();
            }
        }
        
        // Real-time detekce bezpečnostních hrozeb pomocí Redis
        // Implementuje logiku pro detekci Brute Force útoků.
        private static void AnalyzeSecurity(LogEntry log)
        {
            // Detekce: Opakované selhání přihlášení (WARN)
            if (log.EventType == "login_failed" && log.LogLevel == "WARN")
            {
                string redisKey = $"suspicious_ip:{log.SourceIp}";
                
                // Atomická inkrementace počitadla v Redisu
                long count = _redisDb.StringIncrement(redisKey);

                // Nastavení expirace klíče (časové okno) pouze při prvním výskytu
                if (count == 1)
                {
                    _redisDb.KeyExpire(redisKey, TimeSpan.FromSeconds(10));
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"-> [Redis Watch] IP {log.SourceIp} failures: {count}/5");
                Console.ResetColor();

                // Prahová hodnota pro vyvolání poplachu
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
            Console.WriteLine($"Condition: {count} failed logins in < 10 seconds");
            Console.WriteLine($"************************************************");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}