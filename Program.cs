using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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

        // Cola thread-safe para desacoplar el logging del procesamiento de requests
        static readonly ConcurrentQueue<string> _logQueue = new();
        static readonly CancellationTokenSource _logCts = new();

        static async Task Main(string[] args)
        {
            // 1. LEER CONFIGURACIÓN EXTERNA (nativa de .NET, sin parsear texto plano)
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _config = configuration.GetSection("ServerConfig").Get<ServerConfig>() ?? new ServerConfig();
            _logDirectoryPath = Path.GetFullPath(_config.LogDirectory);

            Directory.CreateDirectory(_config.WebRoot);
            Directory.CreateDirectory(_logDirectoryPath);

            // 2. ARRANCAR LOGGER EN SEGUNDO PLANO (desacoplado del procesamiento de requests)
            StartBackgroundLogger();

            Console.WriteLine($"🚀 Servidor iniciando en puerto: {_config.Port}");
            Console.WriteLine($"📁 Carpeta de archivos: {Path.GetFullPath(_config.WebRoot)}");
            Console.WriteLine($"📝 Logs en: {_logDirectoryPath}");
            Console.WriteLine("Presiona Ctrl+C para detener.");

            // 3. CREAR SOCKET TCP (directamente en capa de transporte)
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, _config.Port));
            listener.Listen(1000); // Backlog de conexiones pendientes

            // 4. BUCLE INFINITO ASÍNCRONO (acepta conexiones sin bloquear)
            while (true)
            {
                var clientSocket = await listener.AcceptAsync();

                // Fire-and-forget: procesa cada cliente en paralelo sin bloquear el bucle principal
                _ = HandleClientAsync(clientSocket);
            }
        }

        // ============================================================
        // HILO DE FONDO: Escribe logs desde la cola al disco
        // Esto evita que el I/O lento del disco bloquee las respuestas HTTP
        // ============================================================
        static void StartBackgroundLogger()
        {
            _ = Task.Run(async () =>
            {
                while (!_logCts.Token.IsCancellationRequested)
                {
                    // Vaciar toda la cola en disco de una sola vez (batch write)
                    var batch = new StringBuilder();
                    int count = 0;

                    while (_logQueue.TryDequeue(out var logEntry))
                    {
                        batch.Append(logEntry);
                        count++;
                    }

                    if (count > 0)
                    {
                        string date = DateTime.Now.ToString("yyyy-MM-dd");
                        string logFile = Path.Combine(_logDirectoryPath, $"{date}.log");

                        try
                        {
                            File.AppendAllText(logFile, batch.ToString());
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Error escribiendo log: {ex.Message}");
                        }
                    }

                    // Esperar antes de revisar la cola de nuevo
                    await Task.Delay(100, _logCts.Token);
                }
            });
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
                    // Parsear la solicitud HTTP manualmente desde los bytes del socket
                    var request = await ParseHttpRequestAsync(networkStream);

                    // Ignorar conexiones vacías (keep-alive cortado, favicon sin método, etc.)
                    if (string.IsNullOrEmpty(request.Method))
                        return;

                    // Loguear todo el tráfico (IP, query strings, body POST) - NO bloquea
                    await LogRequestAsync(clientIp, request);

                    // Procesar según el método HTTP
                    if (request.Method == "GET")
                    {
                        await HandleGetAsync(networkStream, request);
                    }
                    else if (request.Method == "POST")
                    {
                        // POST: solo loguear los datos (ya logueados arriba) y responder OK
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

        // ============================================================
        // PARSEAR HTTP MANUALMENTE (leer bytes del socket y separar líneas)
        // ============================================================
        static async Task<HttpRequest> ParseHttpRequestAsync(NetworkStream stream)
        {
            var request = new HttpRequest();
            var buffer = new byte[8192];
            var sb = new StringBuilder();

            int totalRead = 0;
            bool headersComplete = false;

            // Leer hasta encontrar el final de los headers (\r\n\r\n)
            while (!headersComplete)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0) break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                totalRead += read;

                if (sb.ToString().Contains("\r\n\r\n"))
                    headersComplete = true;

                if (totalRead > 65536) break; // Límite de seguridad para headers
            }

            string rawRequest = sb.ToString();
            var lines = rawRequest.Split(new[] { "\r\n" }, StringSplitOptions.None);

            if (lines.Length == 0) return request;

            // Línea 1: METHOD PATH HTTP/1.1
            request.RawRequestLine = lines[0];
            var parts = lines[0].Split(' ');
            if (parts.Length >= 2)
            {
                request.Method = parts[0].ToUpper();
                string fullPath = parts[1];

                // Separar path y query string
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

            // Parsear headers
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

            // Leer body si es POST y tiene Content-Length
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

        // ============================================================
        // MANEJAR GET (archivos estáticos)
        // ============================================================
        static async Task HandleGetAsync(NetworkStream stream, HttpRequest request)
        {
            // Si no especifica archivo, devolver index.html por defecto
            string relativePath = request.Path == "/" ? "index.html" : request.Path.TrimStart('/');

            // Sanitizar: evitar path traversal con ../
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
                // 404 con página personalizada
                string notFoundPath = Path.Combine(_config.WebRoot, "404.html");
                byte[] notFoundBytes;

                if (File.Exists(notFoundPath))
                    notFoundBytes = await File.ReadAllBytesAsync(notFoundPath);
                else
                    notFoundBytes = Encoding.UTF8.GetBytes("<html><body><h1>404 - Not Found</h1></body></html>");

                await SendResponseAsync(stream, 404, "Not Found", notFoundBytes, "text/html");
            }
        }

        // ============================================================
        // ENVIAR RESPUESTA HTTP CON COMPRESIÓN GZIP
        // ============================================================
        static async Task SendResponseAsync(NetworkStream stream, int statusCode, string statusText, byte[] bodyBytes, string contentType)
        {
            // Comprimir body con GZip
            byte[] compressedBody;
            using (var ms = new MemoryStream())
            {
                using (var gzip = new GZipStream(ms, CompressionMode.Compress, true))
                {
                    await gzip.WriteAsync(bodyBytes, 0, bodyBytes.Length);
                }
                compressedBody = ms.ToArray();
            }

            // Construir headers HTTP manualmente
            var response = new StringBuilder();
            response.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
            response.AppendLine($"Content-Type: {contentType}");
            response.AppendLine($"Content-Encoding: gzip");
            response.AppendLine($"Content-Length: {compressedBody.Length}");
            response.AppendLine("Connection: close");
            response.AppendLine(); // Línea en blanco separa headers de body

            byte[] headerBytes = Encoding.UTF8.GetBytes(response.ToString());

            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(compressedBody, 0, compressedBody.Length);
            await stream.FlushAsync();
        }

        // Sobrecarga para enviar string directamente
        static async Task SendResponseAsync(NetworkStream stream, int statusCode, string statusText, string body, string contentType)
        {
            await SendResponseAsync(stream, statusCode, statusText, Encoding.UTF8.GetBytes(body), contentType);
        }

        // ============================================================
        // LOGS: ENCOLAR EN MEMORIA (NO bloquea el request)
        // ============================================================
        static Task LogRequestAsync(string clientIp, HttpRequest request)
        {
            var logEntry = new StringBuilder();
            logEntry.AppendLine($"[{DateTime.Now:HH:mm:ss}] IP: {clientIp}");
            logEntry.AppendLine($"  Método: {request.Method}");
            logEntry.AppendLine($"  Ruta: {request.Path}");

            if (!string.IsNullOrEmpty(request.QueryString))
                logEntry.AppendLine($"  Query: {request.QueryString}");

            if (!string.IsNullOrEmpty(request.Body))
                logEntry.AppendLine($"  Body: {request.Body}");

            logEntry.AppendLine(new string('-', 50));

            // Encolar en memoria (O(1), thread-safe, NO bloquea)
            _logQueue.Enqueue(logEntry.ToString());

            return Task.CompletedTask;
        }

        // ============================================================
        // CONTENT-TYPE POR EXTENSIÓN DE ARCHIVO
        // ============================================================
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