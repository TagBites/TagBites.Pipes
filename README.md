# TagBites.Pipes

[![Nuget](https://img.shields.io/nuget/v/TagBites.Pipes.svg)](https://www.nuget.org/packages/TagBites.Pipes/)
![.NET Standard 2.1](https://img.shields.io/badge/.NET%20Standard-2.1-512BD4)
[![License](https://img.shields.io/github/license/TagBites/TagBites.Pipes)](https://github.com/TagBites/TagBites.Pipes/blob/master/LICENSE)
[![Downloads](https://img.shields.io/nuget/dt/TagBites.Pipes.svg)](https://www.nuget.org/packages/TagBites.Pipes/)

**TagBites.Pipes sends requests between two processes on the same machine, over a named pipe.**
The server handles requests by address, and the client calls them and gets back the answer, or the exception the handler threw. A payload can be a string, a byte array, or a stream that never has to fit in memory.

```csharp
// server
using var server = new NamedPipeServer("my-pipe");
server.Request += (_, e) => e.Response = $"Hello, {e.Message}!";
server.Enabled = true;

// client
using var client = new NamedPipeClient("my-pipe");
await client.ConnectAsync();

await client.SendRequestAsync("greet", "world"); // "Hello, world!"
```

On top of `System.IO.Pipes` it adds what a request and response protocol needs: message boundaries, matching every answer to its question, and one task per connected client.

## Install

```
dotnet add package TagBites.Pipes
```

Targets `netstandard2.1`, has no dependencies and runs on Windows, Linux and macOS. A named pipe is local, so both processes live on the same machine.

## Usage

### Serving requests

The server accepts nothing until `Enabled` is set. `Address` says what to do and `Message` carries the payload.

```csharp
using var server = new NamedPipeServer("my-pipe");
server.Request += (_, e) => e.Response = e.Address switch
{
    "greet" => $"Hello, {e.Message}!",
    "time" => DateTime.Now.ToString("O"),
    _ => null
};
server.Enabled = true;
```

The handler runs on a thread pool thread with no synchronization context, so code that touches a user interface has to marshal itself.

To answer asynchronously, hand the server a task and set the response when it finishes.

```csharp
server.Request += (_, e) => e.ResultTask = HandleAsync(e);

static async Task HandleAsync(NamedPipeRequestEventArgs e)
{
    e.Response = await File.ReadAllTextAsync(e.Message);
}
```

### Sending requests

```csharp
using var client = new NamedPipeClient("my-pipe");
await client.ConnectAsync();

var greeting = await client.SendRequestAsync("greet", "world"); // "Hello, world!"
var time = await client.SendRequestAsync("time", "");
```

Every call also has a synchronous form, `Connect` and `SendRequest`. `Connect` waits 100 milliseconds by default and throws `TimeoutException` when no server answers. The overload takes a longer timeout and a cancellation token.

```csharp
await client.ConnectAsync(5000, CancellationToken.None);
```

### Sending from several threads

One client serves one request at a time. `NamedPipeClientPool` keeps a set of connections and hands them out, so several threads can send at once. A caller waits while every connection is busy.

```csharp
using var pool = new NamedPipeClientPool("my-pipe", 4);

var greeting = await pool.SendRequestAsync("greet", "world");
```

To keep one connection for a few requests in a row, take a link and dispose it when done.

```csharp
using (var link = await pool.GetConnectionAsync())
{
    await link.SendRequestAsync("open", "file.txt");
    await link.SendRequestAsync("read", "100");
}
```

### Large payloads

Sending megabytes as a string means base64 for anything binary, and a full copy of the payload on each side. Streams avoid both and work in constant memory, whatever the size.

Turn on `UseMessageStream` on the server, then read the request and write the answer as streams.

```csharp
using var server = new NamedPipeServer("my-pipe") { UseMessageStream = true };
server.Request += (_, e) => e.ResultTask = HandleAsync(e);
server.Enabled = true;

static async Task HandleAsync(NamedPipeRequestEventArgs e)
{
    var order = await JsonSerializer.DeserializeAsync<Order>(e.MessageStream);
    var receipt = Process(order);

    e.SetResponse(stream => JsonSerializer.SerializeAsync(stream, receipt));
}
```

```csharp
var receipt = await client.SendRequestAsync("submit",
    stream => JsonSerializer.SerializeAsync(stream, order),
    stream => JsonSerializer.DeserializeAsync<Receipt>(stream).AsTask());
```

For bytes already in memory there is a shorter form.

```csharp
var thumbnail = await client.SendBytesAsync("resize", File.ReadAllBytes("photo.jpg"));
```

Two rules come with the stream form. Read `MessageStream` inside the handler and not inside the response callback, because the request is consumed before the answer starts. And once the answer starts, a failure breaks the connection instead of reporting an error, the same way an HTTP response cannot be taken back.

Leave `UseMessageStream` off for handlers that only want `Message` as a string.

### State that lives with a connection

`Context` identifies the connection and carries a store for values that last as long as it does. Assigning `null` removes an entry, and `Disposing` fires when the client goes away.

```csharp
server.Request += (_, e) =>
{
    if (e.Address == "login")
        e.Context.Bag["user"] = e.Message;

    e.Response = e.Context.Bag["user"] as string ?? "anonymous";
};
```

### When something fails

An exception in the handler reaches the client as `NamedPipeServerRemoteException`, carrying the type name, the message and the stack trace as text.

```csharp
try
{
    await client.SendRequestAsync("read", "missing.txt");
}
catch (NamedPipeServerRemoteException e)
{
    Console.WriteLine(e.RemoteType);       // "System.IO.FileNotFoundException"
    Console.WriteLine(e.Message);          // the message from the server
    Console.WriteLine(e.RemoteStackTrace); // the stack trace from the server
}
```

Set `IncludeExceptionStackTrace` to `false` to keep the stack trace on the server. The type and the message still travel, so a message built from a path or a connection string reaches the caller either way.

A broken connection raises `NamedPipeConnectionLostException`. Such a request has an unknown outcome, because the server may have handled it before the connection died. The pool drops that connection, so the next request gets a fresh one.

## Use cases

- a desktop application talking to its own background service
- a command line tool driving a process that is already running
- splitting work into two processes, for example a plugin host with a different bitness
- moving large files or JSON documents between two local processes

## Why TagBites.Pipes?

- **Request and response, not a raw stream** - message boundaries and answer matching are handled.
- **Large payloads in constant memory** - stream a request and a response instead of building strings.
- **A failed handler comes back as an exception** - with the type, the message and the stack trace from the other process.
- **Runs anywhere .NET runs** - Windows, Linux and macOS, with no dependencies.

## Links

- [Changelog](https://github.com/TagBites/TagBites.Pipes/blob/master/CHANGELOG.md)
- [Security policy](https://github.com/TagBites/TagBites.Pipes/blob/master/SECURITY.md)
- [License (MIT)](https://github.com/TagBites/TagBites.Pipes/blob/master/LICENSE)
