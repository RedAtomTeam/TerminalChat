using System;
using System.CommandLine;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;


class Program()
{
    static async Task Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            Console.ResetColor(); 
            Environment.Exit(0); 
        };

        var modeOption = new Option<string>(
            new[] { "-m", "--mode" },
            description: "Mode (server/client)",
            getDefaultValue: () => "server");
        var ipOption = new Option<string>(
            new[] { "-i", "--ip" },
            description: "IP address for client mode");
        var portOption = new Option<int>(
            new[] { "-p", "--port" },
            description: "Local port number (default: 8888)",
            getDefaultValue: () => 8888);
        var passwordOption = new Option<string>(
            new[] { "-P", "--Password" },
            description: "Password for run server");

        var rootCommand = new RootCommand("TerminalMessager");
        rootCommand.AddOption(modeOption);
        rootCommand.AddOption(ipOption);
        rootCommand.AddOption(portOption);
        rootCommand.AddOption(passwordOption);

        rootCommand.SetHandler(async (mode, ip, port, password) =>
        {
            try
            {
                Console.WriteLine("Launching application...");
                await RunApplication(mode, ip, port, password);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, modeOption, ipOption, portOption, passwordOption);

        await rootCommand.InvokeAsync(args);
    }

    static async Task RunApplication(string mode, string ip, int port, string password)
    {
        if (port < 1024 || port > 65535)
            throw new ArgumentException("Ports must be between 1024 and 65535.");

        if (mode.ToLower() == "server")
            await StartServerAsync(port, password);

        if (mode.ToLower() == "client")
        {
            if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out _))
                throw new ArgumentException("Valid IP address is required in server mode");
            await SendMessageAsync(ip, port, password);
        }
        else if(mode != "server" && mode != "client")
            throw new ArgumentException("Invalid mode. Use 'server' or 'client'");
    }

    static async Task StartServerAsync(int port, string password)
    {
        Console.WriteLine($"The server is running at: {port}, with password: '{password}'");
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        using (var client = await listener.AcceptTcpClientAsync())
        using (var stream = client.GetStream())
        {
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer);

            if (Encoding.UTF8.GetString(buffer, 0, bytesRead) != password)
                Console.WriteLine("Incoming connection refused - invalid password");

            Console.WriteLine("Incoming connection accepted:");
            Console.ForegroundColor = ConsoleColor.Yellow;

            var streamTask = StartChatSession(stream, password);
            await streamTask;
            return;
        }
    }

    static async Task SendMessageAsync(string ip, int port, string password)
    {
        Console.WriteLine($"Trying to connect to {ip}:{port} with password: '{password}'");

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(ip, port);
            using (var stream = client.GetStream())
            {
                stream.WriteAsync(Encoding.UTF8.GetBytes(password));

                if(!IsConnectionActive(client))
                {
                    Console.WriteLine("Connection refused - invalid password");
                    return;
                }

                Console.WriteLine("Connection successful:");
                Console.ForegroundColor = ConsoleColor.Yellow;

                var streamTask = StartChatSession(stream, password);
                await streamTask;
                return;
            }
        }
    }

    static public async Task StartChatSession(NetworkStream stream, string password)
    {
        var receiveTask = Task.Run(async () =>
        {
            var buffer = new byte[1024];
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer);
                if (bytesRead == 0) 
                    break;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"- {AddXOR(Encoding.UTF8.GetString(buffer, 0, bytesRead), password)}");
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
        });

        var sendTask = Task.Run(async () =>
        {
            while (true)
            {
                var message = Console.ReadLine();
                if (string.IsNullOrEmpty(message)) 
                    continue;

                stream.WriteAsync(Encoding.UTF8.GetBytes(AddXOR(message, password)));
            }
        });

        Task.WaitAny(receiveTask, sendTask);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Connection interrupted");
        Console.ResetColor();
    }

    static bool IsConnectionActive(TcpClient client)
    {
        if (client == null) 
            return false;
        try
        {
            return !(client.Client.Poll(1000, SelectMode.SelectRead)) && client.Client.Available == 0 && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    static string AddXOR(string text, string key)
    {
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char keyChar = key[i % key.Length];
            char encryptedChar = (char)(text[i] ^ keyChar);
            result.Append((encryptedChar).ToString());

        }
        return result.ToString(); 
    }

    static string GetLocalIpAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        return host.AddressList
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?
            .ToString() ?? "127.0.0.1";
    }
}


