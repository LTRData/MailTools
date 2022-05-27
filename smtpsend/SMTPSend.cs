using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Text;

namespace SMTPSend
{
    public static class SMTPSend
    {
        public static int Main(params string[] args)
        {
            Console.WriteLine("SMTPSEND, by Olof Lagerkvist.");
            Console.WriteLine("Copyright (c) 2013-2021");
            Console.WriteLine();
            Console.WriteLine("http://www.ltr-data.se");

            Encoding encoding;

            try
            {
                encoding = Encoding.GetEncoding(0);
            }
            catch
            {
                encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }

            string server = null;
            int? port = null;
            bool tls = false;
            bool tlsifsupported = false;
            bool ssl = false;
            bool unsafessl = false;
            bool delete = false;
            string heloname = null;
            string sender = null;
            string filename = null;
            var recipents = new List<string>(1);
            var verbose = false;

            foreach (var arg in args)
            {
                if (server == null && arg.StartsWith("/SERVER=", StringComparison.OrdinalIgnoreCase))
                    server = arg.Substring("/SERVER=".Length);
                else if (heloname == null && arg.StartsWith("/HELO=", StringComparison.OrdinalIgnoreCase))
                    heloname = arg.Substring("/HELO=".Length);
                else if (port == null && arg.StartsWith("/PORT=", StringComparison.OrdinalIgnoreCase))
                    port = int.Parse(arg.Substring("/PORT=".Length));
                else if (sender == null && arg.StartsWith("/FROM=", StringComparison.OrdinalIgnoreCase))
                    sender = arg.Substring("/FROM=".Length);
                else if (filename == null && arg.StartsWith("/FILE=", StringComparison.OrdinalIgnoreCase))
                    filename = arg.Substring("/FILE=".Length);
                else if (arg.StartsWith("/RCPT=", StringComparison.OrdinalIgnoreCase))
                    recipents.AddRange(arg.Substring("/RCPT=".Length).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (arg.Equals("/TLS", StringComparison.OrdinalIgnoreCase))
                    tls = true;
                else if (arg.Equals("/SSL", StringComparison.OrdinalIgnoreCase))
                    ssl = true;
                else if (arg.Equals("/VERBOSE", StringComparison.OrdinalIgnoreCase))
                    verbose = true;
                else if ((ssl || tls) && arg.Equals("/UNSAFE", StringComparison.OrdinalIgnoreCase))
                    unsafessl = true;
                else if (tls && arg.Equals("/IFSUPPORTED", StringComparison.OrdinalIgnoreCase))
                    tlsifsupported = true;
                else if (filename != null && arg.Equals("/DELETE", StringComparison.OrdinalIgnoreCase))
                    delete = true;
                else
                {
                    Console.WriteLine("Parameter '" + arg + "' not valid.");
                    Console.WriteLine();
                    Console.WriteLine
                        ("Utility to send an e-mail from file or standard input to an SMTP server." + Environment.NewLine +
                        Environment.NewLine +
                        "Usage:" + Environment.NewLine +
                        "smtpsend /SERVER=server [/PORT=portnumber] [/HELO=heloname] [/FROM=sender]" + Environment.NewLine +
                        "    [/FILE=filename [/DELETE]] [/TLS [/IFSUPPORTED] [/UNSAFE] | /SSL [/UNSAFE]]" + Environment.NewLine +
                        "    [/RCPT=receipent1 [/RCPT=receipent2] [...]]");

                    return 1;
                }
            }

            if (server == null)
            {
                Console.Error.WriteLine("Needs SMTP server name.");
                return -1;
            }

            Stream smtp = null;

            try
            {
                TextReader input;
                if (filename == null)
                    input = Console.In;
                else
                    input = new StreamReader(new FileStream(
                        filename,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete,
                        8),
                        encoding);

                var header = new List<string>(input.EnumerateLinesUntil(string.IsNullOrEmpty));

                if (port == null)
                    if (ssl)
                        port = 465;
                    else
                        port = 25;

                Console.WriteLine($"Connecting to SMTP server {server}:{port}...");
                smtp = Extensions.OpenTcpIpStream(server, port.Value);

                Console.WriteLine("Connected to SMTP server.");

                if (ssl)
                {
                    smtp = smtp.OpenSslStream(server, unsafessl);

                    Console.WriteLine("Connected SSL channel.");
                }

                var receive = new StreamReader(smtp, encoding);

                List<string> GetResponse()
                {
                    var messages = new List<string>(1);
                    for (; ; )
                    {
                        var recvline = receive.ReadLine();
                        Console.WriteLine("< " + recvline);
                        if (recvline == null)
                            break;

                        if (recvline.Length < 4 ||
                            ((recvline[3] != '-') && (recvline[3] != ' ')))
                            throw new Exception(recvline + " is not a valid response.");

                        messages.Add(recvline);
                        if (recvline[3] == ' ')
                            break;
                    }

                    return messages;
                }

                var response = GetResponse();
                if (!response[response.Count - 1].StartsWith("220", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Unknown response from server.");
                    return 2;
                }

                var send = new StreamWriter(smtp, encoding);

                List<string> SendCommand(params string[] command)
                {
                    var s = string.Join(" ", command);
                    Console.WriteLine("> " + s);
                    send.WriteLine(s);
                    send.Flush();
                    return GetResponse();
                }

                if (heloname == null)
                    heloname = Dns.GetHostName();

                for (; ; )
                {
                    if (tls)
                        response = SendCommand("EHLO", heloname);
                    else
                        response = SendCommand("HELO", heloname);

                    if (!response[response.Count - 1].StartsWith("250", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine("Unknown response from server.");

                        SendCommand("QUIT");
                        return 3;
                    }

                    if (tls && !(smtp is AuthenticatedStream))
                    {
                        if (response.FindIndex(s => s.IndexOf("STARTTLS", StringComparison.OrdinalIgnoreCase) == 4) < 0)
                        {
                            Console.Error.WriteLine("Server does not support TLS.");

                            if (tlsifsupported)
                                break;

                            SendCommand("QUIT");
                            return 4;
                        }

                        response = SendCommand("STARTTLS");
                        if (!response[response.Count - 1].StartsWith("220", StringComparison.Ordinal))
                        {
                            Console.Error.WriteLine("Unknown response from server.");
                            SendCommand("QUIT");
                            return 5;
                        }

                        smtp = smtp.OpenSslStream(server, unsafessl);
                        receive = new StreamReader(smtp, encoding);
                        send = new StreamWriter(smtp, encoding);

                        Console.WriteLine("Added TLS layer.");

                        continue;
                    }

                    break;
                }

                if (sender == null)
                {
                    var senderline =
                        header.Find(s =>
                        s.StartsWith("Return-Path: ", StringComparison.OrdinalIgnoreCase) ||
                        s.StartsWith("Reply-To: ", StringComparison.OrdinalIgnoreCase) ||
                        s.StartsWith("From: ", StringComparison.OrdinalIgnoreCase));

                    if (senderline != null)
                        sender = senderline.
                            Substring(senderline.IndexOf(": ") + ": ".Length).
                            TrimStart('<').
                            TrimEnd('>');
                }

                response = SendCommand("MAIL FROM:<" + sender + ">");

                if (!response[response.Count - 1].StartsWith("250", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Unknown response from server.");
                    SendCommand("QUIT");
                    return 6;
                }

                if (recipents.Count == 0)
                {
                    var deliveredtoline =
                        header.Find(s =>
                        s.StartsWith("Delivered-To: ", StringComparison.OrdinalIgnoreCase));

                    if (deliveredtoline != null)
                        recipents.Add(deliveredtoline.
                            Substring(deliveredtoline.IndexOf(": ") + ": ".Length).
                            TrimStart('<').
                            TrimEnd('>'));
                    else
                        recipents.
                            AddRange(header.Where(s =>
                                s.StartsWith("To: ", StringComparison.OrdinalIgnoreCase)).
                                Select(s => s.
                                    Substring("To: ".Length).
                                    TrimStart('<').
                                    TrimEnd('>')));
                }

                foreach (var recipent in recipents)
                {
                    response = SendCommand("RCPT TO:<" + recipent + ">");

                    if (!response[response.Count - 1].StartsWith("250", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine("Server did not accept recipient.");
                        SendCommand("QUIT");
                        return 7;
                    }
                }

                response = SendCommand("DATA");

                if (!response[response.Count - 1].StartsWith("354", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Unknown response from server.");
                    SendCommand("QUIT");
                    return 8;
                }

                header.ForEach(send.WriteLine);

                send.WriteLine();

                for (; ; )
                {
                    var mailline = input.ReadLine();

                    if (mailline == null)
                        break;

                    if (mailline.StartsWith(".", StringComparison.Ordinal))
                        mailline = "." + mailline;

                    send.WriteLine(mailline);
                }

                response = SendCommand(".");

                if (!response[response.Count - 1].StartsWith("250", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Unknown response from server.");
                    {
                        SendCommand("QUIT");
                        return 9;
                    }
                }

                if (delete && filename != null)
                {
                    input.Dispose();
                    File.Delete(filename);
                }

                SendCommand("QUIT");

                return 0;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine(ex.ToString());
#endif

                if (verbose)
                {
                    Console.Error.WriteLine(ex.Source + ": " + ex.ToString());
                }
                else
                {
                    Console.Error.WriteLine(ex.JoinMessages());
                }
                return -1;
            }
            finally
            {
                smtp?.Dispose();
            }
        }
    }
}
