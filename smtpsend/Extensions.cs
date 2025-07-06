using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SMTPSend;

public static partial class Extensions
{
    public static AssemblyName AssemblyName { get; } = typeof(Extensions).Assembly.GetName();

    public static Version AssemblyVersion { get; } = typeof(Extensions).Assembly.GetName().Version;

    public static IEnumerable<string> EnumerateLinesUntil(this TextReader reader, Predicate<string> predicate)
    {
        for (;;)
        {
            var line = reader.ReadLine();
            if (predicate(line))
            {
                yield break;
            }
            else
            {
                yield return line;
            }
        }
    }

    public static IEnumerable<Exception> Enumerate(this Exception ex)
    {
        while (ex != null)
        {
            yield return ex;
            ex = ex.InnerException;
        }

        yield break;
    }

    public static IEnumerable<string> GetMessages(this Exception ex)
    {
        while (ex != null)
        {
            if (ex is TargetInvocationException)
            {
                ex = ex.InnerException;
                continue;
            }
#if NET40_OR_GREATER || NETSTANDARD || NETCOREAPP
            else if (ex is AggregateException)
            {
                var agex = ex as AggregateException;

                foreach (var inner in agex.InnerExceptions)
                {
                    foreach (var msg in GetMessages(inner))
                    {
                        yield return msg;
                    }
                }

                break;
            }
#endif
            else if (ex is TargetInvocationException)
            {
            }
            else
            {
                yield return ex.Message;
            }

            ex = ex.InnerException;
        }
    }

    public static string JoinMessages(this Exception exception)
    {
        return JoinMessages(exception, " -> ");
    }

    public static string JoinMessages(this Exception exception, string separator)
    {
#if NETFRAMEWORK && !NET40_OR_GREATER
        var messages = GetMessages(exception).ToArray();
#else
        var messages = GetMessages(exception);
#endif

        return string.Join(separator, messages);
    }

    public static NetworkStream OpenTcpIpStream(string host, int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            socket.Connect(host, port);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            try
            {
                ((IDisposable)socket).Dispose();
            }
            catch
            {
            }

            throw;
        }
    }

    public static SslStream OpenSslStream(this Stream inner, string hostName, bool ignoreCertErrors)
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
}
