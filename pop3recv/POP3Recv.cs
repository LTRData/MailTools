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

#if NETSTANDARD1_3 || NETCOREAPP1_0
using Trace = System.Diagnostics.Debug;
#endif

#if NETSTANDARD
namespace System.Net
{
    public class ProtocolViolationException : InvalidOperationException
    {
        public ProtocolViolationException() { }
        public ProtocolViolationException(string message) : base(message) { }
    }
}
#endif

#if NET20
namespace System.Runtime.CompilerServices
{
    public sealed class ExtensionAttribute : Attribute
    {
    }
}
#endif

namespace POP3Recv
{
    public static class POP3Recv
    {
        public static NetworkStream OpenTcpIpStream(string host, int port)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

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

#if NETSTANDARD || NETCOREAPP

        public static void Close(this Socket socket) => socket.Dispose();

        public static void AuthenticateAsClient(this SslStream ssl, string targetHost) => ssl.AuthenticateAsClientAsync(targetHost).Wait();

#endif

#if NETFRAMEWORK

        public static Type GetTypeInfo(this Type t) => t;

#endif

        public static SslStream OpenSslStream(Stream inner, string hostName, bool ignoreCertErrors)
        {
            Console.WriteLine("Encrypting connection...");

            SslStream ssl;
            if (ignoreCertErrors)
                ssl = new SslStream(inner,
                                      leaveInnerStreamOpen: false,
                                      userCertificateValidationCallback: (p1, p2, p3, p4) => true);
            else
                ssl = new SslStream(inner,
                                      leaveInnerStreamOpen: false);

            ssl.AuthenticateAsClient(hostName);

            return ssl;
        }

        public static int Main(string[] args)
        {
            Console.WriteLine("POP3RECV, by Olof Lagerkvist.");
            Console.WriteLine("Copyright (c) 2013-2021");
            Console.WriteLine();
            Console.WriteLine("http://www.ltr-data.se");

            try
            {
                var asm = typeof(POP3Recv).GetTypeInfo().Assembly.GetName();
                Console.WriteLine(asm.Name + " ver " + asm.Version.Major.ToString() + "." + asm.Version.Minor.ToString("000"));
                Console.WriteLine();
            }
            catch
            {
            }

            var encoding = Encoding.GetEncoding(1252);

            string server = null;
            int? port = null;
            bool tls = false;
            bool tlsifsupported = false;
            bool ssl = false;
            bool unsafessl = false;
            bool keeponserver = false;
            string username = null;
            string password = null;
            bool apop = false;

            foreach (var arg in args)
                if (server == null && arg.StartsWith("/SERVER=", StringComparison.OrdinalIgnoreCase))
                    server = arg.Substring("/SERVER=".Length);
                else if (port == null && arg.StartsWith("/PORT=", StringComparison.OrdinalIgnoreCase))
                    port = int.Parse(arg.Substring("/PORT=".Length));
                else if (username == null && arg.StartsWith("/USER=", StringComparison.OrdinalIgnoreCase))
                    username = arg.Substring("/USER=".Length);
                else if (username != null && password == null && arg.StartsWith("/PASSWORD=", StringComparison.OrdinalIgnoreCase))
                    password = arg.Substring("/PASSWORD=".Length);
                else if (arg.Equals("/APOP", StringComparison.OrdinalIgnoreCase))
                    apop = true;
                else if (arg.Equals("/TLS", StringComparison.OrdinalIgnoreCase))
                    tls = true;
                else if (arg.Equals("/SSL", StringComparison.OrdinalIgnoreCase))
                    ssl = true;
                else if ((ssl || tls) && arg.Equals("/UNSAFE", StringComparison.OrdinalIgnoreCase))
                    unsafessl = true;
                else if (tls && arg.Equals("/IFSUPPORTED", StringComparison.OrdinalIgnoreCase))
                    tlsifsupported = true;
                else if (arg.Equals("/KEEP", StringComparison.OrdinalIgnoreCase))
                    keeponserver = true;
                else
                {
                    Console.WriteLine("Parameter '" + arg + "' not valid.");
                    Console.WriteLine();
                    Console.WriteLine
                        ("Utility to fetch e-mails from a POP3 server." + Environment.NewLine +
                        Environment.NewLine +
                        "Usage:" + Environment.NewLine +
                        "pop3recv /SERVER=server [/PORT=portnumber] /USER=username /PASSWORD=password" + Environment.NewLine +
                        "    [/APOP] [/TLS [/IFSUPPORTED] [/UNSAFE] | /SSL [/UNSAFE]] [/KEEP]");

                    return 1;
                }

            if (server == null)
            {
                Console.Error.WriteLine("Needs pop3 server name.");
                return -1;
            }

            if (port == null)
                if (ssl)
                    port = 995;
                else
                    port = 110;

            Stream pop = null;

            try
            {
                Console.WriteLine("Connecting to POP3 server {0}...", server);
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
                        Console.WriteLine("< " + recvline);
                        if (recvline == null)
                            break;

                        var fields = recvline.Split(' ');
                        messages.Add(fields);
                        if (fields[0].Equals("+OK", StringComparison.OrdinalIgnoreCase) ||
                            fields[0].Equals("-ERR", StringComparison.OrdinalIgnoreCase))
                            break;
                    }

                    return messages;
                };

