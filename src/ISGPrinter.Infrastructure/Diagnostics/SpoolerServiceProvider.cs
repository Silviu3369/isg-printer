using System.ComponentModel;
using System.ServiceProcess;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Diagnostics;

public sealed class SpoolerServiceProvider : ISpoolerServiceProvider
{
    private const string SpoolerServiceName = "Spooler";
    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(20);

    public Task<PrinterDiagnosticCheck> CheckSpoolerAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var controller = new ServiceController(SpoolerServiceName);
                var status = controller.Status;

                return new PrinterDiagnosticCheck
                {
                    Name = "Print Spooler",
                    Status = status == ServiceControllerStatus.Running ? DiagnosticStatus.Ok : DiagnosticStatus.Error,
                    Message = status == ServiceControllerStatus.Running
                        ? "Print Spooler is running."
                        : $"Print Spooler is {status}."
                };
            }
            catch (Exception ex)
            {
                return new PrinterDiagnosticCheck
                {
                    Name = "Print Spooler",
                    Status = DiagnosticStatus.Error,
                    Message = "Print Spooler could not be checked.",
                    TechnicalDetails = ex.Message
                };
            }
        }, cancellationToken);

    public Task<OperationResult> RestartSpoolerAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var controller = new ServiceController(SpoolerServiceName);

                // Dependent services (e.g. Fax) must be stopped before the
                // spooler will stop; remember which were running to restart them.
                var runningDependents = controller.DependentServices
                    .Where(dependent => dependent.Status != ServiceControllerStatus.Stopped)
                    .ToList();

                foreach (var dependent in runningDependents)
                {
                    dependent.Stop();
                    dependent.WaitForStatus(ServiceControllerStatus.Stopped, TransitionTimeout);
                }

                if (controller.Status != ServiceControllerStatus.Stopped)
                {
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TransitionTimeout);
                }

                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TransitionTimeout);

                foreach (var dependent in runningDependents)
                {
                    try
                    {
                        dependent.Start();
                    }
                    catch
                    {
                        // Best effort — a dependent failing to come back must not
                        // mask a successful spooler restart.
                    }
                }

                return OperationResult.Ok("Print Spooler restarted.");
            }
            catch (System.ServiceProcess.TimeoutException ex)
            {
                return OperationResult.Fail(OperationStatus.Unavailable, "The Print Spooler did not restart in time.", ex.Message);
            }
            catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
            {
                return OperationResult.Fail(OperationStatus.RequiresAdmin, "Restarting the Print Spooler needs administrator rights. Run ISG Printer as administrator.", ex.Message);
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(OperationStatus.Failed, "Could not restart the Print Spooler.", ex.Message);
            }
        }, cancellationToken);
}
