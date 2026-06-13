# 📚 Guía de Defensa Oral - Servidor Web C# (Sockets)

> **Proyecto Final - Redes y Comunicaciones**  
> **Autor:** Melgarejo Monica  
> **Tecnología:** .NET 8, sockets TCP nativos, async/await  
> **Fecha:** Junio 2026

---

## 📋 Tabla de Contenidos

1. [Conceptos Fundamentales](#1-conceptos-fundamentales)
2. [Arquitectura del Servidor](#2-arquitectura-del-servidor)
3. [Análisis Línea por Línea](#3-análisis-línea-por-línea)
4. [El Cliente de Estrés (Tester)](#4-el-cliente-de-estrés-tester)
5. [Preguntas Probables del Profesor](#5-preguntas-probables-del-profesor)
6. [Frases Mágicas para la Defensa](#6-frases-mágicas-para-la-defensa)
7. [Checklist de Requisitos](#7-checklist-de-requisitos)
8. [Cómo Ejecutar y Demostrar](#8-cómo-ejecutar-y-demostrar)
9. [Errores Comunes y Cómo Responder](#9-errores-comunes-y-cómo-responder)

---

## 1. Conceptos Fundamentales

### HTTP/1.1
Protocolo de comunicación de la web. Es **texto plano**. El cliente manda una línea con el verbo, la ruta y la versión; luego headers; y opcionalmente un body. El servidor responde con código de estado (`200`, `404`) y el contenido.

### Verbos HTTP
| Verbo | ¿Qué hace? | Analogía |
|---|---|---|
| **GET** | Solicita un recurso. Solo lee, no modifica. | *"Traeme el menú."* |
| **POST** | Enviar datos al servidor. | *"Tomá mi pedido."* |

### Socket
Abstracción del sistema operativo. Es el **extremo de una conexión TCP** por donde pasan bytes crudos. Todo programa que habla por red usa sockets (aunque no los veas). Es la base de TCP/IP.

| Tipo de Socket | Analogía | Función |
|---|---|---|
| **Socket Listener** | Puerta principal del hotel. | Escucha conexiones entrantes en un puerto. |
| **Socket de cliente (en servidor)** | Habitación privada del hotel. | Creado por `Accept()` para hablar con **ese** cliente. |
| **Socket del cliente (en navegador)** | Puerta de tu casa. | Abre el navegador para conectarse al servidor. |

### Puerto
Número que identifica qué servicio atiende en una máquina. El IP es la dirección del edificio; el puerto es el departamento. Ej: `8080`.

### Hilo (Thread)
Línea de ejecución. Como un empleado del hotel. Cada hilo consume ~1 MB de RAM.

### ThreadPool
Conjunto de hilos que .NET mantiene listos. No contratás uno nuevo cada vez; reutilizás. `Task.Run` agarra un hilo del pool. Si no hay libres, la tarea espera en cola.

### Tarea (Task)
Unidad de trabajo encolada en el ThreadPool. **No es un hilo.** Es un pedido que se pone en la mesa del empleado. Una tarea puede hacer `await` y liberar el hilo.

### Concurrencia
Capacidad de atender múltiples clientes sin que uno bloquee a otro. No significa "al mismo tiempo exacto" (eso es paralelismo). Significa que el progreso de uno no depende de que otro termine.

### Async / Await
Patrón de C# para ejecutar operaciones sin bloquear el hilo mientras se espera. Es como un empleado que deja un pedido en la cocina, va a atender otra mesa, y vuelve cuando está listo. Cuando el código llega a `await`, el hilo se libera y vuelve al pool.

### Semáforo (`SemaphoreSlim`)
Objeto que controla el acceso mediante un contador de permisos. Es un portero que deja pasar N personas a la vez. Si está lleno, los demás esperan afuera sin consumir hilo.

### `lock`
Mecanismo para que solo un hilo acceda a una sección de código a la vez. Es un cartel de **"uno a la vez"** en el baño. Protege variables compartidas de condiciones de carrera.

### Condición de Carrera (Race Condition)
Error cuando dos hilos acceden a una variable compartida sin sincronización. Dos personas leen el mismo número del pizarrón, suman 1, y escriben el mismo resultado. Se pierde una cuenta.

### I/O Bound
El programa pasa la mayoría del tiempo **esperando** (red, disco), no calculando. Un servidor web es I/O bound. La CPU está libre mientras espera.

### Backlog
Fila de espera que el **sistema operativo** arma antes de que tu programa acepte conexiones. `Listen(1000)` = 1,000 pueden esperar en cola. Si llegan más, el SO las rechaza.

### Fire-and-Forget
Lanzar una tarea y **no esperar** a que termine. El programa sigue inmediatamente. En tu servidor, permite que el bucle principal acepte nuevos clientes sin quedarse esperando.

### Compresión GZip
Algoritmo que reduce el tamaño de los datos antes de enviarlos. Como meter la ropa en una bolsa al vacío. El servidor comprime; el navegador descomprime automáticamente al ver `Content-Encoding: gzip`.

### WebSocket vs Socket TCP
| | Socket TCP | WebSocket |
|---|---|---|
| ¿Qué es? | Capa de transporte (tubo de bytes). | Protocolo sobre HTTP con conexión permanente. |
| ¿Quién habla? | Turnos. | Ambos cuando quieren (bidireccional). |
| ¿Se queda abierta? | Depende del protocolo arriba. | Sí, permanentemente. |
| Uso | HTTP, FTP, todo. | Chat en tiempo real, notificaciones push. |

**Tu servidor usa Socket TCP + HTTP/1.1. No WebSocket.**

### CancellationTokenSource
Botón de apagado remoto de .NET. Cuando apretás `Ctrl+C`, se dispara la cancelación. Todos los bucles que monitorean el token se detienen limpiamente.

---

## 2. Arquitectura del Servidor

### Tecnologías
- **.NET 8** como runtime.
- **Sockets TCP nativos** (`System.Net.Sockets.Socket`).
- **Async/await** para concurrencia sin bloqueo.
- **GZip** para compresión de respuestas.
- **JSON externo** (`appsettings.json`) para configuración.
- **CancellationTokenSource** para cierre limpio con Ctrl+C.

### Flujo de una Petición

```
┌─────────────────┐
│  appsettings.json  │
│  (Puerto, WebRoot) │
└────────┬────────┘
         │ ConfigurationBuilder
         ▼
┌─────────────────┐
│  Socket TCP (IPv4)   │
│   Escucha en Puerto    │
└────────┬────────┘
         │ AcceptAsync (no bloquea)
         ▼
┌─────────────────┐     ┌─────────────────┐
│  Cliente conecta   │────▶│ Parseo Manual HTTP │
└────────┬────────┘     │  (Método, Path,     │
         │              │   Query, Headers)  │
         ▼              └────────┬────────┘
┌─────────────────┐            │
│  Loguear Request   │◄───────────┘
│  (IP, Query, Body) │
└────────┬────────┘
         │
    ┌────┴────┐
    ▼         ▼
┌───────┐  ┌────────┐
│  GET   │  │  POST   │
└───┬───┘  └───┬────┘
    │          │
    ▼          ▼
┌────────┐  ┌────────────┐
│ Leer   │  │ Loguear    │
│ archivo│  │ Body       │
│ disco  │  │            │
└───┬────┘  └─────┬──────┘
    │             │
    ▼             ▼
┌────────┐  ┌────────────┐
│ GZip   │  │ Responder  │
│Compress│  │ 200 OK     │
└───┬────┘  └─────┬──────┘
    │             │
    └──────┬──────┘
           ▼
    ┌────────────┐
    │ Enviar por   │
    │ NetworkStream│
    │ (Socket TCP) │
    └────────────┘
```

---

## 3. Análisis Línea por Línea

### 3.1 Configuración (`ServerConfig` + `appsettings.json`)

```csharp
public class ServerConfig
{
    public int Port { get; set; } = 8080;
    public string WebRoot { get; set; } = "wwwroot";
    public string LogDirectory { get; set; } = "logs";
}
```

**¿Qué hace?** Mapea el JSON de configuración a un objeto C#.

```json
{
  "ServerConfig": {
    "Port": 8080,
    "WebRoot": "wwwroot",
    "LogDirectory": "logs"
  }
}
```

**¿Por qué no parsear a mano?** Usamos `Microsoft.Extensions.Configuration` con el **binder**. El método `Get<ServerConfig>()` lee el JSON y asigna automáticamente los valores a las propiedades. Evitamos código de parseo manual.

**Frase:** *"Usamos la librería `Microsoft.Extensions.Configuration` para leer el archivo JSON. El método `Get<ServerConfig>()` hace el binding automáticamente: convierte la configuración escrita en texto plano a un objeto C# sin que nosotros escribamos código de parseo manual."*

> **Nota:** `reloadOnChange: false` porque el puerto y carpetas se definen al inicio. Cambiarlos en caliente requeriría reiniciar el socket de todos modos. El archivo JSON se puede editar sin recompilar el código C#, pero para que los cambios surtan efecto es necesario reiniciar el servidor.

---

### 3.2 El `Main` — Arranque del Servidor

```csharp
static async Task Main(string[] args)
```

**¿Por qué `async Task`?** Porque usamos `await` dentro del `Main`. Si fuera `void`, no podríamos usar `await`.

```csharp
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

_config = configuration.GetSection("ServerConfig").Get<ServerConfig>() ?? new ServerConfig();
```

**Binding:** convierte el JSON en nuestro objeto automáticamente.

```csharp
Directory.CreateDirectory(_config.WebRoot);
Directory.CreateDirectory(_logDirectoryPath);
```

Crea las carpetas si no existen.

```csharp
StartBackgroundLogger();
```

Arranca el hilo de fondo que escribe logs.

```csharp
var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.Bind(new IPEndPoint(IPAddress.Any, _config.Port));
listener.Listen(1000);
```

| Parámetro | Significado |
|---|---|
| `AddressFamily.InterNetwork` | IPv4 |
| `SocketType.Stream` | TCP (confiable, ordenado) |
| `ProtocolType.Tcp` | Protocolo TCP |
| `Bind` | "Enchufo" el socket al puerto configurado |
| `Listen(1000)` | Backlog de 1,000 conexiones pendientes |

**Frase:** *"Creamos un socket TCP nativo, lo ligamos al puerto configurado externamente, y definimos un backlog de 1,000 conexiones pendientes."*

---

### 3.3 Captura de Ctrl+C para Cierre Limpio

```csharp
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;        // No cerrar de golpe
    _cts.Cancel();          // Avisar a todos los bucles que paren
    Console.WriteLine("\n🛑 Señal de detención recibida. Cerrando servidor...");
};
```

**¿Qué hace?**
- `e.Cancel = true` → Evita que Windows mate el proceso abruptamente.
- `_cts.Cancel()` → Aprieta el botón de apagado. Todos los bucles que monitorean `_cts.Token` se detienen.

**Frase:** *"Capturamos el evento `Console.CancelKeyPress` para interceptar Ctrl+C. En vez de que Windows mate el proceso de golpe, disparamos `CancellationTokenSource.Cancel()` para avisar a todos los bucles que terminen de forma limpia, liberando el socket y cerrando los archivos de log."*

---

### 3.4 Bucle Principal con Concurrencia y Cancelación

```csharp
try
{
    while (!_cts.Token.IsCancellationRequested)
    {
        var clientSocket = await listener.AcceptAsync(_cts.Token);
        _ = HandleClientAsync(clientSocket);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("🔌 Socket listener detenido.");
}
finally
{
    listener.Close();  // Cerrar limpiamente
}
```

| Línea | ¿Qué hace? |
|---|---|
| `await listener.AcceptAsync(_cts.Token)` | Espera un cliente. Si se cancela, lanza `OperationCanceledException`. |
| `_ = HandleClientAsync(clientSocket)` | **Fire-and-forget**. Lanza la tarea y sigue inmediatamente. |
| `catch (OperationCanceledException)` | Atrapamos la excepción de cancelación. No es un error, es cierre normal. |
| `finally { listener.Close(); }` | El socket se cierra SIEMPRE, aunque haya error. |

**¿Por qué fire-and-forget?** Si usáramos `await`, el servidor sería secuencial: atendería de a un cliente por vez. Con `_ =`, el bucle nunca se detiene.

**Frase:** *"Usamos `AcceptAsync` con el token de cancelación para poder detener el servidor limpiamente con Ctrl+C. Cuando llega un cliente, lanzamos `HandleClientAsync` en modo fire-and-forget, lo que permite atender múltiples clientes simultáneamente sin crear hilos dedicados."*

---

### 3.5 Logger en Segundo Plano

```csharp
static void StartBackgroundLogger()
{
    _ = Task.Run(async () =>
    {
        while (!_cts.Token.IsCancellationRequested)
        {
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
                File.AppendAllText(logFile, batch.ToString());
            }

            await Task.Delay(100, _cts.Token);
        }
    });
}
```

**¿Por qué un hilo aparte?** Escribir en disco es lento (I/O bound). Si lo hiciéramos en cada request, el cliente esperaría innecesariamente.

**¿Qué hace?**
1. Cada 100ms despierta.
2. Saca **todo** de la cola (`batch write`).
3. Escribe **una sola vez** en el archivo del día.
4. Vuelve a dormir.

**¿Por qué `ConcurrentQueue`?** Es **thread-safe**. Varios hilos pueden hacer `Enqueue` al mismo tiempo sin pisarse.

**¿Por qué `Task.Delay(100, _cts.Token)`?** El token permite que el `Delay` se corte inmediatamente si se cancela. Sin el token, podría tardar hasta 100ms en darse cuenta.

**Frase:** *"Desacoplamos el logging con una cola concurrente en memoria y un hilo de fondo que escribe en batch cada 100ms. Esto evita que el I/O lento del disco bloquee las respuestas HTTP. El `CancellationToken` en `Task.Delay` permite que el logger se despierte inmediatamente si se solicita el cierre."*

---

### 3.6 Atendiendo al Cliente (`HandleClientAsync`)

```csharp
static async Task HandleClientAsync(Socket clientSocket)
{
    string clientIp = "unknown";
    try
    {
        clientIp = (clientSocket.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";

        using (clientSocket)
        using (var networkStream = new NetworkStream(clientSocket, ownsSocket: true))
        {
```

| Línea | ¿Qué hace? |
|---|---|
| `RemoteEndPoint as IPEndPoint` | Obtiene la IP del cliente. Conversión segura (`as` devuelve `null` si falla). |
| `?.Address.ToString()` | Operador de navegación segura. Si es `null`, no explota. |
| `?? "unknown"` | Si todo dio `null`, usa `"unknown"` por defecto. |
| `using (clientSocket)` | Cierra el socket automáticamente al terminar, incluso si hay error. |
| `NetworkStream(..., ownsSocket: true)` | Envuelve el socket para leer/escribir como archivo. `ownsSocket: true` = el stream también cierra el socket. |

**Frases:**
- *"Obtenemos la IP del cliente mediante `RemoteEndPoint`, con conversiones seguras para evitar errores si el formato no es el esperado."*
- *"Usamos `using` para garantizar que el socket se cierre automáticamente al terminar, incluso si ocurre una excepción. Esto evita fugas de recursos y puertos bloqueados."*
- *"`NetworkStream` envuelve el socket para leer y escribir bytes como si fuera un stream de archivo, simplificando el código. Con `ownsSocket: true`, el stream también se encarga de cerrar el socket cuando se dispone."*

---

### 3.7 Parseo Manual de HTTP (`ParseHttpRequestAsync`)

```csharp
static async Task<HttpRequest> ParseHttpRequestAsync(NetworkStream stream)
{
    var buffer = new byte[8192];    // Cajón de 8KB
    var sb = new StringBuilder();   // Armador de texto

    while (!headersComplete)
    {
        int read = await stream.ReadAsync(buffer, 0, buffer.Length);
        sb.Append(Encoding.UTF8.GetString(buffer, 0, read));

        if (sb.ToString().Contains("\r\n\r\n"))
            headersComplete = true;  // Fin de headers encontrado
    }
```

**¿Por qué 8KB?** No sabemos de antemano cuánto mide el request. Leemos de a pedazos hasta encontrar el delimitador `

`.

**¿Qué es `

`?** Fin de línea + línea en blanco + fin de línea en blanco = **fin de los headers en HTTP**.

```csharp
    // Línea 1: METHOD PATH HTTP/1.1
    var parts = lines[0].Split(' ');
    request.Method = parts[0].ToUpper();           // "GET"
    string fullPath = parts[1];                     // "/index.html?nombre=Juan"

    int queryIndex = fullPath.IndexOf('?');
    if (queryIndex >= 0)
    {
        request.Path = fullPath.Substring(0, queryIndex);       // "/index.html"
        request.QueryString = fullPath.Substring(queryIndex + 1);   // "nombre=Juan"
    }
```

**Separación de path y query:** busca el `?` y divide.

```csharp
    // Parsear headers
    int colonIndex = lines[i].IndexOf(':');
    string key = lines[i].Substring(0, colonIndex).Trim();    // "Content-Length"
    string value = lines[i].Substring(colonIndex + 1).Trim();  // "23"
    request.Headers[key] = value;
```

**Headers:** separan por `:` en key y value.

```csharp
    // Leer body si es POST y tiene Content-Length
    if (request.Method == "POST" && request.Headers.ContainsKey("Content-Length"))
    {
        int contentLength = int.Parse(request.Headers["Content-Length"]);
        // Leer exactamente esa cantidad de bytes del stream
    }
```

**Body:** para POST, leemos exactamente lo que dice `Content-Length`.

**Frase:** *"Parseamos HTTP manualmente leyendo bytes del socket en bloques de 8KB hasta detectar el delimitador `

` que marca el fin de los headers. Separamos la primera línea para obtener método, ruta y query string, luego recorremos los headers separando por el carácter `:` y guardando en un diccionario. Para POST, leemos el body adicional usando el valor de `Content-Length`."*

---

### 3.8 Manejar GET (`HandleGetAsync`)

```csharp
string relativePath = request.Path == "/" ? "index.html" : request.Path.TrimStart('/');
relativePath = relativePath.Replace("..", "").Replace("//", "/");
```

| Línea | ¿Qué hace? |
|---|---|
| `== "/" ? "index.html"` | Si no especifica archivo, sirve `index.html` por defecto. |
| `Replace("..", "")` | Sanitización básica contra **path traversal** (`../../etc/passwd`). |

```csharp
if (File.Exists(fullPath))
{
    byte[] fileBytes = await File.ReadAllBytesAsync(fullPath);
    await SendResponseAsync(stream, 200, "OK", fileBytes, contentType);
}
else
{
    // 404 con página personalizada
    await SendResponseAsync(stream, 404, "Not Found", notFoundBytes, "text/html");
}
```

**Frase:** *"Primero sanitizamos la ruta para evitar path traversal. Si el archivo existe en `wwwroot`, lo leemos del disco y enviamos con código 200. Si no existe, devolvemos código 404 y servimos el archivo `404.html` personalizado."*

---

### 3.9 Enviar Respuesta con GZip (`SendResponseAsync`)

```csharp
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
```

**¿Qué comprime?** El **body** (el archivo HTML, CSS, etc.). Los **headers** van en texto plano.

```csharp
// Construir headers HTTP manualmente
response.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
response.AppendLine($"Content-Type: {contentType}");
response.AppendLine($"Content-Encoding: gzip");   // ← Aviso al navegador
response.AppendLine($"Content-Length: {compressedBody.Length}");
response.AppendLine("Connection: close");           // ← Cerrar después de responder
response.AppendLine(); // Línea en blanco separa headers de body
```

**Frase:** *"La compresión GZip la aplica únicamente el servidor en las respuestas. El navegador envía las peticiones en texto plano, ya que los headers y el body de un GET o POST son pequeños. El servidor comprime los archivos estáticos antes de enviarlos, y el navegador los descomprime automáticamente al detectar el header `Content-Encoding: gzip`."*

---

### 3.10 Logging (`LogRequestAsync`)

```csharp
static Task LogRequestAsync(string clientIp, HttpRequest request)
{
    // ... arma el texto del log ...
    _logQueue.Enqueue(logEntry.ToString());
    return Task.CompletedTask;
}
```

**¿Por qué `Task.CompletedTask`?** El método es `async` en apariencia pero no usa `await` adentro (solo encola). Devuelve una tarea ya completada para que el llamador pueda hacer `await`.

**Frase:** *"`ConcurrentQueue` en el servidor es solo para los logs. Es una cola thread-safe donde muchos hilos (los clientes) meten logs al mismo tiempo, y un hilo de fondo los saca para escribirlos en disco. El resto del servidor no necesita estructuras thread-safe porque no comparte variables entre hilos."*

---

## 4. El Cliente de Estrés (Tester)

### ¿Qué es?
Un programa que simula muchos usuarios pegándole al servidor al mismo tiempo para ver si aguanta.

### Configuración
```csharp
string url = "http://localhost:8080/";
int totalRequests = 500;        // Peticiones totales
int concurrentConnections = 500;  // Cuántas al mismo tiempo
```

### El Semáforo (`SemaphoreSlim`)
```csharp
var semaphore = new SemaphoreSlim(concurrentConnections);
```

**¿Qué hace?** Es un **contador de permisos**. Arranca en 500.
- `await semaphore.WaitAsync()` = "¿Hay lugar? Si sí, paso y el contador baja. Si no, espero."
- `semaphore.Release()` = "Devuelvo mi permiso, pase el siguiente."

**¿Por qué en el tester y no en el servidor?** El tester se **autolimita** para no saturar la red. El servidor no necesita semáforo porque `async/await` + ThreadPool ya gestionan la concurrencia.

### El `lock`
```csharp
lock (lockObj) successCount++;
lock (lockObj) failCount++;
```

**¿Por qué?** Los contadores (`int`) son variables simples compartidas por múltiples hilos. Sin `lock`, dos hilos podrían leer el mismo valor, sumar 1, y escribir el mismo resultado → **condición de carrera**.

**Frase:** *"El semáforo está en el tester, no en el servidor. El tester se autolimita para simular una carga realista sin saturar la red ni la máquina. El servidor no necesita semáforo porque usa `async/await` con el ThreadPool de .NET, que ya gestiona la concurrencia de forma eficiente. El `lock` en el tester protege los contadores de race conditions; en el servidor usamos `ConcurrentQueue` que es thread-safe por diseño."*

---

## 5. Preguntas Probables del Profesor

### 1. "¿Por qué usaste asincronía y no threads?"
**Respuesta corta (30 segundos):**
> *"Porque un servidor web es I/O bound: pasa el 90% del tiempo esperando red o disco. Si uso un hilo por cliente, cada hilo consume 1 MB de RAM y se bloquea esperando. Con `async/await`, unos pocos hilos del Thread Pool atienden miles de conexiones sin bloquearse, delegando la espera al hardware."*

**Si te pide profundizar:**
- **Memoria:** 10,000 hilos = ~10 GB RAM solo en stacks.
- **CPU:** Context switching entre miles de hilos consume ciclos del procesador en tareas administrativas.
- **I/O bound:** La tarjeta de red o el disco avisan cuando terminan, y el hilo retoma desde el Thread Pool.

### 2. "¿Cómo parseás el HTTP si no usás un framework?"
> *"Leo los bytes crudos del `NetworkStream` en un buffer de 8 KB. Convierto esos bytes a string con `Encoding.UTF8`, busco el delimitador `

` que separa headers del body, y divido por `
`. La primera línea tiene `METHOD PATH HTTP/1.1`. Separo el path del query string por el `?`. Para POST, leo el `Content-Length` del header y consumo exactamente esa cantidad de bytes del stream."*

### 3. "¿Cómo leés la configuración externa?"
> *"Usé `Microsoft.Extensions.Configuration`, que es la herramienta nativa de .NET. Con `ConfigurationBuilder` apunto a `appsettings.json`, leo la sección `ServerConfig` y la mapeo automáticamente a una clase `ServerConfig` con el binder. No parseé texto plano manualmente; usé el sistema de configuración que .NET provee para esto."*

### 4. "¿Cómo manejás la concurrencia?"
> *"El bucle principal hace `await listener.AcceptAsync()`, que no bloquea el hilo cuando no hay clientes. Cuando llega uno, lanzo `HandleClientAsync` con fire-and-forget (`_ = ...`). Eso significa que el bucle vuelve inmediatamente a escuchar el siguiente cliente, mientras el anterior se procesa en paralelo. Como todo es async, no se crean hilos nuevos; se usan los del Thread Pool existente."*

### 5. "¿Cómo funciona la compresión?"
> *"Antes de enviar la respuesta, paso los bytes del archivo por un `GZipStream` en modo `Compress`. Eso me devuelve el body comprimido. En los headers HTTP agrego `Content-Encoding: gzip` para que el navegador sepa que debe descomprimirlo antes de mostrarlo."*

### 6. "¿Qué pasa si me piden un archivo que no existe?"
> *"Primero sanitizo la ruta para evitar path traversal (`../`). Si el archivo no existe en `wwwroot`, devuelvo código HTTP 404 y sirvo el archivo `404.html`, que es una página personalizada con diseño visual. Si incluso `404.html` no existiera, devuelvo un HTML de respaldo embebido en el código."*

### 7. "¿Cómo loguean las solicitudes?"
> *"Cada solicitud se registra en un archivo por día (`yyyy-MM-dd.log`). Guardo: timestamp, IP de origen, método HTTP, ruta, query string si existe, y el body completo si es POST. Uso una `ConcurrentQueue` en memoria y un hilo de fondo que escribe en batch cada 100ms, para que el I/O del disco no bloquee las respuestas."*

### 8. "¿Cómo probaste que funciona?"
> *"Probé con el navegador y las DevTools (F12). Verifiqué que `/` devuelve `index.html`, que `/style.css` devuelve `Content-Type: text/css` y `Content-Encoding: gzip`, que una ruta inexistente devuelve 404 con la página personalizada, que el formulario POST loguea el body, y que los query strings aparecen en el archivo de log. También usé el tester de estrés para verificar concurrencia."*

### 9. "¿Por qué `Listen(1000)` y no otro número?"
> *"Es el backlog: cuántas conexiones pueden esperar en cola antes de ser aceptadas. 1,000 es generoso. Si el servidor está saturado, el SO rechaza las que excedan. Para nuestro proyecto es suficiente; en producción se usarían balanceadores de carga."*

### 10. "¿Es seguro el `Replace("..", "")` contra path traversal?"
> *"Es básico. Un atacante sofisticado podría usar codificación URL (`%2e%2e`). Para la materia alcanza, pero en producción se usaría `Path.GetFullPath` con validación estricta."*

### 11. "¿Por qué `Connection: close`?"
> *"Simplifica el manejo. Con `keep-alive` habría que manejar múltiples requests por conexión, parsear `Content-Length` vs chunked, etc. Para este alcance, `close` es suficiente."*

### 12. "¿Qué pasa si dos clientes piden el mismo archivo al mismo tiempo?"
> *"`File.ReadAllBytesAsync` es segura para lectura concurrente. El archivo no se modifica, así que no hay conflicto."*

### 13. "¿Y si el log crece mucho?"
> *"Se separa por día (`yyyy-MM-dd.log`). Cada día es un archivo nuevo. No hay rotación automática, pero la consigna no la pide."*

### 14. "¿Qué diferencia hay entre socket y WebSocket?"
> *"Usamos sockets TCP nativos para implementar HTTP/1.1. WebSocket es un protocolo diferente que se construye sobre HTTP y mantiene la conexión abierta para comunicación bidireccional en tiempo real. Nosotros no lo usamos porque nuestra consigna requiere el modelo clásico de request-response de HTTP, donde cada conexión se cierra después de responder."*

### 15. "¿Los datos viajan encriptados?"
> *"No. Usamos HTTP/1.1 sin TLS/SSL. Los datos viajan como texto plano, con el body comprimido mediante GZip. No implementamos HTTPS porque la consigna no lo requiere y agregaría complejidad significativa al manejo de certificados y el handshake de encriptación."*

### 16. "¿Cómo cerrás el servidor?"
> *"Capturamos el evento `Console.CancelKeyPress` para interceptar Ctrl+C. En vez de que Windows mate el proceso de golpe, disparamos `CancellationTokenSource.Cancel()` para avisar a todos los bucles que terminen de forma limpia. El bucle principal, el `AcceptAsync` y el logger de fondo detectan la señal y se detienen, liberando el socket y cerrando los archivos de log."*

### 17. "¿Por qué un solo `CancellationTokenSource` para todo?"
> *"Usamos un único `CancellationTokenSource` para coordinar el cierre de todo el servidor. Al capturar Ctrl+C, se dispara la cancelación y tanto el bucle de escucha como el logger de fondo detectan la señal y terminan sus tareas de forma limpia, liberando el socket y cerrando los archivos de log."*

### 18. "¿Qué pasa si llegan 50,000 clientes a la vez?"
> *"El sistema operativo acepta 1,000 en el backlog. Las demás son rechazadas. De las 1,000 que entran, el bucle `AcceptAsync` las va sacando de a una y lanzando `HandleClientAsync` en paralelo. El servidor puede procesarlas concurrentemente, pero el límite real lo ponen la RAM, el ThreadPool y el ancho de banda."*

### 19. "¿Por qué el tester tiene semáforo y el servidor no?"
> *"El semáforo está en el tester porque es un cliente que se autolimita para no saturar la red ni la máquina del tester. El servidor no necesita semáforo porque usa `async/await` con el ThreadPool de .NET, que ya gestiona la concurrencia de forma natural. El `lock` en el tester protege los contadores de race conditions; en el servidor usamos `ConcurrentQueue` que es thread-safe por diseño."*

### 20. "¿Qué es `ownsSocket: true`?"
> *"`ownsSocket: true` le dice al `NetworkStream` que es el dueño del socket. Cuando el stream se cierra (al salir del `using`), también cierra el socket. Es una doble seguridad: tanto el `using (clientSocket)` como el `using (networkStream)` se aseguran de que el socket se libere."*

---

## 6. Frases Mágicas para la Defensa

### Sobre async/await vs hilos
> *"Elegimos programación asíncrona sobre hilos dedicados porque un servidor web es I/O bound: pasa la mayor parte del tiempo esperando datos de red o del disco. Con `async/await`, el hilo se libera durante esa espera y puede atender otras peticiones. Si usáramos un hilo por cliente, con miles de conexiones el sistema operativo colapsaría por el consumo de memoria y el context switching."*

### Sobre el ThreadPool
> *".NET maneja automáticamente los hilos a través del ThreadPool. Nosotros solo creamos tareas (`Task`) y el sistema se encarga de asignarlas, reutilizar hilos y liberarlos durante las esperas. Es más fácil de programar que crear hilos manualmente, más eficiente en memoria, y evita el context switching que saturaría el procesador con miles de hilos."*

### Sobre sockets
> *"El socket es la interfaz entre nuestro programa y el sistema operativo para enviar y recibir datos por la red. El servidor abre un socket listener en un puerto para escuchar, y por cada cliente que se conecta, el sistema operativo crea un socket nuevo dedicado a esa conversación. El cliente también abre un socket para iniciar la conexión. Sin sockets no hay comunicación TCP/IP."*

### Sobre HTTP
> *"HTTP es un protocolo de texto. El cliente manda una línea con el verbo, la ruta y la versión, luego encabezados, y opcionalmente un cuerpo. Nosotros leemos eso del socket byte por byte y lo interpretamos."*

### Sobre GET y POST
> *"GET es para solicitar recursos. El servidor busca el archivo y lo devuelve. POST es para enviar datos; en nuestro caso solo los leemos del cuerpo de la petición y los guardamos en el log, sin devolver un archivo."*

### Sobre la compresión
> *"La compresión GZip la aplica únicamente el servidor en las respuestas. El navegador envía las peticiones en texto plano, ya que los headers y el body de un GET o POST son pequeños. El servidor comprime los archivos estáticos antes de enviarlos, y el navegador los descomprime automáticamente al detectar el header `Content-Encoding: gzip`."*

### Sobre el modelo del servidor
> *"Nuestro servidor es un servidor web tradicional que devuelve HTML completo. A diferencia de una API REST que solo entrega datos en JSON, nosotros armamos las páginas en el servidor y el navegador las renderiza directamente. Esto incluye el formulario POST: el navegador envía los datos, el servidor los procesa y responde con una página de confirmación."*

### Sobre el cierre limpio
> *"Capturamos el evento `Console.CancelKeyPress` para interceptar Ctrl+C. En vez de que Windows mate el proceso de golpe, disparamos `CancellationTokenSource.Cancel()` para avisar a todos los bucles que terminen de forma limpia, liberando el socket y cerrando los archivos de log."*

---

## 7. Checklist de Requisitos

| # | Requisito | ¿Cumple? | Implementación |
|---|-----------|----------|----------------|
| 1 | **Concurrencia indefinida** | ✅ | `async/await` + `AcceptAsync` + fire-and-forget |
| 2 | **Index.html por defecto** | ✅ | Si `Path == "/"`, se resuelve como `index.html` |
| 3 | **Carpeta de archivos configurable** | ✅ | `ServerConfig.WebRoot` en `appsettings.json` |
| 4 | **Puerto configurable** | ✅ | `ServerConfig.Port` en `appsettings.json` |
| 5 | **Error 404 personalizado** | ✅ | Retorna `404.html` con diseño visual y código HTTP 404 |
| 6 | **GET y POST** | ✅ | GET sirve archivos; POST loguea datos y responde 200 OK |
| 7 | **Query strings logueados** | ✅ | Extraídos de la URL y registrados en el archivo de log |
| 8 | **Compresión de respuestas** | ✅ | `GZipStream` + header `Content-Encoding: gzip` |
| 9 | **Logs por día con IP** | ✅ | Archivo `logs/yyyy-MM-dd.log` con IP de origen |
| 10 | **Sockets directos, sin frameworks** | ✅ | `Socket` + `NetworkStream`; parseo manual de HTTP |
| 11 | **Cierre limpio** | ✅ | `CancellationTokenSource` + `Console.CancelKeyPress` |

---

## 8. Cómo Ejecutar y Demostrar

### 8.1 Arrancar el Servidor

```bash
cd serverC-
dotnet run
```

**Salida esperada:**
```
🚀 Servidor iniciando en puerto: 8080
📁 Carpeta de archivos: C:\...\serverC-\wwwroot
📝 Logs en: C:\...\serverC-\logs
Presiona Ctrl+C para detener.
```

### 8.2 Pruebas en el Navegador

| URL | ¿Qué probar? | ¿Qué ver en DevTools (F12)? |
|---|---|---|
| `http://localhost:8080/` | Página por defecto | Status 200, `Content-Type: text/html`, `Content-Encoding: gzip` |
| `http://localhost:8080/style.css` | Archivo CSS | Status 200, `Content-Type: text/css`, `Content-Encoding: gzip` |
| `http://localhost:8080/noexiste.html` | Error 404 | Status 404, página personalizada con diseño |
| `http://localhost:8080/?nombre=Juan&edad=20` | Query string | En el log: `Query: nombre=Juan&edad=20` |
| Formulario POST | Enviar datos | En el log: `Body: nombre=Juan`, respuesta 200 |

### 8.3 Verificar Compresión

1. Abrir DevTools (F12) → pestaña **Network**.
2. Recargar la página.
3. Click en cualquier request → **Headers**.
4. Verificar:
   - Response Headers: `Content-Encoding: gzip`
   - Response Headers: `Content-Type: text/html` (o `text/css`)

### 8.4 Correr el Tester de Estrés

```bash
cd testServer
dotnet run
```

**Salida esperada:**
```
🔥 Stress Test contra http://localhost:8080/
📊 Peticiones totales: 500
⚡ Concurrentes: 500
==================================================
  → Progreso: 50/500
  → Progreso: 100/500
  ...
==================================================
✅ Exitosas: 500
❌ Fallidas: 0
⏱️ Tiempo total: 1234 ms
🚀 Peticiones/segundo: 405.20
📉 Latencia promedio: 2.47 ms
El servidor atendio todas las peticiones!
```

### 8.5 Verificar Logs

Abrir el archivo `logs/2026-06-11.log`:

```
[18:30:15] IP: 127.0.0.1
  Método: GET
  Ruta: /
  Query: nombre=Juan&edad=20
--------------------------------------------------
[18:30:20] IP: 127.0.0.1
  Método: POST
  Ruta: /
  Body: nombre=Monica
--------------------------------------------------
```

### 8.6 Cerrar el Servidor

1. En la terminal del servidor, apretar **Ctrl+C**.
2. **Salida esperada:**
   ```
   🛑 Señal de detención recibida. Cerrando servidor...
   🔌 Socket listener detenido.
   ✅ Servidor cerrado correctamente.
   ```
3. Verificar que el puerto 8080 se liberó.

---

