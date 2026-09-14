# MailTools

Command-line utilities for sending and retrieving email, inspecting POP3 mailboxes, extracting message attachments and summarizing DMARC aggregate reports.

## Tools

| Tool | Purpose | Requirements and behavior |
| --- | --- | --- |
| [mailout](mailout) | Sends a message file through `System.Net.Mail.SmtpClient`, with optional sender, recipient and subject overrides. | Supports username/password credentials and STARTTLS. Uses a simple message parser; see limitations below. |
| [smtpsend](smtpsend) | Sends message text from a file or standard input through a direct SMTP connection. | Configurable port, envelope addresses, STARTTLS or implicit TLS; no SMTP AUTH implementation. |
| [pop3recv](pop3recv) | Retrieves all messages in a POP3 mailbox into individual `.eml` files. | **Deletes retrieved messages from the server by default. Use `/KEEP` to retain them.** |
| [pop3mgr](pop3mgr) | Interactive POP3 session with authentication, mailbox listing and manual protocol commands. | Prompts for missing connection details and credentials. Commands can retrieve or delete messages. |
| [emlextr](emlextr) | Extracts named attachments from one or more `.eml` files. | Windows only; requires registered CDO and ADODB COM components. |
| [dnsreport](dnsreport) | Summarizes DMARC aggregate XML reports, including source IP, message count and DKIM/SPF results. | Performs reverse DNS lookups for source addresses. Accepts compressed reports as described below. |

## Build and runtime requirements

Use the .NET 10 SDK for the current source tree. All six projects have `net8.0`, `net9.0` and `net10.0` targets. Build an individual tool for one framework, for example:

```sh
dotnet build mailout/mailout.csproj -c Release -f net10.0
```

Substitute the desired project name to build another tool. Shared build properties place outputs under `Release/<framework>/` in the repository root. Run a framework-dependent build with the matching installed .NET runtime:

```sh
dotnet Release/net10.0/mailout.dll --server smtp.example.com --file message.eml --ssl
```

The mail and report tools use managed .NET networking and file APIs; choose a runtime for your operating system. `emlextr` must run on Windows with its COM dependencies available. Its bundled `Interop.CDO.dll` and `Interop.ADODB.dll` are interop assemblies, not installers for the native COM components.

The projects also retain these compatibility targets:

| Projects | Additional targets |
| --- | --- |
| `mailout` | `net46`, `netstandard2.0` |
| `smtpsend` | `net35`, `net40`, `netstandard2.0` |
| `pop3recv`, `pop3mgr` | `net20`, `net40`, `netstandard2.0` |
| `emlextr` | `net35`, `net40` |
| `dnsreport` | `netstandard2.1` |

.NET Standard is a compatibility target, not a standalone runtime. Building every target may require additional framework reference assemblies. `mailout` restores the `LTRData.Extensions` NuGet dependency.

## Sending messages

### mailout

A simple input file contains headers, a blank line and the body:

```text
From: sender@example.com
To: recipient@example.net
Subject: Example message

Hello from MailTools.
```

`--mailfrom`, `--mailto` (one or more addresses) and `--subject` override the corresponding values from the file. `--username` and `--password` supply SMTP credentials. `--delete` removes the input file after a successful send.

Despite its name, `--ssl` requests [STARTTLS through SmtpClient](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient.enablessl), not implicit TLS on port 465. There is no port command-line option; modern .NET builds use the default SMTP port 25.

The parser reads headers one line at a time and assigns the remaining text to `MailMessage.Body`. It does not unfold headers, decode MIME parts or reconstruct attachments. Use simple message files; arbitrary MIME messages are not preserved by this conversion.

### smtpsend

```sh
dotnet Release/net10.0/smtpsend.dll /SERVER=smtp.example.com /TLS /FROM=sender@example.com /RCPT=recipient@example.net /FILE=message.eml
```

Omit `/FILE` to read from standard input. `/FROM` and `/RCPT` set SMTP envelope addresses; they do not rewrite the message headers. Multiple recipients can be supplied through repeated `/RCPT` arguments or a comma-separated value. `/DELETE`, placed after `/FILE`, removes the file after the server accepts the message.

Use a server that permits sending without SMTP AUTH. This utility transmits message text line by line rather than parsing it into a `MailMessage`.

## POP3 tools

Retrieve mail while retaining the server copies:

```sh
dotnet Release/net10.0/pop3recv.dll /SERVER=pop.example.com /SSL /USER=account /PASSWORD=example-password /KEEP
```

Files are created in the current directory as `account@server[number].eml`. Existing filenames cause an error rather than being overwritten, so use a separate directory for each retrieval. Retrieval processes the mailbox by message number; it does not maintain a UIDL-based download history. Messages are decoded and written as text, not preserved byte for byte.

For an interactive session, let `pop3mgr` prompt for the password:

```sh
dotnet Release/net10.0/pop3mgr.dll /SERVER=pop.example.com /PORT=995 /SSL /USER=account
```

It lists the mailbox and then accepts commands such as `CAPA`, `TOP 1 0`, `RETR 1`, `DELE 1`, `RSET` and `QUIT`. Server deletions are committed when the POP3 session successfully quits.

Both POP3 tools support USER/PASS and APOP authentication. These are legacy protocol clients without OAuth support.

## Connection options

For `smtpsend`, `pop3recv` and `pop3mgr`:

- `/TLS` requests a protocol upgrade: SMTP STARTTLS or POP3 STLS.
- `/SSL` starts TLS immediately. Default ports are SMTP 465 and POP3 995; otherwise they are 25 and 110. `/PORT` overrides the port.
- `/UNSAFE`, placed after a TLS option, disables server certificate validation.
- The direct socket implementations use IPv4.

Encryption is not enabled by default in the noninteractive tools. The examples above explicitly select TLS. Passwords supplied as command-line arguments may be visible in shell history or process listings.

## Attachments and reports

On Windows, with the COM prerequisites available and an existing output directory:

```sh
dotnet Release/net10.0/emlextr.dll -o attachments message.eml
```

Place `-o` before the input filenames. Without it, attachments are saved relative to the current directory. The program uses attachment-provided filenames and returns the number of saved attachments as its exit code.

To summarize reports:

```sh
dotnet Release/net10.0/dnsreport.dll report.xml report.xml.gz
```

Supported inputs are `.xml`, `.zip` (first XML entry), `.gz`, `.z` (Deflate) and, on modern .NET builds, `.br` (Brotli). The tool prints the reporting organization, date range and per-source results. It pauses for Enter when output is not redirected.
