namespace TagBites.Pipes;

public class NamedPipeConnectionLostException : Exception
{
    public NamedPipeConnectionLostException()
        : base("Connection lost.")
    { }
}
