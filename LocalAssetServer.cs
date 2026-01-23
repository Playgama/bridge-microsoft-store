using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PlaygamaBridgeMicrosoftStore.Server
{
    internal sealed class LocalAssetServer : IAsyncDisposable
    {
        private readonly string _rootPath;
        private readonly HttpListener _listener;

        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        public Uri BaseUri { get; }

        public LocalAssetServer(string rootPath, int port = 0)
        {
            _rootPath = rootPath;

            var chosenPort = port == 0 ? GetFreeTcpPort() : port;
            BaseUri = new Uri($"http://127.0.0.1:{chosenPort}/");

            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUri.ToString());
        }

        public void Start()
        {
            if (_cts is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _listener.Start();
            _loopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleAsync(ctx), ct);
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                var rawPath = ctx.Request.Url?.AbsolutePath ?? "/";
                var rel = rawPath.TrimStart('/');
                if (string.IsNullOrWhiteSpace(rel))
                {
                    rel = "index.html";
                }

                rel = rel.Replace('/', Path.DirectorySeparatorChar);

                var full = Path.GetFullPath(Path.Combine(_rootPath, rel));
                var rootFull = Path.GetFullPath(_rootPath);

                // No path traversal
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 403;
                    ctx.Response.Close();
                    return;
                }

                if (!File.Exists(full))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                // Important for Unity WebGL: .gz must be served with Content-Encoding: gzip
                if (full.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.AddHeader("Content-Encoding", "gzip");
                }

                ctx.Response.AddHeader("Cache-Control", "no-cache");

                ctx.Response.ContentType = GetContentType(full);

                using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                ctx.Response.ContentLength64 = fs.Length;
                await fs.CopyToAsync(ctx.Response.OutputStream).ConfigureAwait(false);
                ctx.Response.OutputStream.Close();
                ctx.Response.Close();
            }
            catch
            {
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch { }
            }
        }

        private static string GetContentType(string fullPath)
        {
            // If file is *.something.gz, pick mime based on inner extension
            var fileName = Path.GetFileName(fullPath);
            var nameWithoutGz = fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^3]
                : fileName;

            var ext = Path.GetExtension(nameWithoutGz).ToLowerInvariant();

            return ext switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".wasm" => "application/wasm",
                ".data" => "application/octet-stream",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".mp4" => "video/mp4",
                _ => "application/octet-stream",
            };
        }

        private static int GetFreeTcpPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            try { return ((IPEndPoint)l.LocalEndpoint).Port; }
            finally { l.Stop(); }
        }

        public async ValueTask DisposeAsync()
        {
            if (_cts is null)
            {
                return;
            }

            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }

            if (_loopTask is not null)
            {
                try { await _loopTask.ConfigureAwait(false); } catch { }
            }

            _cts.Dispose();
            _cts = null;
        }
    }
}