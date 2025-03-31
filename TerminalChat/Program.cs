using System.CommandLine;
using System.Net;


class Program()
{
    static async Task Main(string[] args)
    {
        var modeOption = new Option<string>(
            new[] { "-m", "--mode" },
            description: "Mode (server/client)",
            getDefaultValue: () => "server");
        var ipOption = new Option<string>(
            new[] { "-i", "--ip" },
            description: "IP address for client mode");
        var portOption = new Option<int>(
            new[] { "-p", "--port" },
            description: "Port number (default: 8888)",
            getDefaultValue: () => 8888);

        var rootCommand = new RootCommand("TerminalMessager");
        rootCommand.AddOption(modeOption);
        rootCommand.AddOption(ipOption);
        rootCommand.AddOption(portOption);

        rootCommand.SetHandler((mode, ip, port) =>
        {
            try
            {
                RunApplication(mode, ip, port);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, modeOption, ipOption, portOption);

        await rootCommand.InvokeAsync(args);
    }

    static void RunApplication(string mode, string ip, int port)
    {
        if (port < 1024 || port > 65535) throw new ArgumentException("Port must be between 1024 and 65535.");

        if (mode.ToLower() == "server")
        {
            if (string.IsNullOrEmpty(ip))
            {
                Console.WriteLine("server is running");
            }
            else throw new ArgumentException("Server mode does not require IP address.");

        }
        else
        {
            if (mode.ToLower() == "client")
            {
                IPAddress ipAddress;
                if (!string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out ipAddress))
                {
                    Console.WriteLine($"Connecting to {ip}:{port}");
                }
                else throw new ArgumentException("Invalid IP address.");
            }
            else throw new ArgumentException("Invalid mode. Use 'server' or 'client'.");
        }
    }
}