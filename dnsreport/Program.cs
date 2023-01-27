using System.IO.Compression;
using System.Net;
using System.Xml.Linq;
using System.Xml.XPath;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;

namespace dnsreport;

public static class Program
{
    public static async Task<int> Main(params string[] args)
    {
        using var cancellationTokenSource = new CancellationTokenSource();

        var cancellationToken = cancellationTokenSource.Token;

        Console.CancelKeyPress += (sender, e) =>
        {
            cancellationTokenSource.Cancel();
            e.Cancel = true;
        };

        var rc = 0;

        foreach (var arg in args)
        {
            try
            {
                XDocument xml;

                switch (Path.GetExtension(arg).ToLowerInvariant())
                {
                    case ".zip":
                        {
                            using var zip = ZipFile.OpenRead(arg);

                            var entry = zip.Entries.FirstOrDefault(entry => entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

                            if (entry is null)
                            {
                                throw new InvalidOperationException($"No xml documents in zip archive");
                            }

                            using var data = entry.Open();

                            xml = await XDocument.LoadAsync(data, LoadOptions.None, cancellationToken).ConfigureAwait(false);

                            break;
                        }

                    case ".gz":
                        {
                            using var data = new GZipStream(File.OpenRead(arg), CompressionMode.Decompress);

                            xml = await XDocument.LoadAsync(data, LoadOptions.None, cancellationToken).ConfigureAwait(false);

                            break;
                        }

                    case ".z":
                        {
                            using var data = new DeflateStream(File.OpenRead(arg), CompressionMode.Decompress);

                            xml = await XDocument.LoadAsync(data, LoadOptions.None, cancellationToken).ConfigureAwait(false);

                            break;
                        }

                    case ".br":
                        {
                            using var data = new BrotliStream(File.OpenRead(arg), CompressionMode.Decompress);

                            xml = await XDocument.LoadAsync(data, LoadOptions.None, cancellationToken).ConfigureAwait(false);

                            break;
                        }

                    case ".xml":
                        {
                            using var data = File.OpenRead(arg);

                            xml = await XDocument.LoadAsync(data, LoadOptions.None, cancellationToken).ConfigureAwait(false);

                            break;
                        }

                    default:
                        {
                            throw new NotImplementedException();
                        }
                }

                var report_metadata = xml.XPathSelectElement("/feedback/report_metadata");

                var org_name = report_metadata?.Element("org_name")?.Value;

                var date_range = report_metadata?.Element("date_range");

                var date_from = int.TryParse(date_range?.Element("begin")?.Value, out var begin)
                    ? (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(begin) : null;

                var date_to = int.TryParse(date_range?.Element("end")?.Value, out var end)
                    ? (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(end) : null;

                Console.WriteLine($"Report by '{org_name}', from {date_from} to {date_to}");

                foreach (var row in xml.XPathSelectElements("/feedback/record/row"))
                {
                    var source_ip = row.Element("source_ip")?.Value;
                    var count = row.Element("count")?.Value;
                    var dkim = row.Element("policy_evaluated")?.Element("dkim")?.Value;
                    var spf = row.Element("policy_evaluated")?.Element("spf")?.Value;

                    var source = source_ip is null ? "<unknown>" : (await Dns.GetHostEntryAsync(source_ip).ConfigureAwait(false))?.HostName;

                    Console.WriteLine($"Source IP: {source_ip} - Source host: {source} - Count: {count} - dkim: {dkim} - spf: {spf}");
                }
            }
            catch (Exception ex)
            {
                rc = ex.HResult;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Failed to open or parse '{arg}': {ex.GetBaseException().Message}");
                Console.ResetColor();
            }
        }

        if (!Console.IsOutputRedirected)
        {
            Console.WriteLine("Press enter to continue");
            Console.ReadLine();
        }

        return rc;
    }
}
