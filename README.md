# Servidor Web Concurrente en C# (Sockets de Bajo Nivel)

> Proyecto Final - Redes  
> Desarrollado con .NET 8, sockets TCP nativos y programación asíncrona.

---

## 📋 Tabla de Contenidos

1. [Descripción](#-descripción)
2. [Arquitectura y Decisiones Técnicas](#-arquitectura-y-decisiones-técnicas)
3. [Requisitos Cumplidos](#-requisitos-cumplidos)
4. [Estructura del Proyecto](#-estructura-del-proyecto)
5. [Cómo Ejecutar](#-cómo-ejecutar)
6. [Pruebas y Validación](#-pruebas-y-validación)
7. [Justificación Técnica: Asincronía vs. Threads](#-justificación-técnica-asincronía-vs-threads)
8. [Diagrama de Flujo](#-diagrama-de-flujo)
9. [Autor](#-autor)

---

## 📝 Descripción

Este proyecto implementa un **servidor HTTP/1.1 desde cero**, operando directamente sobre la **capa de transporte (TCP)** mediante sockets nativos del sistema operativo. No se utilizó ningún framework web de alto nivel (como ASP.NET, Kestrel o HttpListener); en su lugar, se parsean manualmente las solicitudes HTTP a partir de los bytes recibidos por el socket.

El servidor es **concurrente y asíncrono**, capaz de atender un número indefinido de conexiones simultáneas utilizando un pool reducido de hilos gracias al modelo `async/await` de .NET.

---

## 🏗️ Arquitectura y Decisiones Técnicas

### 1. Capa de Transporte
- Se utiliza `System.Net.Sockets.Socket` con `AddressFamily.InterNetwork` (IPv4) y `SocketType.Stream` (TCP).
- El socket escucha en el puerto configurado externamente (`appsettings.json`).

### 2. Parseo Manual de HTTP
- Se leen los bytes crudos del `NetworkStream`.
- Se separan los headers por el delimitador `\r\n`.
- Se extraen: **Método**, **Ruta**, **Query String** y **Headers**.
- Para POST, se lee el `Content-Length` y se consume el body de forma controlada.

### 3. Concurrencia
- Modelo **asíncrono (async/await)** con fire-and-forget (`_ = HandleClientAsync()`).
- No se crea un hilo por cliente. Se utiliza el **Thread Pool** de .NET, que reutiliza un puñado de hilos para miles de conexiones.

### 4. Compresión
- Todas las respuestas se comprimen con **GZip** (`System.IO.Compression.GZipStream`) antes de enviarse.
- El header `Content-Encoding: gzip` informa al navegador que debe descomprimir.

### 5. Configuración Externa
- El puerto y las rutas de carpetas se leen desde `appsettings.json`.
- Se utiliza `Microsoft.Extensions.Configuration` (herramienta nativa de .NET) con inyección de secciones (`GetSection<ServerConfig>()`).
- **No se parseó texto plano manualmente**; se usó el binder de configuración de .NET.

### 6. Logging
- Un archivo `.log` por día (`yyyy-MM-dd.log`).
- Registra: **IP de origen**, **timestamp**, **método HTTP**, **ruta**, **query string** y **body de POST**.
- Acceso thread-safe mediante `lock` para evitar corrupción de archivo en escrituras concurrentes.

---

## ✅ Requisitos Cumplidos

| # | Requisito | Estado | Implementación |
|---|-----------|--------|----------------|
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

---

## 📁 Estructura del Proyecto

```
serverC-/
├── .gitignore                  # Excluye bin/, obj/, logs temporales
├── appsettings.json             # Configuración externa (puerto, carpetas)
├── Program.cs                   # Código principal del servidor
├── serverC-.csproj              # Archivo de proyecto .NET 8
├── README.md                    # Este archivo
├── logs/                        # Archivos de log generados automáticamente
│   └── 2026-06-05.log
└── wwwroot/                     # Archivos estáticos servidos por el servidor
    ├── index.html               # Página por defecto
    ├── 404.html                 # Página de error personalizada
    └── style.css                # Hoja de estilos
```

---

## 🚀 Cómo Ejecutar

### Prerrequisitos
- [.NET SDK 8.0](https://dotnet.microsoft.com/download) o superior.
- Visual Studio Code (opcional) con la extensión **C# Dev Kit**.

### Pasos

```bash
# 1. Clonar o navegar al directorio del proyecto
cd serverC-

# 2. Restaurar paquetes NuGet
dotnet restore

# 3. Compilar y ejecutar
dotnet run
```

### Salida esperada en consola

```
 Servidor iniciando en puerto: 8080
 Carpeta de archivos: C:\...\serverC-\wwwroot
 Logs en: C:\...\serverC-\logs
 Presiona Ctrl+C para detener.
```

### Acceder al servidor
Abrir el navegador en: `http://localhost:8080`

---

## 🧪 Pruebas y Validación

### 1. GET - Página por defecto
```
GET http://localhost:8080/
```
- **Esperado:** Carga `index.html` con el formulario.

### 2. GET - Archivo estático (CSS)
```
GET http://localhost:8080/style.css
```
- **Esperado:** Código CSS con `Content-Type: text/css` y `Content-Encoding: gzip`.

### 3. GET - Error 404 personalizado
```
GET http://localhost:8080/noexiste.html
```
- **Esperado:** Página negra con "404" en rojo grande y código HTTP 404.

### 4. GET - Query String
```
GET http://localhost:8080/?nombre=Juan&edad=20
```
- **Esperado:** La página carga normalmente. En el log aparece: `Query: nombre=Juan&edad=20`.

### 5. POST - Logueo de datos
```
POST http://localhost:8080/
Body: nombre=Monica
```
- **Esperado:** Respuesta "Datos POST recibidos y logueados correctamente". En el log: `Body: nombre=Monica`.

### 6. Verificación de compresión (DevTools)
- Abrir `F12` → Network → Headers.
- Confirmar `Content-Encoding: gzip` en la respuesta.

---

## ⚖️ Justificación Técnica: Asincronía vs. Threads

Se eligió **programación asíncrona (`async/await`)** sobre el modelo tradicional de **un hilo por conexión** por tres razones técnicas fundamentales:

### 1. Consumo de Memoria
Cada hilo del sistema operativo reserva aproximadamente **1 MB de RAM** para su stack de ejecución. Si el servidor recibe 10.000 conexiones simultáneas, el modelo de hilos dedicados consumiría **~10 GB de RAM** únicamente en la estructura de los hilos, antes de procesar un solo byte de tráfico. Con asincronía, el **Thread Pool** de .NET reutiliza un puñado de hilos (típicamente 20-50) para atender miles de conexiones.

### 2. Sobrecarga del Procesador (Context Switching)
Un procesador tiene un número limitado de núcleos físicos. Si se crean miles de hilos, el sistema operativo debe alternar constantemente entre ellos (guardar y restaurar registros, stacks, etc.). Este **context switching** consume ciclos de CPU en tareas administrativas, restando rendimiento real al servidor. La asincronía evita esta sobrecarga porque los hilos no se bloquean esperando I/O.

### 3. Naturaleza I/O Bound del Servidor Web
Un servidor web pasa la mayor parte de su ciclo de vida en **espera**: esperando paquetes de red por el socket, o esperando que el disco duro termine de leer un archivo estático. En el modelo de hilos tradicional, el hilo se bloquea y queda inutilizado durante esa espera. La asincronía delega esa espera al hardware (tarjeta de red o controlador de disco), permitiendo que el hilo se libere y atienda otra petición. Cuando el hardware notifica que la operación terminó, el hilo retoma la tarea desde el Thread Pool.

> **Conclusión:** La asincronía es la elección óptima para servidores web de alto rendimiento porque maximiza el uso de recursos en escenarios dominados por operaciones de entrada/salida.

---

## 🔄 Diagrama de Flujo

```
┌─────────────────┐
│   appsettings.json   │
│  (Puerto, WebRoot)   │
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

## 👤 Autor
Melgarejo Monica

Proyecto desarrollado para la materia **Redes y Comunicaciones** - IFTS 2026.

---

## 📚 Referencias

- Microsoft Docs: [Async Programming in C#](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/)
- Microsoft Docs: [Configuration in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration)

