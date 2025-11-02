using System;
using System.Threading.Tasks;

namespace XProxyV1
{
    public class CommandLineInterface
    {
        private readonly ProxyServer _proxy;

        public CommandLineInterface(ProxyServer proxy)
        {
            _proxy = proxy;
        }

        public async Task StartAsync()
        {
            Console.WriteLine("XProxyV1");
            Console.WriteLine("============================================\n");
            Console.WriteLine("\nAvailable commands:");
            Console.WriteLine("  blacklistrl  - Reload blacklist.json");
            Console.WriteLine("  redirectsrl  - Reload redirects.json");
            Console.WriteLine("  help         - Show this help");
            Console.WriteLine("  exit/quit    - Stop proxy and exit\n");
            Console.WriteLine("============================================\n");

            while (true)
            {
                Console.Write("XProxyV1> ");
                var command = Console.ReadLine()?.Trim().ToLower();

                if (string.IsNullOrEmpty(command))
                    continue;

                switch (command)
                {
                    case "blacklistrl":
                        await _proxy.ReloadBlacklistAsync();
                        break;

                    case "redirectsrl":
                        await _proxy.ReloadRedirectsAsync();
                        break;

                    case "exit":
                    case "quit":
                        Console.WriteLine("Exiting XProxy...");
                        _proxy.Stop();
                        Environment.Exit(0);
                        break;

                    case "help":
                        Console.WriteLine("============================================\n");
                        Console.WriteLine("\nAvailable commands:");
                        Console.WriteLine("  blacklistrl  - Reload blacklist.json");
                        Console.WriteLine("  redirectsrl  - Reload redirects.json");
                        Console.WriteLine("  help         - Show this help");
                        Console.WriteLine("  exit/quit    - Stop proxy and exit\n");
                        Console.WriteLine("============================================\n");
                        break;

                    default:
                        Console.WriteLine("Unknown Command. for Help type 'help'");
                        break;
                }
            }
        }
    }
}