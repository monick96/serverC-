using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace serverC_
{
    public class ServerConfig
    {
        public int Port { get; set; } = 8080;
        public string WebRoot { get; set; } = "wwwroot";
        public string LogDirectory { get; set; } = "logs";
    }

    public class HttpRequest
    {
        public string Method { get; set; } = "";
        public string Path { get; set; } = "";
        public string QueryString { get; set; } = "";
        public System.Collections.Generic.Dictionary<string, string> Headers { get; set; } = new();
        public string Body { get; set; } = "";
        public string RawRequestLine { get; set; } = "";
    }

    class Program
    {
        static ServerConfig _config = new();
        static string _logDirectoryPath = "logs";
        static readonly object _logLock = new object();

        static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _config = configuration.GetSection("ServerConfig").Get<ServerConfig>() ?? new ServerConfig();
            _logDirectoryPath = Path.GetFullPath(_config.LogDirectory);

            Directory.CreateDirectory(_config.WebRoot);
            Directory.CreateDirectory(_logDirectoryPath);

            Console.WriteLine($"Servidor iniciando en puerto: {_config.Port}");
            Console.WriteLine($"Carpeta de archivos: {Path.GetFullPath(_config.WebRoot)}");
            Console.WriteLine($"Logs en: {_logDirectoryPath}");
            Console.WriteLine("Presiona Ctrl+C para detener.\n");

            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, _config.Port));
            listener.Listen(1000);

            while (true)
            {
                var clientSocket = await listener.AcceptAsync();
                _ = HandleClientAsync(clientSocket);
            }
        }

        static async Task HandleClientAsync(Socket clientSocket)
        {
            string clientIp = "unknown";
            try
            {
                clientIp = (clientSocket.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";

                using (clientSocket)
                using (var networkStream = new NetworkStream(clientSocket, ownsSocket: true))
                {
                    var request = await ParseHttpRequestAsync(networkStream);

                    // IGNORAR conexiones vacías (favicon, keep-alive cortado, etc.)
                    if (string.IsNullOrEmpty(request.Method))return;

                    await LogRequestAsync(clientIp, request);

                    if (request.Method == "GET")
                    {
                        await HandleGetAsync(networkStream, request);
                    }
                    else if (request.Method == "POST")
                    {
                        await SendResponseAsync(networkStream, 200, "OK", 
                            "<html><body><h1>Datos POST recibidos y logueados correctamente</h1></body></html>", 
                            "text/html");
                    }
                    else
                    {
                        await SendResponseAsync(networkStream, 405, "Method Not Allowed", 
                            "<html><body><h1>405 - Método no permitido</h1></body></html>", 
                            "text/html");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error con cliente {clientIp}: {ex.Message}");
            }
        }

        static async Task<HttpRequest> ParseHttpRequestAsync(NetworkStream stream)
        {
            var request = new HttpRequest();
            var buffer = new byte[8192];
            var sb = new StringBuilder();

            int totalRead = 0;
            bool headersComplete = false;

            while (!headersComplete)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0) break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                totalRead += read;

                if (sb.ToString().Contains("\r\n\r\n"))
                    headersComplete = true;

                if (totalRead > 65536) break;
            }

            string rawRequest = sb.ToString();
            var lines = rawRequest.Split(new[] { "\r\n" }, StringSplitOptions.None);
            
            if (lines.Length == 0) return request;

            request.RawRequestLine = lines[0];
            var parts = lines[0].Split(' ');
            if (parts.Length >= 2)
            {
                request.Method = parts[0].ToUpper();
                string fullPath = parts[1];
                
                int queryIndex = fullPath.IndexOf('?');
                if (queryIndex >= 0)
                {
                    request.Path = fullPath.Substring(0, queryIndex);
                    request.QueryString = fullPath.Substring(queryIndex + 1);
                }
                else
                {
                    request.Path = fullPath;
                }
            }

            int i = 1;
            for (; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) break;
                int colonIndex = lines[i].IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = lines[i].Substring(0, colonIndex).Trim();
                    string value = lines[i].Substring(colonIndex + 1).Trim();
                    request.Headers[key] = value;
                }
            }

            if (request.Method == "POST" && request.Headers.ContainsKey("Content-Length"))
            {
                int contentLength = int.Parse(request.Headers["Content-Length"]);
                int headerEnd = rawRequest.IndexOf("\r\n\r\n") + 4;
                int bodyAlreadyRead = rawRequest.Length - headerEnd;

                var bodyBuilder = new StringBuilder();
                if (bodyAlreadyRead > 0)
                    bodyBuilder.Append(rawRequest.Substring(headerEnd, bodyAlreadyRead));

                while (bodyBuilder.Length < contentLength)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    bodyBuilder.Append(Encoding.UTF8.GetString(buffer, 0, read));
                }

                request.Body = bodyBuilder.ToString();
            }

            return request;
        }

        static async Task HandleGetAsync(NetworkStream stream, HttpRequest request)
        {
            string relativePath = request.Path == "/" ? "index.html" : request.Path.TrimStart('/');
            relativePath = relativePath.Replace("..", "").Replace("//", "/");
            string fullPath = Path.Combine(_config.WebRoot, relativePath);

            if (File.Exists(fullPath))
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(fullPath);
                string contentType = GetContentType(fullPath);
                await SendResponseAsync(stream, 200, "OK", fileBytes, contentType);
            }
            else
            {
                string notFoundPath = Path.Combine(_config.WebRoot, "404.html");
                byte[] notFoundBytes;
                
                if (File.Exists(notFoundPath))
                    notFoundBytes = await File.ReadAllBytesAsync(notFoundPath);
                else
                    notFoundBytes = Encoding.UTF8.GetBytes("<html><body><h1>404 - Not Found</h1></body></html>");

                await SendResponseAsync(stream, 404, "Not Found", notFoundBytes, "text/html");
            }
        }

        static async Task SendResponseAsync(NetworkStream stream, int statusCode, string statusText, byte[] bodyBytes, string contentType)
        {
            byte[] compressedBody;
            using (var ms = new MemoryStream())
            {
                using (var gzip = new GZipStream(ms, CompressionMode.Compress, true))
                {
                    await gzip.WriteAsync(bodyBytes, 0, bodyBytes.Length);
                }
                compressedBody = ms.ToArray();
            }

            var response = new StringBuilder();
            response.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
            response.AppendLine($"Content-Type: {contentType}");
            response.AppendLine($"Content-Encoding: gzip");
            response.AppendLine($"Content-Length: {compressedBody.Length}");
            response.AppendLine("Connection: close");
            response.AppendLine();

            byte[] headerBytes = Encoding.UTF8.GetBytes(response.ToString());
            
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(compressedBody, 0, compressedBody.Length);
            await stream.FlushAsync();
        }

        static async Task SendResponseAsync(NetworkStream stream, int statusCode, string statusText, string body, string contentType)
        {
            await SendResponseAsync(stream, statusCode, statusText, Encoding.UTF8.GetBytes(body), contentType);
        }

        static async Task LogRequestAsync(string clientIp, HttpRequest request)
        {
            try
            {
                string date = DateTime.Now.ToString("yyyy-MM-dd");
                string logFile = Path.Combine(_logDirectoryPath, $"{date}.log");
                
                var logEntry = new StringBuilder();
                logEntry.AppendLine($"[{DateTime.Now:HH:mm:ss}] IP: {clientIp}");
                logEntry.AppendLine($"  Método: {request.Method}");
                logEntry.AppendLine($"  Ruta: {request.Path}");
                
                if (!string.IsNullOrEmpty(request.QueryString))
                    logEntry.AppendLine($"  Query: {request.QueryString}");
                
                if (!string.IsNullOrEmpty(request.Body))
                    logEntry.AppendLine($"  Body: {request.Body}");
                
                logEntry.AppendLine(new string('-', 50));

                await Task.Run(() =>
                {
                    lock (_logLock)
                    {
                        File.AppendAllText(logFile, logEntry.ToString());
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error escribiendo log: {ex.Message}");
            }
        }

        static string GetContentType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return ext switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }
}