namespace MDictindle;

public enum ExitCodes
{
    Ok ,
    ReadFilePathFailed,
    FileNotExist,
    PathTooLong,
    UnauthorizedAccess,
    // ReSharper disable once InconsistentNaming
    IO,
    FileNameWithAt,

    Unknown
}