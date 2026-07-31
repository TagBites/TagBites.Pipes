# Security policy

## Supported versions

| Version | Supported |
| ------- | --------- |
| 1.2.x   | yes       |
| 1.1.x   | no        |
| 1.0.x   | no        |

## Reporting a vulnerability

Report a suspected vulnerability through [private security advisories](https://github.com/TagBites/TagBites.Pipes/security/advisories/new) on GitHub. Do not open a public issue.

## Threat model

The library carries messages between two processes on one machine. The points below describe what the code does today, so that an application can decide what it still has to add.

### Any local process can open the pipe

`NamedPipeServer` creates the pipe without a `PipeSecurity` descriptor and without `PipeOptions.CurrentUserOnly`, so the operating system default decides who may connect. A pipe name is not a secret and can be guessed or enumerated. Treat a request as input from an untrusted source: the `Request` handler runs application code for every process that manages to connect, and the address and the message come straight from that process.

An application that must restrict callers has to run the pipe under its own access control, or place the process where no untrusted code runs.

### A failed request tells the client about the server

When a request handler throws, the server sends the exception type name and message to the client. The stack trace goes with them unless `IncludeExceptionStackTrace` is set to `false`. The type and the message are always sent and cannot be turned off, so a message built from a file path, a query or a connection string reaches the caller. A handler that talks to a client outside the trust boundary should catch its own exceptions and answer with a message it chose.

### No authentication and no encryption

The library does not identify the process on the other end and does not encrypt the stream. A named pipe stays inside one machine and does not cross the network, so traffic is exposed to code already running on that machine, and to nothing else.

### Resource limits

The server accepts as many connections as the operating system allows for one pipe name, and serves each on its own task. The library adds no limit of its own, no timeout on a request handler and no quota per caller. A local process that opens many connections can exhaust that budget.

There is also no limit on the size of a single message. A caller can declare a length of any size, and the server allocates it. This applies to every encoding version: a line of text is read to its end just as a length prefix is honoured.

`NamedPipeClientPool` bounds the connections it holds, so it limits the client side only.

### Request data is not validated

Addresses and messages pass through unchanged apart from encoding. The library does not restrict their length or content, so a handler receives whatever the caller sent.
