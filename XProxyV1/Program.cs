using System;
using System.Threading.Tasks;

namespace XProxyV1
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "XProxy";
            var proxy = new ProxyServer(8080);
            var cli = new CommandLineInterface(proxy);

            var proxyTask = proxy.StartAsync();
            var cliTask = cli.StartAsync();

            await Task.WhenAny(proxyTask, cliTask);
        }
    }
}