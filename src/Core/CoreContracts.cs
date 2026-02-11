namespace tero.session.src.Core;

public interface IJoinableSession<TSession>
{
    public Result<TSession, Error> AddPlayer(Guid userId);
}

public interface ICleanuppable<TSession>
{
    public TSession Cleanup(Guid userId);
}