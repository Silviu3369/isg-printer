namespace ISGPrinter.Domain.Enums;

public enum DiagnosticStatus
{
    Ok,
    Warning,
    Error,
    Unknown
}

public enum OperationStatus
{
    Success,
    Failed,
    AlreadyExists,
    RequiresAdmin,
    InvalidInput,
    NotFound,
    BlockedByPolicy,
    Unavailable,
    Unknown
}

public enum PrinterOnlineState
{
    Unknown,
    Online,
    Offline,
    Warning,
    Error
}

public enum PrinterInstallState
{
    Unknown,
    NotInstalled,
    Installed,
    AlreadyInstalled,
    Failed
}

public enum TonerLevelState
{
    Unknown,
    Ok,
    Low,
    Critical,
    Empty,
    NotSupported,
    SnmpUnavailable
}

public enum PrinterConnectionType
{
    Unknown,
    Local,
    Network,
    Shared,
    Virtual
}

public enum SnmpVersion
{
    V2C,
    V3
}

public enum SnmpAuthenticationProtocol
{
    None,
    Md5,
    Sha1,
    Sha256,
    Sha384,
    Sha512
}

public enum SnmpPrivacyProtocol
{
    None,
    Des,
    Aes128,
    Aes192,
    Aes256
}
