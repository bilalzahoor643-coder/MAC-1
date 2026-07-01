using System;
using System.Threading;
using MAC_1.Service.Core;

namespace MAC_1.Service
{
    class Program
    {
        private static ServiceHost? _serviceHost;
        private static ManualResetEvent _shutdownEvent = new(false);

        static void Main(string[] args)
        {
            Console.Title = "MAC-1 Background Service";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════╗");
            Console.WriteLine("║       MAC-1 Background Service          ║");
            Console.WriteLine("║   Lightweight Download Event Router     ║");
            Console.WriteLine("╚══════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            _serviceHost = new ServiceHost();
            _serviceHost.Start();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[READY] Service is running. Press Ctrl+C to stop.");
            Console.ResetColor();
            Console.WriteLine();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _shutdownEvent.Set();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                _serviceHost?.Stop();
            };

            _shutdownEvent.WaitOne();

            _serviceHost?.Stop();
            Console.WriteLine("[STOPPED] Service has been shut down.");
        }
    }
}