                var send = new StreamWriter(pop, encoding);

                List<string[]> SendCommand(params string[] command)
                {
                    var s = string.Join(" ", command);
                    if (command[0].Equals("PASS", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine("> (password)");
                    else
                        Console.WriteLine("> " + s);

                    send.WriteLine(s);
                    send.Flush();
                    return GetResponse();
                };

                var response = GetResponse();
                var banner = response[response.Count - 1];
                if (!banner[0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Unknown response from server.");
                    return 1;
                }

                string apopChallenge =
                    Array.Find(banner, (f) =>
                        f.StartsWith("<", StringComparison.Ordinal) &&
                        f.EndsWith(">", StringComparison.Ordinal));

                if (apop && (apopChallenge == null))
                {
                    Console.Error.WriteLine("APOP requested, but server provides no challenge.");
                    SendCommand("QUIT");
                    return 1;
                }

                if (tls)
                {
                    response = SendCommand("STLS");
                    if (!response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine("Server does not support STLS.");

                        if (!tlsifsupported)
                        {
                            SendCommand("QUIT");
                            return 1;
                        }
                    }

                    pop = OpenSslStream(pop, server, unsafessl);
                    receive = new StreamReader(pop, encoding);
                    send = new StreamWriter(pop, encoding);
                    Console.WriteLine("Added TLS layer.");
                }

                if (apop)
                {
                    apopChallenge += password;

                    string apopResponse;
                    using (var md5 = MD5.Create())
                        apopResponse = BitConverter.ToString(md5.ComputeHash(encoding.GetBytes(apopChallenge))).Replace("-", null).ToLowerInvariant();

                    response = SendCommand("APOP", username, apopResponse);
                    if (!response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine("APOP authenication failed.");
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

                response = SendCommand("STAT");
                var statResponse = response[response.Count - 1];
                if (!statResponse[0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Server error.");
                    {
                        SendCommand("QUIT");
                        return 1;
                    }
                }

                var numMail = long.Parse(statResponse[1]);
                if (numMail == 0)
                {
                    Console.WriteLine("No mail.");
                    {
                        SendCommand("QUIT");
                        return 0;
                    }
                }

                var totalBytes = long.Parse(statResponse[2]);
                Console.WriteLine("Found {0} mail, {1} bytes total.", numMail, totalBytes);

                for (long mailCounter = 1; mailCounter <= numMail; mailCounter++)
                {
                    var mailId = mailCounter.ToString();
                    var filename = String.Format("{0}@{1}[{2}].eml", username, server, mailId);
                    Console.WriteLine("Creating file {0}...", filename);
                    using (var outputFile = new StreamWriter(new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.Delete), encoding))
                    {
                        Console.WriteLine("Receiving mail {0}...", mailId);
                        response = SendCommand("RETR", mailId);
                        if (!response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine("Error receiving e-mail.");
                            {
                                SendCommand("QUIT");
                                return 1;
                            }
                        }

                        for (; ; )
                        {
                            var mailline = receive.ReadLine();
                            if (mailline == null)
                                throw new ProtocolViolationException("Unexpected end of e-mail stream.");

                            if (mailline.Equals(".", StringComparison.Ordinal))
                                break;

                            outputFile.WriteLine(mailline);
                        }
                    }

                    if (!keeponserver)
                    {
                        Console.WriteLine("Deleting mail on POP server...");
                        response = SendCommand("DELE", mailId);
                        if (!response[response.Count - 1][0].Equals("+OK", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine("Error deleting e-mail.");
                            {
                                SendCommand("QUIT");
                                return 1;
                            }
                        }
                    }
                }

                SendCommand("QUIT");

                return 0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
                Console.Error.WriteLine(ex.GetBaseException().Message);
                return -1;
            }
            finally
            {
                pop?.Dispose();
            }
        }
    }
}
