using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System;
using System.IO;
using System.Net;
using System.Net.Mail;

namespace MailOut;

public static class Program
{
    public static int Main(params string[] args)
    {
        try
        {
            return UnsafeMain(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.JoinMessages());
            Console.ResetColor();

            return ex.HResult;
        }
    }
    
    public static int UnsafeMain(params string[] args)
    {
        var cmds = CommandLineParser.ParseCommandLine(args, StringComparer.Ordinal);

        string? file = null;
        using var smtp = new SmtpClient();
        using var mail = new MailMessage();
        string? username = null;
        string? password = null;
        bool deleteAfterSend = false;

        foreach (var cmd in cmds)
        {
            if (cmd.Key == "server" && cmd.Value.Length == 1)
            {
                smtp.Host = cmd.Value[0];
            }
            else if (cmd.Key == "file" && cmd.Value.Length == 1)
            {
                file = cmd.Value[0];
            }
            else if (cmd.Key == "mailfrom" && cmd.Value.Length == 1)
            {
                mail.From = new(cmd.Value[0]);
            }
            else if (cmd.Key == "mailto" && cmd.Value.Length >= 1)
            {
                foreach (var to in cmd.Value)
                {
                    mail.To.Add(to);
                }
            }
            else if (cmd.Key == "subject" && cmd.Value.Length == 1)
            {
                mail.Subject = cmd.Value[0];
            }
            else if (cmd.Key == "username" && cmd.Value.Length == 1)
            {
                username = cmd.Value[0];
            }
            else if (cmd.Key == "password" && cmd.Value.Length == 1
                && cmds.ContainsKey("username"))
            {
                password = cmd.Value[0];
            }
            else if (cmd.Key == "delete" && cmd.Value.Length == 0)
            {
                deleteAfterSend = true;
            }
            else if (cmd.Key == "ssl" && cmd.Value.Length == 0)
            {
                smtp.EnableSsl = true;
            }
            else
            {
                Console.WriteLine("Usage: mailout --server <smtp server> --file <path to eml file> [--mailfrom <email address>] [--mailto <email address(es)>] [--subject <email subject>] [--username <smtp username> --password <smtp password>] [--delete]");
                return -1;
            }
        }

        if (smtp.Host is null)
        {
            Console.WriteLine("Missing required argument: server");
            return 1;
        }

        if (file is null)
        {
            Console.WriteLine("Missing required argument: emlfile");
            return 1;
        }

        using (var reader = new StreamReader(file))
        {
            while (reader.ReadLine() is { Length: > 0 } line)
            {
                if (line.StartsWith("From:", StringComparison.OrdinalIgnoreCase))
                {
                    mail.From ??= new(line.AsSpan(5).Trim().ToString());
                }
                else if (line.StartsWith("To:", StringComparison.OrdinalIgnoreCase))
                {
                    if (mail.To.Count == 0)
                    {
                        mail.To.Add(line.AsSpan(3).Trim().ToString());
                    }
                }
                else if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(mail.Subject))
                    {
                        mail.Subject = line.AsSpan(8).Trim().ToString();
                    }
                }
                else
                {
                    var colonIndex = line.IndexOf(':');
                    var key = line.AsSpan(0, colonIndex).Trim().ToString();
                    var value = line.AsSpan(colonIndex + 1).Trim().ToString();

                    mail.Headers.Add(key, value);
                }
            }

            if (mail.From is null)
            {
                Console.WriteLine("Missing required argument: mailfrom");
                return 1;
            }

            if (mail.To.Count == 0)
            {
                Console.WriteLine("Missing required argument: mailto");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(mail.Subject))
            {
                Console.WriteLine("Warning, missing subject");
            }

            mail.Body = reader.ReadToEnd();
        }

        if (username is not null)
        {
            smtp.Credentials = new NetworkCredential(username, password);
        }

        Console.WriteLine($"Sending email {file} to server {smtp.Host}...");

        smtp.Send(mail);

        if (deleteAfterSend)
        {
            File.Delete(file);
        }

        return 0;
    }
}
