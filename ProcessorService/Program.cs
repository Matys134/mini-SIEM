using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ProcessorService
{
    // Stejný model (v reálu by byl ve sdílené knihovně, tady ho kopírujeme)
    public class LogEntry
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string ServiceName { get; set; }
        public string LogLevel { get; set; }
        public string EventType { get; set; }
        public string SourceIp { get; set; }
        public string Message { get; set; }
    }

    class Program
    {
        private const string QueueName = "siem-logs-queue";

        static void Main(string[] args)
        {
            Console.Title = "SIEM Stream Processor";
            Console.WriteLine("--- SIEM Stream Processor Started ---");
            Console.WriteLine("Waiting for logs from RabbitMQ...");

            var factory = new ConnectionFactory() { HostName = "localhost" };

            try
            {
                using (var connection = factory.CreateConnection())
                using (var channel = connection.CreateModel())
                {
                    // Ujistíme se, že fronta existuje (idempotentní operace)
                    channel.QueueDeclare(queue: QueueName,
                                         durable: false,
                                         exclusive: false,
                                         autoDelete: false,
                                         arguments: null);

                    // Vytvoříme konzumenta (posluchače)
                    var consumer = new EventingBasicConsumer(channel);

                    // Tady definujeme, co se stane, když přijde zpráva
                    consumer.Received += (model, ea) =>
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);

                        try 
                        {
                            var log = JsonSerializer.Deserialize<LogEntry>(message);
                            ProcessLog(log);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Error] Failed to parse log: {ex.Message}");
                        }
                    };

                    // Spustíme konzumaci
                    channel.BasicConsume(queue: QueueName,
                                         autoAck: true, // Automaticky potvrdit, že jsme zprávu přečetli
                                         consumer: consumer);

                    // Aby se aplikace hned neukončila
                    Console.WriteLine("Press [enter] to exit.");
                    Console.ReadLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL ERROR: Is RabbitMQ running? {ex.Message}");
            }
        }

        // --- ZDE JE TVOJE BUSINESS LOGIKA (RTAP) ---
        private static void ProcessLog(LogEntry log)
        {
            // 1. Jednoduchý výpis (DEBUG)
            // Console.WriteLine($"Received: {log.Timestamp} | {log.ServiceName} | {log.LogLevel}");

            // 2. DETEKCE HROZEB (Simulace RTAP)
            
            // Pravidlo A: Kritická chyba
            if (log.LogLevel == "CRITICAL" || log.LogLevel == "FATAL")
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"[ALERT] CRITICAL ERROR DETECTED on {log.ServiceName}: {log.Message}");
                Console.ResetColor();
            }

            // Pravidlo B: Detekce útoku (z našeho Generátoru)
            // Generátor posílá při útoku EventType = "login_failed" a LogLevel = "WARN"
            else if (log.EventType == "login_failed" && log.LogLevel == "WARN")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[SECURITY ALERT] Suspicious login attempt from IP: {log.SourceIp}");
                // TODO: Tady by v budoucnu bylo počítadlo v Redisu (Rate Limiting)
                Console.ResetColor();
            }
            else
            {
                // Běžný traffic - jen to problikne šedě
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"[OK] {log.ServiceName}: {log.EventType}");
                Console.ResetColor();
            }
        }
    }
}