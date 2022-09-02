using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

#if NETSTANDARD1_3 || NETCOREAPP1_0
using Trace = System.Diagnostics.Debug;
#endif

namespace POP3Mgr;

public static class POP3Mgr
{
    public static NetworkStream OpenTcpIpStream(string host, int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        
        try
        {
            socket.Connect(host, port);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception ex)
        {
            socket.Close();
            throw new Exception("Connection failed", ex);
        }
    }

    public static SslStream OpenSslStream(Stream inner, string hostName, bool ignoreCertErrors)
    {
        Console.WriteLine("Encrypting connection...");

        SslStream ssl;
        if (ignoreCertErrors)
        {
            ssl = new SslStream(inner,
                                  leaveInnerStreamOpen: false,
                                  userCertificateValidationCallback: (p1, p2, p3, p4) => true);
        }
        else
        {
            ssl = new SslStream(inner,
                                  leaveInnerStreamOpen: false);
        }

        ssl.AuthenticateAsClient(hostName);

        return ssl;
    }

    public static bool? GetConsoleYNInput()
    {
        switch (GetConsoleInputKey(new[] { 'y', 'n', 'Y', 'N' }))
        {
            case 'y': case 'Y':
                return true;

            case 'n': case 'N':
                return false;

            default:
                return null;
        }
    }

    public static char? GetConsoleInputKey(char[] validkeys)
    {
        for (; ; )
        {
            var key = Console.ReadKey(intercept: true);
            if (Array.IndexOf(validkeys, key.KeyChar) >= 0)
            {
                Console.WriteLine(key.KeyChar);
                return key.KeyChar;
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return null;
            }
        }
    }

