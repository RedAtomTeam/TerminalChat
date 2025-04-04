using System;
using System.CommandLine;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using TerminalChat;

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

    private static async Task RunApplication(string mode, string ip, int port, string password)
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

    private static async Task StartServerAsync(int port, string password)
    {
        string ip = GetLocalIpAddress();
        Console.WriteLine($"The server is running at: {ip}:{port}, with password: '{password}'");
        var listener = new TcpListener(IPAddress.Parse(ip), port);
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

    private static async Task SendMessageAsync(string ip, int port, string password)
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

    private static async Task StartChatSession(NetworkStream stream, string password)
    {
        byte[] key = DeriveKeyFromPassword(password);

        var receiveTask = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            while (true)
            {
                await ReadExactly(stream, buffer, 0, 4);
                int nonceLen = BitConverter.ToInt32(buffer, 0);

                await ReadExactly(stream, buffer, 0, nonceLen);
                byte[] nonce = buffer[0..nonceLen];

                await ReadExactly(stream, buffer, 0, 4);
                int tagLen = BitConverter.ToInt32(buffer, 0);

                await ReadExactly(stream, buffer, 0, tagLen);
                byte[] tag = buffer[0..tagLen];

                await ReadExactly(stream, buffer, 0, 4);
                int cipherLen = BitConverter.ToInt32(buffer, 0);

                await ReadExactly(stream, buffer, 0, cipherLen);
                byte[] ciphertext = buffer[0..cipherLen];

                // Дешифруем
                string message = AesGcmHelper.Decrypt(ciphertext, nonce, tag, key);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"- {message}");
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
        });

        var sendTask = Task.Run(async () =>
        {
            while (true)
            {
                string message = Console.ReadLine();
                if (string.IsNullOrEmpty(message)) 
                    continue;

                var (ciphertext, nonce, tag) = AesGcmHelper.Encrypt(message, key);

                await stream.WriteAsync(BitConverter.GetBytes(nonce.Length));
                await stream.WriteAsync(nonce);
                await stream.WriteAsync(BitConverter.GetBytes(tag.Length));
                await stream.WriteAsync(tag);
                await stream.WriteAsync(BitConverter.GetBytes(ciphertext.Length));
                await stream.WriteAsync(ciphertext);
            }
        });

        Task.WaitAny(receiveTask, sendTask);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Connection interrupted");
        Console.ResetColor();
    }

    private static bool IsConnectionActive(TcpClient client)
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

    private static byte[] DeriveKeyFromPassword(string password)
    {
        byte[] salt = Encoding.UTF8.GetBytes("FixedChatSalt123");

        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256);

        return pbkdf2.GetBytes(32); 
    }

    private static async Task ReadExactly(NetworkStream stream, byte[] buffer, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int bytes = await stream.ReadAsync(buffer, offset + read, count - read);
            if (bytes == 0) throw new EndOfStreamException();
            read += bytes;
        }
    }

    private static string AddXOR(string text, string key)
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

    private static string GetLocalIpAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        return host.AddressList
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?
            .ToString() ?? "127.0.0.1";
    }
}


