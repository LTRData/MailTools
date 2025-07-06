using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using System.Runtime.Versioning;

namespace emlextr;

#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
static class Program
{
    static int Main(params string[] argsarray)
    {
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

        var args = new List<string>(argsarray);

        if (args.Count == 0)
        {
            Console.WriteLine("Syntax: emlextr [-o outdir] emlfile ...");
            return 0;
        }

        string target_dir = null;

        if (args[0].Equals("-o", StringComparison.OrdinalIgnoreCase) &&
            args.Count > 2)
        {
            target_dir = args[1];

            args.RemoveRange(0, 2);
        }

        var count = 0;

        foreach (var arg in args)
        {
            try
            {
                count += DoWork(target_dir, arg);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"File {arg} could not be parsed: {string.Join(" -> ", [.. ex.EnumerateMessages()])}");
            }
        }

        Console.WriteLine($"Saved {count} attachment files.");

        return count;
    }

    static int DoWork(string target_dir, string emlFile)
    {
        Console.WriteLine($"Parsing file {emlFile}...");

        var count = 0;

        var msg = new CDO.Message();

        try
        {
            var stream = new ADODB.Stream();

            try
            {
                stream.Open(Type.Missing,
                    ADODB.ConnectModeEnum.adModeUnknown,
                    ADODB.StreamOpenOptionsEnum.adOpenStreamUnspecified,
                    string.Empty, string.Empty);

                stream.LoadFromFile(emlFile);
                stream.Flush();

                msg.DataSource.OpenObject(stream, "_Stream");

                msg.DataSource.Save();

                stream.Close();
            }
            finally
            {
                Marshal.ReleaseComObject(stream);
            }

            foreach (CDO.IBodyPart att in msg.Attachments)
            {
                if (string.IsNullOrEmpty(att.FileName))
                {
                    continue;
                }

                var attFile = att.FileName;
                if (!string.IsNullOrEmpty(target_dir))
                {
                    attFile = Path.Combine(target_dir, attFile);
                }

                attFile = Path.GetFullPath(attFile);

                Console.WriteLine($"Saving attachment file {attFile}...");

                try
                {
                    att.SaveToFile(attFile);
                    count++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving {attFile}: {string.Join(" -> ", [.. ex.EnumerateMessages()])}");
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(msg);
        }

        return count;
    }
    internal static readonly char[] separator = [','];

    private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        var file = args.Name.Split(separator)[0] + ".dll";

        using var res = typeof(Program).Assembly.GetManifestResourceStream(typeof(Program), file);
        if (res == null)
        {
            return null;
        }

        var b = new byte[res.Length];

        _ = res.Read(b, 0, b.Length);

        return Assembly.Load(b);
    }

    private static IEnumerable<string> EnumerateMessages(this Exception ex)
    {
        while (ex != null)
        {
            var msg = ex.Message.Replace(Environment.NewLine, "");

            if (ex is ExternalException)
            {
                msg += $" ({(ex as ExternalException).ErrorCode:X})";
            }

            yield return msg;
            ex = ex.InnerException;
        }

        yield break;
    }
}