    public static string GetConsolePasswordInput()
    {
        var pwd = new StringBuilder();
        for (;;)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return pwd.ToString();

                case ConsoleKey.Backspace:
                    if (pwd.Length < 1)
                    {
                        continue;
                    }

                    Console.Write("\b \b");
                    pwd.Length -= 1;
                    break;

                default:
                    Console.Write('*');
                    pwd.Append(key.KeyChar);
                    break;
            }
        }
    }

    public static void WriteExceptionMessages(this TextWriter writer, Exception ex)
    {
        while (ex != null)
        {
            if (ex is TargetInvocationException)
            {
            }
            else if (ex is ReflectionTypeLoadException rtlex)
            {
                foreach (var ldex in rtlex.LoaderExceptions)
                {
                    writer.WriteExceptionMessages(ldex);
                }

                writer.WriteLine(ex.Message);
            }
            else
            {
                writer.WriteLine(ex.Message);
            }

            ex = ex.InnerException;
        }
    }

    public static int Main(params string[] args)
    {
        Console.WriteLine("POP3MGR, by Olof Lagerkvist.");
        Console.WriteLine("Copyright (c) 2013-2021");
        Console.WriteLine();
        Console.WriteLine("http://www.ltr-data.se");

        try
        {
            var asm = typeof(POP3Mgr).GetTypeInfo().Assembly.GetName();
            Console.WriteLine($"{asm.Name} ver {asm.Version.Major}.{asm.Version.Minor:000}");
            Console.WriteLine();
        }
        catch
        {
        }

        var encoding = Encoding.GetEncoding(0);

        string server = null;
        int? port = null;
        var tls = false;
        var tlsifsupported = false;
        var ssl = false;
        var unsafessl = false;
        string username = null;
        string password = null;
        var apop = false;
        
        foreach (var arg in args)
        {
            if (server == null && arg.StartsWith("/SERVER=", StringComparison.OrdinalIgnoreCase))
            {
                server = arg.Substring("/SERVER=".Length);
            }
            else if (port == null && arg.StartsWith("/PORT=", StringComparison.OrdinalIgnoreCase))
            {
                port = int.Parse(arg.Substring("/PORT=".Length));
            }
            else if (username == null && arg.StartsWith("/USER=", StringComparison.OrdinalIgnoreCase))
            {
                username = arg.Substring("/USER=".Length);
            }
            else if (username != null && password == null && arg.StartsWith("/PASSWORD=", StringComparison.OrdinalIgnoreCase))
            {
                password = arg.Substring("/PASSWORD=".Length);
            }
            else if (arg.Equals("/APOP", StringComparison.OrdinalIgnoreCase))
            {
                apop = true;
            }
            else if (arg.Equals("/TLS", StringComparison.OrdinalIgnoreCase))
            {
                tls = true;
            }
            else if (arg.Equals("/SSL", StringComparison.OrdinalIgnoreCase))
            {
                ssl = true;
            }
            else if ((ssl || tls) && arg.Equals("/UNSAFE", StringComparison.OrdinalIgnoreCase))
            {
                unsafessl = true;
            }
            else if (tls && arg.Equals("/IFSUPPORTED", StringComparison.OrdinalIgnoreCase))
            {
                tlsifsupported = true;
            }
            else
            {
                Console.WriteLine("Parameter '" + arg + "' not valid.");
                Console.WriteLine();
                Console.WriteLine
                    ("Utility to manually interact with POP3 server." + Environment.NewLine +
                    Environment.NewLine +
                    "Usage:" + Environment.NewLine +
                    "pop3mgr [/SERVER=server] [/PORT=portnumber] /USER=username /PASSWORD=password" + Environment.NewLine +
                    "    [/APOP] [/TLS [/IFSUPPORTED] [/UNSAFE] | /SSL [/UNSAFE]]");

                return 1;
            }
        }

        if (server == null)
        {
            Console.Write("Server name: ");
            server = Console.ReadLine();
            if (String.IsNullOrEmpty(server))
            {
                return -1;
            }

            if (!(ssl | tls | apop))
            {
                Console.Write("Encrypt connection using SSL tunneling (y/n): ");
                var q = GetConsoleYNInput();
                if (q == null)
                {
                    return -1;
                }

                ssl = q.Value;
            }
        }

        if (port == null)
        {
            if (ssl)
            {
                port = 995;
            }
            else
            {
                port = 110;
            }

            Console.Write($"Connect to port number (default={port}): ");
            
            if (int.TryParse(Console.ReadLine(), out var pnr))
            {
                port = pnr;
            }
        }

        Stream pop = null;

        try
        {
            Console.WriteLine($"Connecting to POP3 server {server} port {port}...");

            pop = OpenTcpIpStream(server, port.Value);

            Console.WriteLine("Connected to POP server.");

            if (ssl)
            {
                pop = OpenSslStream(pop, server, unsafessl);
                Console.WriteLine("Connected SSL channel.");
            }

            var receive = new StreamReader(pop, encoding);

            List<string[]> GetResponse()
            {
                var messages = new List<string[]>(1);
                for (; ; )
                {
                    var recvline = receive.ReadLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(recvline);
                    Console.ResetColor();
                    if (recvline == null)
                    {
                        throw new ProtocolViolationException("Unexpected disconnection");
                    }

                    var fields = recvline.Split(' ');
                    messages.Add(fields);
                    if (fields[0].Equals("+OK", StringComparison.OrdinalIgnoreCase) ||
                        fields[0].Equals("-ERR", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                return messages;
            }

            var send = new StreamWriter(pop, encoding);

            List<string[]> SendCommand(params string[] command)
            {
                var s = string.Join(" ", command);
                Console.ForegroundColor = ConsoleColor.Cyan;
                if (command[0].Equals("PASS", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("(password)");
                }
                else
                {
                    Console.WriteLine(s);
                }

                Console.ResetColor();

                send.WriteLine(s);
                send.Flush();
                return GetResponse();
            }

            var response = GetResponse();
            var banner = response[response.Count - 1];
            if (!banner[0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Unknown response from server.");
                return 1;
            }

            var apopChallenge =
                Array.Find(banner, (f) =>
                    f.StartsWith("<", StringComparison.Ordinal) &&
                    f.EndsWith(">", StringComparison.Ordinal));

            if (apop && (apopChallenge == null))
            {
                Console.Error.WriteLine("APOP requested, but server provides no challenge.");
                SendCommand("QUIT");
                return 1;
            }

            var capabilities = new List<string>();
            response = SendCommand("CAPA");
            if (response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
            {
                for (; ; )
                {
                    var line = receive.ReadLine();

                    if (string.IsNullOrEmpty(line))
                    {
                        throw new ProtocolViolationException("Unexpected end of list.");
                    }

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(line);
                    Console.ResetColor();

                    if (line.Equals(".", StringComparison.Ordinal))
                    {
                        break;
                    }

                    capabilities.Add(line);
                }
            }

            if (!(ssl|tls))
            {
                Console.Write("Try encrypt connection using TLS? (y/n): ");
                var q = GetConsoleYNInput();
                if (q == null)
                {
                    SendCommand("QUIT");
                    return -1;
                }

                tls = q.Value;
            }

            if (tls)
            {
                response = SendCommand("STLS");
                if (response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                {
                    pop = OpenSslStream(pop, server, unsafessl);
                    receive = new StreamReader(pop, encoding);
                    send = new StreamWriter(pop, encoding);
                    Console.WriteLine("Added TLS layer.");
                }
                else
                {
                    Console.WriteLine("Server does not support TLS.");
                    if (!tlsifsupported)
                    {
                        Console.Write("Continue with clear text communication? (y/n): ");
                        if (!GetConsoleYNInput().GetValueOrDefault(false))
                        {
                            SendCommand("QUIT");
                            return 1;
                        }
                    }
                }
            }

            if ((apopChallenge == null) &&
                !(tls | ssl))
            {
                Console.WriteLine("Connection not encrypted and password hashing not supported by server.");
                Console.Write("Continue sending clear text user name and password? (y/n): ");
                if (!GetConsoleYNInput().GetValueOrDefault(false))
                {
                    SendCommand("QUIT");
                    return -1;
                }
            }
                
            if (username == null)
            {
                Console.Write("User name: ");
                username = Console.ReadLine();
            }

            if (password == null)
            {
                Console.Write("Password: ");
                password = GetConsolePasswordInput();
            }

            if (apopChallenge != null)
            {
                apopChallenge += password;

                string apopResponse;
                using (var md5 = MD5.Create())
                {
                    apopResponse = BitConverter.ToString(md5.ComputeHash(encoding.GetBytes(apopChallenge))).Replace("-", null).ToLowerInvariant();
                }

                response = SendCommand("APOP", username, apopResponse);
                if (!response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("APOP authentication failed.");
                    {
                        SendCommand("QUIT");
                        return 1;
                    }
                }
            }
            else
            {
                response = SendCommand("USER", username);
                if (!response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("User name not accepted.");
                    {
                        SendCommand("QUIT");
                        return 1;
                    }
                }

                response = SendCommand("PASS", password);
                if (!response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Password not accepted.");
                    {
                        SendCommand("QUIT");
                        return 1;
                    }
                }
            }

            response = SendCommand("LIST");
            if (!response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Server does not support LIST command.");
                {
                    SendCommand("QUIT");
                    return 1;
                }
            }

            long numMail = 0;
            long totalBytes = 0;
            for (; ; )
            {
                var line = receive.ReadLine();

                if (string.IsNullOrEmpty(line))
                {
                    throw new ProtocolViolationException("Unexpected end of list.");
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(line);
                Console.ResetColor();

                if (line.Equals(".", StringComparison.Ordinal))
                {
                    break;
                }

                var fields = line.Split(' ');
                if (fields.Length >= 1)
                {
                    totalBytes += long.Parse(fields[1]);
                }

                numMail++;
            }

            if (numMail == 0)
            {
                Console.WriteLine("No mail.");
                {
                    SendCommand("QUIT");
                    return 0;
                }
            }

            Console.WriteLine("Found {0} mail, {1} bytes total.", numMail, totalBytes);

            void cmdThreadFnc()
            {
                try
                {
                    for (; ; )
                    {
                        var line = receive.ReadLine();

                        if (line == null)
                        {
                            return;
                        }

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(line);
                        Console.ResetColor();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                    Console.Error.WriteExceptionMessages(ex);
                }
            }

            var cmdThread = new Thread(cmdThreadFnc);
            cmdThread.Start();

            Console.WriteLine("Command examples:");
            Console.WriteLine("See which commands this server implements:               CAPA");
            Console.WriteLine("Remove e-mail number 2:                                  DELE 2");
            Console.WriteLine("See headers for e-mail number 2:                         TOP 2 0");
            Console.WriteLine("See headers and 10 first body lines for e-mail number 2: TOP 2 10");
            Console.WriteLine("Retrieve complete e-mail number 2:                       RETR 2");
            Console.WriteLine("Undo all retrieval or delete operations:                 RSET");
            Console.WriteLine("Disconnect:                                              QUIT");

            for (; ; )
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                var cmd = Console.ReadLine();
                Console.ResetColor();
                if (cmd == null)
                {
                    send.Dispose();
                    break;
                }

                send.WriteLine(cmd);
                send.Flush();

                if (cmd.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    cmdThread.Join();
                    break;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex.ToString());
            Console.Error.WriteExceptionMessages(ex);
            return -1;
        }
        finally
        {
            pop?.Dispose();
        }
    }
}
