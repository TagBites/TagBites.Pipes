# TagBites.Pipes

C# library for simple inter-process communication using named pipes.

## Sync Example

Server:
```csharp
var sn = new NamedPipeServer("my-pipe-name");
sn.Request += (_, r) =>
{
    var clientId = r.Context.Id;
            
    Console.WriteLine($"{clientId}: {r.Address}, {r.Message}");

    r.Response = "Hello! :)";
};
```

Client:
```csharp
using var client = new NamedPipeClient("my-pipe-name");
client.Connect();

var response = client.SendRequest("command-name-or-address", "Hello?");
```

## Async Example

Server:
```csharp
var sn = new NamedPipeServer("my-pipe-name");
sn.Request += (_, r) => r.ResultTask = ProcessAsync(r);

async Task ProcessAsync(NamedPipeRequestEventArgs r)
{
    await Task.Delay(100);

    var clientId = r.Context.Id;
            
    Console.WriteLine($"{clientId}: {r.Address}, {r.Message}");

    r.Response = "Hello! :)";
}
```

Client:
```csharp
using var client = new NamedPipeClient("my-pipe-name");
await client.ConnectAsync();

var response = await client.SendRequestAsync("command-name-or-address", "Hello?");
```

