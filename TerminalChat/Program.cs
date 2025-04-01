using System.CommandLine;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;


class Program()
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("-1");
        var modeOption = new Option<string>(
            new[] { "-m", "--mode" },
            description: "Mode (server/client)",
            getDefaultValue: () => "server");
        var ipOption = new Option<string>(
            new[] { "-i", "--ip" },
            description: "IP address for client mode");
        var localPortOption = new Option<int>(
            new[] { "-lp", "--local_port" },
            description: "Local port number (default: 8888)",
            getDefaultValue: () => 8888);
        var remotePortOption = new Option<int>(
            new[] { "-rp", "--remote_port" },
            description: "Remote port number (default: 8888)",
            getDefaultValue: () => 8888);
        var localPasswordOption = new Option<string>(
            new[] { "-lP", "--localPassword" },
            description: "Password for run server");
        var remotePasswordOption = new Option<string>(
            new[] { "-rP", "--remotePassword" },
            description: "Password for connect to server");

        var rootCommand = new RootCommand("TerminalMessager");
        rootCommand.AddOption(modeOption);
        rootCommand.AddOption(ipOption);
        rootCommand.AddOption(localPortOption);
        rootCommand.AddOption(remotePortOption);
        rootCommand.AddOption(localPasswordOption);
        rootCommand.AddOption(remotePasswordOption);

        rootCommand.SetHandler(async (mode, ip, localPort, remotePort, localPassword, remotePassword) =>
        {
            try
            {
                await RunApplication(mode, ip, localPort, remotePort, localPassword, remotePassword);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }, modeOption, ipOption, localPortOption, remotePortOption, localPasswordOption, remotePasswordOption);

        await rootCommand.InvokeAsync(args);
    }

    static async Task RunApplication(string mode, string ip, int localPort, int remotePort, string localPassword, string remotePassword)
    {
        if ((localPort < 1024 || localPort > 65535) || (mode == "client" && (remotePort < 1024 || remotePort > 65535)))
            throw new ArgumentException("Ports must be between 1024 and 65535.");


        if (mode.ToLower() == "server")
        {
            await StartServerAsync(localPort, localPassword);
        }
        else
        {
            if (mode.ToLower() == "client")
            {
                IPAddress ipAddress;
                if (!string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out ipAddress))
                {
                    await StartServerAsync(localPort, localPassword, false);
                    await ConnectToPeerAsync(ip, GetLocalIpAddress(), remotePort, localPort, remotePassword, localPassword);
                }
                else throw new ArgumentException("Invalid IP address.");
            }
            else throw new ArgumentException("Invalid mode. Use 'server' or 'client'.");
        }
    }

    static async Task StartServerAsync(int port, string password, bool isRecep = true)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        IPEndPoint localEndPoint = (IPEndPoint)listener.Server.LocalEndPoint!;
        string myIp = localEndPoint.Address.ToString();

        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();


            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer);
            var receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead).Split('|');

            if (receivedData.Length != 4 || receivedData[0] != password)
            {
                Console.WriteLine("Invalid connection attempt rejected");
                continue;
            }

            var peerPassword = receivedData[1];
            var peerIp = receivedData[2];
            var peerPort = int.Parse(receivedData[3]);

            Console.WriteLine($"Peer connected from {peerIp}:{peerPort}");
            if (isRecep)
                await ConnectToPeerAsync(peerIp, myIp, peerPort, port, peerPassword, password);

        }
    }

    static async Task ConnectToPeerAsync(string ip, string myIp, int port, int myPort, string password, string myPassword)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ip, port);
            using var stream = client.GetStream();

            var connectionData = $"{password}|{myPassword}|{myIp}|{myPort}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(connectionData));

            Console.WriteLine($"Successfully connected to {ip}:{port}");
            await StartChatSession(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to peer: {ex.Message}");
        }
    }

    static async Task StartChatSession(NetworkStream stream)
    {
        var receiveTask = Task.Run(async () =>
        {
            var buffer = new byte[1024];
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer);
                if (bytesRead == 0) break;
                Console.WriteLine($"\n[Peer]: {Encoding.UTF8.GetString(buffer, 0, bytesRead)}");
            }
        });

        while (true)
        {
            Console.Write("You: ");
            var message = Console.ReadLine();
            if (string.IsNullOrEmpty(message)) continue;

            await stream.WriteAsync(Encoding.UTF8.GetBytes(message));
        }
    }

    static string GetLocalIpAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        return host.AddressList
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?
            .ToString() ?? "127.0.0.1";
    }
}


