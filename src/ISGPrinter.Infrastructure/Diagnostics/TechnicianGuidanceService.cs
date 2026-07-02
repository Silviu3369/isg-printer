using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.Infrastructure.Diagnostics;

public sealed class TechnicianGuidanceService : ITechnicianGuidanceService
{
    public IReadOnlyList<string> BuildRecommendedSteps(PrinterDiagnosticResult diagnosticResult)
    {
        var steps = new List<string>();

        foreach (var check in diagnosticResult.Checks)
        {
            if (check.Status is DiagnosticStatus.Ok or DiagnosticStatus.Unknown)
            {
                continue;
            }

            switch (check.Name)
            {
                case "Administrator":
                    steps.Add("Restart ISG Printer as administrator before running technician actions.");
                    break;
                case "Print Spooler":
                    steps.Add("Check the Print Spooler service in Services. Start it if stopped, or restart it manually if jobs are stuck.");
                    break;
                case "Installed locally":
                    steps.Add("Install the printer from its UNC path, then rerun diagnostics.");
                    break;
                case "Default printer":
                    steps.Add("Set the printer as default for the current Windows account if this is the user's intended printer.");
                    break;
                case "Driver":
                    steps.Add("Verify that the print driver is deployed and allowed by Point and Print policy.");
                    break;
                case "Queue":
                    steps.Add("Inspect the printer queue manually. Clear or restart only after confirming with the user and checking active jobs.");
                    break;
                case "Ping":
                    steps.Add("The printer did not respond to ping. Confirm the IP/hostname, that the device is powered on and on the network, and that ICMP is not blocked.");
                    break;
                case "Print ports":
                    steps.Add("No raw print port (9100/515/631) is reachable. Check the network path, VLAN, and firewall rules between this PC and the printer.");
                    break;
                case "Print errors":
                    steps.Add("Review recent Print Spooler errors in Event Viewer (Applications and Services Logs - Microsoft - Windows - PrintService).");
                    break;
                case "Supplies":
                    steps.Add("Toner/ink is low or empty. Replace the affected cartridge, or confirm the SNMP profile if the level could not be read.");
                    break;
                default:
                    steps.Add($"Review check '{check.Name}': {check.Message}");
                    break;
            }
        }

        if (steps.Count == 0)
        {
            steps.Add("No technician action is currently recommended by the basic diagnostic checks.");
        }

        return steps;
    }
}
