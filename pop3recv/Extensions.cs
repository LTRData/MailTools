using System;
using System.Net.Security;
using System.Net.Sockets;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0161 // Convert to file-scoped namespace

#if NETFRAMEWORK && !NET35_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    internal sealed class ExtensionAttribute : Attribute
    {
    }
}
#endif

#if NETSTANDARD && !NETSTANDARD2_0_OR_GREATER

namespace System.Net
{
    public class ProtocolViolationException : InvalidOperationException
    {
        public ProtocolViolationException() { }
        public ProtocolViolationException(string message) : base(message) { }
    }
}
#endif

namespace POP3Recv
{
    internal static class Extensions
    {

#if NETSTANDARD && !NETSTANDARD2_0_OR_GREATER

        public static void AuthenticateAsClient(this SslStream ssl, string targetHost) => ssl.AuthenticateAsClientAsync(targetHost).Wait();

        public static void Close(this Socket socket) => socket.Dispose();

#endif

#if NETFRAMEWORK

        public static Type GetTypeInfo(this Type type) => type;

#endif

    }
}
