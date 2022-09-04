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
