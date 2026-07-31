# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-08-01

### Added

- Encoding version 3, which carries a message as a length prefix and the raw bytes instead of an escaped line of text. Client and server agree on it when the connection opens and fall back to version 2 against an older peer.
- Streaming requests and responses, through `NamedPipeClient.SendRequestAsync(address, writeMessage, readResponse)` on one side and `NamedPipeServer.UseMessageStream` with `NamedPipeRequestEventArgs.SetResponse` on the other. A large payload no longer has to fit in memory. Both need version 3 and throw `NotSupportedException` against an older peer.
- `NamedPipeClient.SendBytesAsync`, which sends and receives bytes without base64.
- `NamedPipeServer.IncludeExceptionStackTrace`, which keeps the stack trace of a failed request off the wire. The exception type and message are always sent.
- `IsDisposed` on the client, the server, the pool and the pool link.
- XML documentation for the public API.

### Changed

- Setting `NamedPipeServer.Enabled` to `false` closes the connections that are still open. A client in the middle of a request gets `NamedPipeConnectionLostException`.
- `NamedPipeClientPool.Dispose()` waits for the requests that are still running. It used to return at once and leave the pool broken, so every later call ended in `NullReferenceException`.
- An address that starts with `--` is rejected with `ArgumentException`, because the prefix is reserved for internal commands.
- A connection that breaks while a request is in flight raises `NamedPipeConnectionLostException`. Earlier versions reported `NotSupportedException` for an unknown server response.
- `NamedPipeClient.IsConnected` is `false` after `Dispose()`.

### Fixed

- The library runs on Linux and macOS. Earlier versions used pipe calls that exist on Windows only, so a server on another platform never started listening and every client timed out.
- A disabled server no longer leaves behind a pipe that accepts connections and never answers, which also made a restart hang.
- The server no longer raises `Request` with an empty address when a client disconnects.
- Client and server agree on the encoding again after a reconnect. A reconnected client could otherwise send in a different version than the server expected.
- `NamedPipeConnectionBag` is safe for concurrent access.
- `NamedPipeClient` and `NamedPipeServer` reject a `null` pipe name, and `NamedPipeClientPool` rejects a size below one, which used to hang instead.

## [1.1.1]

### Fixed

- The negotiated encoding version is applied to every connection in `NamedPipeClientPool`.

## [1.1.0]

### Added

- `NamedPipeClientPool` keeps a bounded set of reusable connections.
- Encoding version 1 is available for a peer that predates version 2, through `NamedPipeServer.SupportLegacyEncoding`. Client and server negotiate the version when the connection opens.

## [1.0.1]

### Fixed

- A `null` message no longer fails the request.

## [1.0.0]

### Added

- First release: `NamedPipeServer` and `NamedPipeClient` with a request and response API over named pipes.

[1.2.0]: https://github.com/TagBites/TagBites.Pipes/compare/1.1.1...1.2.0
[1.1.1]: https://github.com/TagBites/TagBites.Pipes/compare/1.1.0...1.1.1
[1.1.0]: https://github.com/TagBites/TagBites.Pipes/compare/1.0.1...1.1.0
[1.0.1]: https://github.com/TagBites/TagBites.Pipes/compare/1.0.0...1.0.1
[1.0.0]: https://github.com/TagBites/TagBites.Pipes/releases/tag/1.0.0
