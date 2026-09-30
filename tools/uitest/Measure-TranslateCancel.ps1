<#
.SYNOPSIS
The live measurement ticket S0010 TD-3 deferred (S0037 TR-7): what cancelling a streaming translation
costs the thread that cancels it.

.DESCRIPTION
The question behind TD-3: on .NET Framework, does disposing a half-read chunked HTTP response drain
the rest of it - i.e. wait for the model to finish its answer? If it does, a cancel run inline on the
UI thread (Esc, the popup closed) would stall that thread, which is also the thread of both low-level
hooks. TD-3 moved the dispose to the pool without having measured it; this script measures it.

Two scenes against the local Ollama, each a long answer that is cancelled about a second into the
stream:

  1. raw   - HttpClient on this very runtime, the response stream disposed inline after a few lines:
             the time the Dispose() call takes is the drain, if there is one;
  2. app   - the production path, reflection into the built CyrFlip.exe: OllamaClient.GenerateAsync
             with a real transport, CancellationTokenSource.Cancel() timed on the calling thread, and
             the time until GenerateAsync gives up.

Nothing here changes any setting or file of CyrFlip's. It needs Ollama running with the model named
below, and it must run under Windows PowerShell 5.1: the exe is .NET Framework, and so is the
behaviour being measured (PowerShell 7 would measure .NET's HttpClient instead).

.PARAMETER Model
An installed model; a small one answers fastest. Default qwen2.5:3b.

.PARAMETER Endpoint
The Ollama address. Default http://127.0.0.1:11434.

.EXAMPLE
powershell.exe -NoProfile -File tools\uitest\Measure-TranslateCancel.ps1
#>
#requires -PSEdition Desktop
param(
    [string]$Model = 'qwen2.5:3b',
    [string]$Endpoint = 'http://127.0.0.1:11434'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$exe = @('Release', 'Debug') | ForEach-Object { Join-Path $root "src\CyrFlip\bin\$_\net48\CyrFlip.exe" } |
    Where-Object { Test-Path $_ } | Sort-Object { (Get-Item $_).LastWriteTimeUtc } -Descending | Select-Object -First 1
if (-not $exe) { throw 'CyrFlip.exe not built - run dotnet build first.' }

Add-Type -ReferencedAssemblies System.Net.Http -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class TranslateCancelProbe
{
    private const string Prompt = "Write a very long, detailed essay about the history of keyboard layouts, at least two thousand words.";

    // C# 5: Add-Type in Windows PowerShell compiles with the .NET Framework's own compiler.
    private static string Body(string model)
    {
        return "{\"model\":\"" + model + "\",\"prompt\":\"" + Prompt + "\",\"stream\":true}";
    }

    public static string Raw(string endpoint, string model)
    {
        using (var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint + "/api/generate")
            {
                Content = new StringContent(Body(model), Encoding.UTF8, "application/json"),
            };
            HttpResponseMessage response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Result;
            Stream stream = response.Content.ReadAsStreamAsync().Result;
            var reader = new StreamReader(stream);
            var clock = Stopwatch.StartNew();
            int lines = 0;
            while (clock.ElapsedMilliseconds < 1000 && reader.ReadLine() != null) lines++;
            var dispose = Stopwatch.StartNew();
            reader.Dispose();
            response.Dispose();
            dispose.Stop();
            return "raw: read " + lines + " line(s) in " + clock.ElapsedMilliseconds + " ms, then Dispose() took "
                + dispose.ElapsedMilliseconds + " ms on the calling thread";
        }
    }

    public static string App(string exe, string endpoint, string model)
    {
        Assembly app = Assembly.LoadFrom(exe);
        Type type = app.GetType("CyrFlip.OllamaClient", true);
        object client = Activator.CreateInstance(type, new object[] { endpoint });
        MethodInfo generate = type.GetMethod("GenerateAsync");
        using (var cts = new CancellationTokenSource())
        {
            var first = new ManualResetEventSlim(false);
            Action<string> partial = delegate(string chunk) { first.Set(); };
            var clock = Stopwatch.StartNew();
            var task = (Task)generate.Invoke(client, new object[] { model, "", Prompt, 5, partial, 120000, 30000, 600000, cts.Token });
            if (!first.Wait(120000)) return "app: no first chunk within 120 s";
            long firstAt = clock.ElapsedMilliseconds;
            Thread.Sleep(1000);
            var cancel = Stopwatch.StartNew();
            cts.Cancel();
            cancel.Stop();
            var ended = Stopwatch.StartNew();
            try { task.Wait(60000); } catch (AggregateException) { }
            ended.Stop();
            return "app: first chunk at " + firstAt + " ms; Cancel() took " + cancel.ElapsedMilliseconds
                + " ms on the calling thread; GenerateAsync ended " + ended.ElapsedMilliseconds + " ms later ("
                + task.Status + ")";
        }
    }
}
'@

Write-Host "Ollama $Endpoint, model $Model, exe $exe"
Write-Host ([TranslateCancelProbe]::Raw($Endpoint, $Model))
Write-Host ([TranslateCancelProbe]::App($exe, $Endpoint, $Model))
Write-Host ''
Write-Host 'Reading it: a raw Dispose() in the hundreds of ms or more is the drain TD-3 feared, and the'
Write-Host 'reason the app queues it to the pool; the app''s Cancel() must stay near 0 ms either way.'
