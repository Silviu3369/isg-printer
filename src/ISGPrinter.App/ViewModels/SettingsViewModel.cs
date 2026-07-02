using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ISGPrinter.Application.Abstractions;
using ISGPrinter.Domain.Enums;
using ISGPrinter.Domain.Models;

namespace ISGPrinter.App.ViewModels;

public sealed partial class SettingsViewModel(
    ISettingsService settingsService,
    ISecretStore secretStore,
    IPrinterDiscoveryService printerDiscoveryService) : ObservableObject
{
    private AppSettings currentSettings = new();
    private SnmpProfile profile = SnmpProfile.DefaultV2();

    public ObservableCollection<string> KnownPrintServers { get; } = [];

    public SnmpVersion[] SnmpVersions { get; } = Enum.GetValues<SnmpVersion>();

    public SnmpAuthenticationProtocol[] AuthProtocols { get; } = Enum.GetValues<SnmpAuthenticationProtocol>();

    public SnmpPrivacyProtocol[] PrivacyProtocols { get; } = Enum.GetValues<SnmpPrivacyProtocol>();

    [ObservableProperty]
    private string settingsPath = settingsService.SettingsPath;

    [ObservableProperty]
    private string statusText = "Ready.";

    [ObservableProperty]
    private bool enableSnmp = true;

    [ObservableProperty]
    private bool enableActiveDirectoryDiscovery = true;

    [ObservableProperty]
    private int snmpTimeoutMs = 2500;

    [ObservableProperty]
    private int networkTimeoutMs = 2000;

    [ObservableProperty]
    private string newServerName = string.Empty;

    [ObservableProperty]
    private string profileName = "Default";

    [ObservableProperty]
    private SnmpVersion snmpVersion = SnmpVersion.V2C;

    [ObservableProperty]
    private string community = string.Empty;

    [ObservableProperty]
    private string userName = string.Empty;

    [ObservableProperty]
    private SnmpAuthenticationProtocol authProtocol = SnmpAuthenticationProtocol.None;

    [ObservableProperty]
    private string authPassword = string.Empty;

    [ObservableProperty]
    private SnmpPrivacyProtocol privacyProtocol = SnmpPrivacyProtocol.None;

    [ObservableProperty]
    private string privacyPassword = string.Empty;

    public bool IsV2c => SnmpVersion == SnmpVersion.V2C;

    public bool IsV3 => SnmpVersion == SnmpVersion.V3;

    partial void OnSnmpVersionChanged(SnmpVersion value)
    {
        OnPropertyChanged(nameof(IsV2c));
        OnPropertyChanged(nameof(IsV3));
    }

    public async Task LoadAsync()
    {
        currentSettings = await settingsService.LoadAsync(CancellationToken.None);

        KnownPrintServers.Clear();
        foreach (var server in currentSettings.KnownPrintServers)
        {
            KnownPrintServers.Add(server);
        }

        EnableSnmp = currentSettings.EnableSnmp;
        EnableActiveDirectoryDiscovery = currentSettings.EnableActiveDirectoryDiscovery;
        SnmpTimeoutMs = currentSettings.SnmpTimeoutMs;
        NetworkTimeoutMs = currentSettings.NetworkTimeoutMs;

        profile = currentSettings.SnmpProfiles.FirstOrDefault(p =>
                      string.Equals(p.Name, currentSettings.DefaultSnmpProfile, StringComparison.OrdinalIgnoreCase))
                  ?? currentSettings.SnmpProfiles.FirstOrDefault()
                  ?? SnmpProfile.DefaultV2();

        ProfileName = profile.Name;
        SnmpVersion = profile.Version;
        UserName = profile.UserName;
        AuthProtocol = profile.AuthenticationProtocol;
        PrivacyProtocol = profile.PrivacyProtocol;

        Community = await secretStore.GetAsync(profile.CommunitySecretName, CancellationToken.None);
        AuthPassword = await secretStore.GetAsync(profile.AuthSecretName, CancellationToken.None);
        PrivacyPassword = await secretStore.GetAsync(profile.PrivacySecretName, CancellationToken.None);

        StatusText = "Settings loaded.";
    }

    [RelayCommand]
    private async Task AddServerAsync()
    {
        var name = NewServerName.Trim().TrimStart('\\');
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            StatusText = $"Validating {name}...";
            var validation = await printerDiscoveryService.DiscoverServerPrintersAsync(name, CancellationToken.None);
            if (validation.ServerErrors.Count > 0)
            {
                StatusText = $"Could not add {name}: {validation.ServerErrors[0].Message}";
                return;
            }

            if (validation.Printers.Count == 0)
            {
                StatusText = $"Could not add {name}: the server responded, but no shared printers were found.";
                return;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Could not add server: {ex.Message}";
            return;
        }

        if (!KnownPrintServers.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            KnownPrintServers.Add(name);
        }

        NewServerName = string.Empty;
        StatusText = $"Validated {name}. Click Save to use it in this session.";
    }

    [RelayCommand]
    private void RemoveServer(string? server)
    {
        if (!string.IsNullOrWhiteSpace(server))
        {
            KnownPrintServers.Remove(server);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            profile.Name = string.IsNullOrWhiteSpace(ProfileName) ? "Default" : ProfileName.Trim();
            profile.Version = SnmpVersion;
            profile.UserName = UserName.Trim();
            profile.AuthenticationProtocol = AuthProtocol;
            profile.PrivacyProtocol = PrivacyProtocol;
            SnmpTimeoutMs = Math.Clamp(SnmpTimeoutMs, 500, 15000);
            NetworkTimeoutMs = Math.Clamp(NetworkTimeoutMs, 500, 30000);

            if (string.IsNullOrWhiteSpace(profile.CommunitySecretName))
            {
                profile.CommunitySecretName = "snmp-community";
            }

            if (string.IsNullOrWhiteSpace(profile.AuthSecretName))
            {
                profile.AuthSecretName = "snmp-auth";
            }

            if (string.IsNullOrWhiteSpace(profile.PrivacySecretName))
            {
                profile.PrivacySecretName = "snmp-privacy";
            }

            await secretStore.SetAsync(profile.CommunitySecretName, Community, CancellationToken.None);
            await secretStore.SetAsync(profile.AuthSecretName, AuthPassword, CancellationToken.None);
            await secretStore.SetAsync(profile.PrivacySecretName, PrivacyPassword, CancellationToken.None);

            currentSettings.SnmpProfiles = [profile];
            currentSettings.DefaultSnmpProfile = profile.Name;
            currentSettings.EnableSnmp = EnableSnmp;
            currentSettings.EnableActiveDirectoryDiscovery = EnableActiveDirectoryDiscovery;
            currentSettings.SnmpTimeoutMs = SnmpTimeoutMs;
            currentSettings.NetworkTimeoutMs = NetworkTimeoutMs;
            currentSettings.KnownPrintServers = KnownPrintServers.ToList();

            var result = await settingsService.SaveAsync(currentSettings, CancellationToken.None);
            WeakReferenceMessenger.Default.Send(new SessionSettingsChangedMessage());
            StatusText = BuildSaveStatus(result.Message);
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "Could not save settings. Run ISG Printer as administrator and try again.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not save settings: {ex.Message}";
        }
    }

    private string BuildSaveStatus(string baseMessage)
    {
        if (!EnableSnmp)
        {
            return baseMessage;
        }

        if (SnmpVersion == SnmpVersion.V2C && string.IsNullOrWhiteSpace(Community))
        {
            return $"{baseMessage} SNMP v2c reads need a community for this session.";
        }

        if (SnmpVersion == SnmpVersion.V3 && string.IsNullOrWhiteSpace(UserName))
        {
            return $"{baseMessage} SNMP v3 reads need a user name for this session.";
        }

        if (SnmpVersion == SnmpVersion.V3
            && AuthProtocol != SnmpAuthenticationProtocol.None
            && string.IsNullOrWhiteSpace(AuthPassword))
        {
            return $"{baseMessage} SNMP v3 auth reads need an authentication password for this session.";
        }

        if (SnmpVersion == SnmpVersion.V3
            && PrivacyProtocol != SnmpPrivacyProtocol.None
            && string.IsNullOrWhiteSpace(PrivacyPassword))
        {
            return $"{baseMessage} SNMP v3 privacy reads need a privacy password for this session.";
        }

        return baseMessage;
    }
}
