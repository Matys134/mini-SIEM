using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LogGenerator
{
    // Definice datového modelu odpovídajícího kontraktu Ingestion Service
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
        private static readonly Random Random = new Random();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        
        // Statická data pro generování variabilního provozu
        private static readonly string[] Services = { "auth-service", "payment-service", "inventory-service", "frontend-gateway" };
        private static readonly string[] Users = { "admin", "jan_novak", "petr_svoboda", "guest_user", "system_internal" };
        private static readonly string[] IpAddresses = { "192.168.1.10", "192.168.1.11", "10.0.0.5", "172.16.0.23" };
        
        // IP adresa vyhrazená pro simulaci útočníka (Brute Force)
        private const string AttackerIp = "66.66.66.66"; 

        static async Task Main(string[] args)
        {
            Console.WriteLine("--- SIEM Log Generator Started ---");
            Console.WriteLine($"Target Attacker IP: {AttackerIp}");
            Console.WriteLine("----------------------------------");

            while (true)
            {
                // Pravděpodobnostní model: 10% šance na spuštění sekvence útoku
                if (Random.NextDouble() < 0.1)
                {
                    await GenerateBruteForceAttack();
                }
                else
                {
                    GenerateNormalTraffic();
                }

                // Simulace náhodného zpoždění mezi požadavky (200-1000ms)
                await Task.Delay(Random.Next(200, 1000));
            }
        }
        
        // Generuje standardní provozní logy (směs úspěšných a chybových stavů).
        private static void GenerateNormalTraffic()
        {
            var log = CreateBaseLog();
            
            // 95% úspěšnost požadavků
            bool isSuccess = Random.NextDouble() > 0.05;

            log.LogLevel = isSuccess ? "INFO" : "ERROR";
            log.EventType = isSuccess ? "action_success" : "action_failed";
            
            log.Message = isSuccess 
                ? $"Action processed successfully via {log.Endpoint}" 
                : $"Exception occurred while processing request on {log.Endpoint}. Connection timeout.";

            PrintAndSendLog(log);
        }
        
        // Simuluje Brute Force útok (série rychlých selhání přihlášení z jedné IP).
        private static async Task GenerateBruteForceAttack()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n>>> SIMULATING BRUTE FORCE ATTACK <<<\n");
            Console.ResetColor();

            // Vygenerování sekvence 5-10 neúspěšných pokusů
            int attempts = Random.Next(5, 10);

            for (int i = 0; i < attempts; i++)
            {
                var log = CreateBaseLog();
                
                // Nastavení parametrů specifických pro útok
                log.SourceIp = AttackerIp;
                log.ServiceName = "auth-service";
                log.Endpoint = "/api/v1/login";
                log.HttpMethod = "POST";
                log.LogLevel = "WARN"; 
                log.EventType = "login_failed";
                log.UserId = "admin"; 
                log.Message = "Invalid password provided. Auth failed.";
                log.ResponseTimeMs = Random.Next(10, 50); 

                PrintAndSendLog(log);

                // Krátká prodleva pro simulaci rychlého skriptu
                await Task.Delay(100); 
            }

            Console.WriteLine("\n>>> ATTACK SEQUENCE FINISHED <<<\n");
        }

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
        
        // Odesílá vygenerovaný log na API Ingestion Service.
        private static async void PrintAndSendLog(LogEntry log)
        {
            Console.WriteLine($"Generováno: {log.EventType} | IP: {log.SourceIp}");

            try
            {
                string url = "http://ingestion-service/api/ingest"; 
                await HttpClient.PostAsJsonAsync(url, log);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chyba odeslání (Ingestion Service nedostupná?): {ex.Message}");
            }
        }
    }
}