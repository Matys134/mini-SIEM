using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LogGenerator
{
    
    // 1. Definice datového modelu (přesně podle našeho JSON schématu)
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

    class Program
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        
        // Konfigurace pro generování dat
        private static readonly Random Random = new Random();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // "Databáze" fake dat
        private static readonly string[] Services = { "auth-service", "payment-service", "inventory-service", "frontend-gateway" };
        private static readonly string[] Users = { "admin", "jan_novak", "petr_svoboda", "guest_user", "system_internal" };
        private static readonly string[] IpAddresses = { "192.168.1.10", "192.168.1.11", "10.0.0.5", "172.16.0.23" };
        
        // Tuto IP budeme používat pro simulaci útočníka
        private const string AttackerIp = "66.66.66.66"; 

        static async Task Main(string[] args)
        {
            Console.WriteLine("--- SIEM Log Generator Started ---");
            Console.WriteLine("Press Ctrl+C to stop.");
            Console.WriteLine($"Watching for attacker IP: {AttackerIp}");
            Console.WriteLine("----------------------------------");

            while (true)
            {
                // 10% šance, že spustíme "ÚTOK" (série rychlých chyb)
                if (Random.NextDouble() < 0.1)
                {
                    await GenerateBruteForceAttack();
                }
                else
                {
                    GenerateNormalTraffic();
                }

                // Náhodná pauza mezi requesty (simulace přirozeného provozu)
                await Task.Delay(Random.Next(200, 1000));
            }
        }

        // Metoda pro generování běžného provozu
        private static void GenerateNormalTraffic()
        {
            var log = CreateBaseLog();
            
            // Náhodně vybereme typ události
            bool isSuccess = Random.NextDouble() > 0.05; // 95% úspěšnost

            log.LogLevel = isSuccess ? "INFO" : "ERROR";
            log.EventType = isSuccess ? "action_success" : "action_failed";
            
            // Pokud je to error, dáme tomu trochu "šťávy" pro Elasticsearch
            log.Message = isSuccess 
                ? $"Action processed successfully via {log.Endpoint}" 
                : $"Exception occurred while processing request on {log.Endpoint}. Connection timeout.";

            PrintLog(log);
        }

        // Metoda pro simulaci útoku (RTAP demo)
        private static async Task GenerateBruteForceAttack()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n>>> SIMULATING BRUTE FORCE ATTACK <<<\n");
            Console.ResetColor();

            // Vygenerujeme 5-10 rychlých failů z jedné IP
            int attempts = Random.Next(5, 10);

            for (int i = 0; i < attempts; i++)
            {
                var log = CreateBaseLog();
                
                // Přepíšeme klíčové hodnoty pro útok
                log.SourceIp = AttackerIp; // Vždy stejná IP útočníka
                log.ServiceName = "auth-service";
                log.Endpoint = "/api/v1/login";
                log.HttpMethod = "POST";
                log.LogLevel = "WARN"; // Security warning
                log.EventType = "login_failed";
                log.UserId = "admin"; // Útočník často zkouší admina
                log.Message = "Invalid password provided. Auth failed.";
                log.ResponseTimeMs = Random.Next(10, 50); // Rychlé zamítnutí

                PrintLog(log);

                // Velmi krátká pauza mezi pokusy (aby to Redis stihl zachytit v okně)
                await Task.Delay(100); 
            }

            Console.WriteLine("\n>>> ATTACK SEQUENCE FINISHED <<<\n");
        }

        // Helper pro vytvoření kostry logu
        private static LogEntry CreateBaseLog()
        {
            var service = Services[Random.Next(Services.Length)];
            
            return new LogEntry
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                ServiceName = service,
                SourceIp = IpAddresses[Random.Next(IpAddresses.Length)],
                UserId = Users[Random.Next(Users.Length)],
                HttpMethod = Random.NextDouble() > 0.7 ? "POST" : "GET",
                Endpoint = $"/api/v1/{GetEndpointForService(service)}",
                ResponseTimeMs = Random.Next(20, 500)
            };
        }

        // Pomocná metoda pro hezčí endpointy
        private static string GetEndpointForService(string service)
        {
            return service switch
            {
                "auth-service" => "login",
                "payment-service" => "process-payment",
                "inventory-service" => "check-stock",
                _ => "status"
            };
        }

        // Serializace a výpis (ZDE by se později volalo RabbitMQ/HTTP)
        private static async void PrintLog(LogEntry log)
        {
            // Výpis pro kontrolu
            string jsonString = JsonSerializer.Serialize(log, JsonOptions);
            Console.WriteLine($"Generováno: {log.EventType} z IP {log.SourceIp}");

            try
            {
                // Odeslání na Ingestion Service (běží defaultně na portu 5000-5200, zkontroluj v konzoli po spuštění)
                // Změň port podle toho, co ti napíše IngestionService po spuštění (např. http://localhost:5112)
                string url = "http://localhost:5217/api/ingest"; 
        
                await HttpClient.PostAsJsonAsync(url, log);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chyba odeslání: {ex.Message} (Běží Ingestion Service?)");
            }
        }
    }
}