using ISGPrinter.Domain.Enums;

namespace ISGPrinter.Domain.Models;

public sealed class OperationResult
{
    public bool Success { get; set; }

    public OperationStatus Status { get; set; }

    public string Message { get; set; } = string.Empty;

    public string TechnicalDetails { get; set; } = string.Empty;

    public static OperationResult Ok(string message = "Operation completed.") =>
        new()
        {
            Success = true,
            Status = OperationStatus.Success,
            Message = message
        };

    public static OperationResult Fail(OperationStatus status, string message, string technicalDetails = "") =>
        new()
        {
            Success = false,
            Status = status,
            Message = message,
            TechnicalDetails = technicalDetails
        };
}

public sealed class PrinterServerError
{
    public string ServerName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class PrinterDiscoveryResult
{
    public List<PrinterDevice> Printers { get; set; } = [];

    public List<PrinterServerError> ServerErrors { get; set; } = [];

    public int ServersQueried { get; set; }
}

public sealed class PrinterInstallResult
{
    public bool Success { get; set; }

    public PrinterInstallState InstallState { get; set; } = PrinterInstallState.Unknown;

    public OperationStatus Status { get; set; } = OperationStatus.Unknown;

    public string Message { get; set; } = string.Empty;

    public string TechnicalDetails { get; set; } = string.Empty;
}

public sealed class PrinterDiagnosticCheck
{
    public string Name { get; set; } = string.Empty;

    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Unknown;

    public string Message { get; set; } = string.Empty;

    public string TechnicalDetails { get; set; } = string.Empty;
}

public sealed class PrinterDiagnosticResult
{
    public string PrinterName { get; set; } = string.Empty;

    public string UncPath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public List<PrinterDiagnosticCheck> Checks { get; set; } = [];

    public List<string> RecommendedSteps { get; set; } = [];

    public DiagnosticStatus OverallStatus { get; set; } = DiagnosticStatus.Unknown;
}

public sealed class NetworkProbeResult
{
    public bool Success { get; set; }

    public string Target { get; set; } = string.Empty;

    public int? Port { get; set; }

    public TimeSpan Elapsed { get; set; }

    public string Message { get; set; } = string.Empty;

    public string TechnicalDetails { get; set; } = string.Empty;
}

public sealed class SnmpResult<T>
{
    public bool Success { get; set; }

    public T? Value { get; set; }

    public string Message { get; set; } = string.Empty;

    public string TechnicalDetails { get; set; } = string.Empty;

    public static SnmpResult<T> Unavailable(string message) =>
        new()
        {
            Success = false,
            Message = message
        };
}
