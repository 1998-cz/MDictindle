namespace MDictindle.Step;

public abstract class AbsStep
{
    public abstract string Description { get; }
    public abstract bool EnableAsync { get; }
    public abstract void Do(DictManager manager, TextWriter logger);
    public abstract Task DoAsync(DictManager manager, TextWriter logger);

    public async Task AutoDo(DictManager manager, TextWriter logger)
    {
        if (EnableAsync)
        {
            await DoAsync(manager, logger);
        }
        else
        {
            Do(manager, logger);
        }
    }
}