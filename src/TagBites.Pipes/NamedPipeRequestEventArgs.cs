namespace TagBites.Pipes;

[PublicAPI]
public class NamedPipeRequestEventArgs : EventArgs
{
    public NamedPipeConnectionContext Context { get; }
    public string Address { get; }
    public string Message { get; }
    public string? Response { get; set; }
    public Task? ResultTask { get; set; }

    public NamedPipeRequestEventArgs(NamedPipeConnectionContext context, string address, string message)
    {
        Context = context;
        Address = address;
        Message = message;
    }
}
