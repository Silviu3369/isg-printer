# ISG Printer - Blueprint v3 Technician

Data: 2026-06-11
Autor proiect: Ionel-Silviu Ghimpau
Aplicatie: ISG Printer
Tip: Windows desktop corporate printer support tool pentru tehnicieni IT

## 1. Decizie finala de produs

ISG Printer este o aplicatie pentru tehnicieni IT, helpdesk si system administrators.

Aplicatia nu este destinata utilizatorilor normali.
Nu exista User Mode si Technician Mode.
Aplicatia ruleaza direct ca Technician Tool.

Reguli principale:

- aplicatia cere drepturi de administrator la pornire;
- toate actiunile sunt executate de contul Windows care ruleaza procesul elevated;
- aplicatia afiseaza clar contul curent si daca procesul este elevated;
- aplicatia nu executa repair automat;
- aplicatia ofera diagnostic, recomandari si pasi pentru tehnician;
- SNMP v2c si SNMP v3 sunt functii importante si trebuie implementate modular;
- actiunile riscante trebuie explicate clar, chiar daca aplicatia ruleaza ca admin.

## 2. Target tehnic

Recomandat pentru proiect nou:

- .NET 10 LTS;
- WPF;
- C#;
- MVVM;
- CommunityToolkit.Mvvm;
- Dependency Injection;
- Serilog;
- JSON configuration;
- Clean Architecture.

Nota de mediu:

Pe masina curenta este instalat doar .NET SDK 8.0.421.
Inainte de build trebuie aleasa una dintre variante:

1. instalam .NET 10 SDK si targetam `net10.0-windows`;
2. pornim temporar pe `net8.0-windows`, cu plan de upgrade la .NET 10.

Recomandarea ramane varianta 1 pentru aplicatie noua in 2026.

## 3. Elevation model

Aplicatia trebuie sa porneasca direct elevated.

In proiectul WPF se foloseste manifest cu:

```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

Consecinte importante:

- daca tehnicianul ruleaza aplicatia cu alt cont admin, default printer si printer connections se pot aplica acelui cont, nu userului normal;
- raportul trebuie sa includa `Running as`;
- UI-ul trebuie sa afiseze `Administrator: Yes/No`;
- aplicatia trebuie sa arate clar pentru ce cont face operatiile.

Pentru v1, regula este simpla:

Toate actiunile se aplica pentru contul curent elevated.

Functia avansata "configure printer for another logged-in user" nu intra in v1.

## 4. Functionalitati incluse

### 4.1 Application Shell

Aplicatia nu are Dashboard.
Porneste direct in pagina `Printers`.

Header-ul aplicatiei afiseaza permanent:

- search printer/server;
- computer name;
- running user;
- domain/workgroup;
- elevated/admin state.

Status bar-ul afiseaza permanent:

- default printer;
- known print servers count;
- last refresh;
- active SNMP profile;
- warnings importante.

Navigatie:

- Printers;
- Local Printers;
- Diagnostics;
- Reports;
- Settings;
- About.

### 4.2 Printers

Pagina principala.
Afiseaza imprimante disponibile pe servere cunoscute sau descoperite:

- printer name;
- share name;
- UNC path;
- server;
- location;
- driver;
- IP, daca este cunoscut;
- installed locally;
- default;
- toner;
- status;

Actiuni:

- install;
- set default;
- print test page;
- details;
- diagnose;
- copy UNC path;
- open web interface, daca IP-ul este cunoscut.

### 4.3 Local Printers

Afiseaza imprimantele instalate local:

- name;
- default;
- driver;
- port;
- server/UNC;
- connection type;
- status.
- queue state.

Actiuni:

- refresh;
- set default;
- print test page;
- details;
- diagnose;
- copy ticket info.

### 4.4 Printer Details

Afiseaza:

- name;
- share name;
- UNC path;
- server name;
- location;
- driver;
- port;
- IP address;
- model;
- manufacturer;
- serial number;
- firmware version;
- page counter;
- toner;
- queue status;
- installed locally;
- default;
- print server reachable;
- SNMP status;
- web interface link.

### 4.5 Diagnostics

Diagnostic first, fara repair automat.

Verificari:

1. aplicatia ruleaza elevated;
2. print spooler exista;
3. print spooler ruleaza;
4. imprimanta este instalata local;
5. imprimanta este default;
6. print server DNS resolves;
7. print server reachable;
8. UNC path valid;
9. UNC path reachable;
10. printer IP known, unde este posibil;
11. printer IP ping, daca IP-ul este cunoscut;
12. TCP 9100 reachable, daca IP-ul este cunoscut;
13. TCP 631 reachable, daca IP-ul este cunoscut;
14. SNMP reachable, daca este configurat;
15. toner readable, daca SNMP raspunde;
16. hardware info readable, daca SNMP raspunde;
17. queue status readable;
18. driver name present;
19. test page capability.

Rezultat:

- OK;
- Warning;
- Error;
- Unknown.

### 4.6 Technician Guidance

In loc de Repair Module, aplicatia ofera recomandari pentru tehnician.

Exemple:

- "Print spooler is stopped. Recommended technician action: start or restart Print Spooler from Services."
- "Printer is not installed. Recommended action: install from UNC path."
- "Server is unreachable. Recommended action: verify VPN, DNS, server availability."
- "Driver install is blocked by policy. Recommended action: check Point and Print policy or deploy driver via IT management."
- "SNMP is unavailable. Recommended action: verify SNMP profile, firewall, printer configuration."

Aplicatia poate afisa comenzi PowerShell utile pentru tehnician, dar nu le executa automat ca repair.

Exemplu:

```powershell
Restart-Service -Name Spooler
```

Aceasta comanda este doar instructiune afisata/copiat, nu actiune automata in v1.

### 4.7 Install Printer

Instalarea imprimantei share-uite se face prin UNC:

```text
\\SERVER\PrinterShare
```

Flux:

1. valideaza UNC path;
2. verifica daca imprimanta este deja instalata;
3. verifica daca print serverul este reachable;
4. instaleaza printer connection;
5. verifica dupa instalare;
6. returneaza rezultat clar.

Rezultate posibile:

- Success;
- AlreadyInstalled;
- RequiresAdmin;
- ServerUnavailable;
- InvalidUncPath;
- DriverInstallBlocked;
- BlockedByPolicy;
- FailedUnknownReason.

### 4.8 Set Default Printer

Set default printer se face pentru contul curent care ruleaza aplicatia.

UI-ul si raportul trebuie sa spuna clar:

- `Running as: DOMAIN\User`;
- `Default printer will be set for this Windows account`.

### 4.9 Print Test Page

Aplicatia poate trimite o pagina de test pentru imprimanta selectata.

Continut recomandat:

- ISG Printer Test Page;
- computer;
- running user;
- printer;
- timestamp;
- diagnostic summary.

### 4.10 Reports

Raport ticket-ready:

- computer;
- running user;
- elevated state;
- date/time;
- selected printer;
- UNC path;
- server;
- location;
- IP;
- driver;
- installed state;
- default state;
- queue status;
- DNS/server connectivity;
- printer IP connectivity;
- TCP checks;
- SNMP status;
- toner;
- model;
- serial number;
- page counter;
- diagnostics;
- recommended technician steps;
- actions performed.

Export:

- copy to clipboard;
- TXT;
- JSON;
- CSV pentru lista de imprimante.

## 5. SNMP

SNMP este functie principala, nu optionala decorativa.
Totusi, aplicatia trebuie sa ramana stabila daca SNMP nu raspunde.

Versiuni:

- SNMP v2c;
- SNMP v3.

Reguli:

- SNMP profile configurabil;
- v2c community string nu se logheaza;
- v3 credentials nu se logheaza;
- secretele se salveaza securizat cu DPAPI sau Windows Credential Manager;
- timeout scurt;
- retry limitat;
- lipsa SNMP nu inseamna automat printer offline.

SNMP citeste unde este posibil:

- toner levels;
- model;
- manufacturer;
- serial number;
- firmware version;
- page counter;
- raw printer status.

Rezultate toner:

- OK;
- Low;
- Critical;
- Empty;
- Unknown;
- NotSupported;
- SnmpUnavailable.

## 6. Settings

Settings non-secret pot fi in:

```text
%ProgramData%\ISG\ISG Printer\settings.json
```

Important:

Installerul trebuie sa creeze folderul si ACL-urile necesare.

Secretele SNMP nu se salveaza in plain text in `settings.json`.

Setari:

- KnownPrintServers;
- EnableActiveDirectoryDiscovery;
- EnableSnmp;
- DefaultSnmpProfile;
- TcpPortsToCheck;
- EnableIpRangeScan = false implicit;
- DefaultExportFolder;
- Theme;
- NetworkTimeoutMs;
- SnmpTimeoutMs;
- LogRetentionDays;
- CacheDurationMinutes.

## 7. Logging

Log path:

```text
%ProgramData%\ISG\ISG Printer\Logs
```

Se logheaza:

- app start;
- app version;
- running user;
- elevated state;
- computer;
- domain/workgroup;
- discovered servers;
- discovered printers count;
- install attempts;
- default printer changes;
- diagnostics;
- report exports;
- errors.

Nu se logheaza:

- passwords;
- SNMP community string;
- SNMP v3 passwords/auth keys/privacy keys;
- date confidentiale inutile.

Logurile trebuie sa aiba:

- rolling files;
- size limit;
- retention.

## 8. Deployment production

Pentru productie trebuie planificate:

- MSI sau MSIX;
- code signing certificate;
- version number;
- changelog;
- Intune/SCCM deployment;
- install path;
- ProgramData folders;
- ACL-uri pentru settings/logs/reports;
- uninstall behavior;
- upgrade behavior;
- rollback plan.

## 9. Arhitectura solutie

Structura:

```text
ISGPrinter/
|-- src/
|   |-- ISGPrinter.App/
|   |-- ISGPrinter.Domain/
|   |-- ISGPrinter.Application/
|   |-- ISGPrinter.Infrastructure/
|-- tests/
|   |-- ISGPrinter.Tests/
|-- docs/
|-- ISGPrinter.sln
```

Responsabilitati:

- `Domain`: modele, enum-uri, rezultate pure;
- `Application`: interfete si use cases;
- `Infrastructure`: Windows APIs, WMI/CIM, PowerShell wrapper, AD, SNMP, filesystem;
- `App`: WPF views, viewmodels, navigation, resources;
- `Tests`: unit tests si teste cu mock providers.

## 10. Servicii principale

Application interfaces:

- IAppEnvironmentService;
- ISettingsService;
- ILocalPrinterService;
- IPrinterDiscoveryService;
- IPrinterInstallService;
- IDefaultPrinterService;
- IPrinterDetailsService;
- IPrinterDiagnosticsService;
- ITechnicianGuidanceService;
- IPrinterReportService;
- ISnmpPrinterProvider;
- INetworkProbeProvider;
- ISpoolerServiceProvider;
- ICredentialProtector;

Nu includem:

- IPrinterRepairService;
- RepairPrinterUseCase;
- Repair Module.

## 11. Provideri infrastructure

Provideri:

- WindowsPrinterProvider;
- WmiPrinterProvider;
- PowerShellPrinterProvider;
- PrintServerPrinterProvider;
- ActiveDirectoryPrinterProvider;
- SnmpV2PrinterProvider;
- SnmpV3PrinterProvider;
- NetworkProbeProvider;
- SpoolerServiceProvider;
- FileSettingsProvider;
- CredentialManagerProvider sau DpapiCredentialProtector;
- ReportFileProvider;

## 12. Acceptance criteria v1

Aplicatia este acceptata pentru v1 cand:

1. solutia se compileaza fara erori;
2. aplicatia cere admin la pornire;
3. aplicatia afiseaza running user si elevated state;
4. aplicatia afiseaza computer name si domain/workgroup;
5. aplicatia listeaza imprimantele locale;
6. aplicatia detecteaza default printer curent;
7. aplicatia permite adaugarea manuala a unui print server;
8. aplicatia salveaza known print servers;
9. aplicatia listeaza imprimante share-uite de pe server;
10. aplicatia descopera AD printQueue objects cand PC-ul este domain joined;
11. aplicatia instaleaza imprimanta selectata prin UNC;
12. aplicatia seteaza default printer pentru contul curent;
13. aplicatia afiseaza detalii imprimanta;
14. aplicatia ruleaza diagnostic complet;
15. aplicatia interogheaza SNMP v2c unde este configurat;
16. aplicatia interogheaza SNMP v3 unde este configurat;
17. aplicatia afiseaza toner/model/serial/page counter unde sunt disponibile;
18. aplicatia nu crapa daca SNMP nu raspunde;
19. aplicatia genereaza recomandari pentru tehnician;
20. aplicatia nu executa repair automat;
21. aplicatia trimite test page;
22. aplicatia genereaza raport ticket-ready;
23. aplicatia copiaza raportul in clipboard;
24. aplicatia exporta TXT;
25. aplicatia scrie loguri cu rolling/retention;
26. aplicatia salveaza settings non-secret in ProgramData;
27. aplicatia protejeaza secretele SNMP;
28. aplicatia nu modifica Group Policy;
29. aplicatia afiseaza erori clare.

## 13. Roadmap de constructie

### Phase 1 - Foundation

- create solution;
- create projects;
- configure WPF app;
- add admin manifest;
- configure MVVM;
- configure DI;
- configure Serilog;
- create base settings service;
- create app environment service.

### Phase 2 - Shell UI

- main window;
- navigation;
- Printers page as startup page;
- persistent header with computer/user/admin/domain context;
- status bar with default printer, servers, refresh and SNMP profile;
- status badges;
- loading indicators;
- error presentation.

### Phase 3 - Local Printers

- list local printers;
- detect default printer;
- local printer details;
- refresh.

### Phase 4 - Manual Print Server

- add known print server;
- normalize server name;
- save settings;
- list shared printers;
- deduplicate by UNC.

### Phase 5 - Printer Actions

- install printer;
- verify install;
- set default printer;
- verify default;
- print test page.

### Phase 6 - Diagnostics

- spooler check;
- DNS check;
- ping check;
- TCP check;
- UNC validation;
- queue check;
- diagnostic result model.

### Phase 7 - Technician Guidance

- map diagnostic findings to recommended steps;
- copy recommended steps;
- include recommendations in reports.

### Phase 8 - SNMP

- SNMP v2c provider;
- SNMP v3 provider;
- profile settings;
- secure credential storage;
- toner/hardware/page counter mapping;
- unsupported-device handling.

### Phase 9 - Reports

- ticket report;
- copy to clipboard;
- TXT export;
- JSON export;
- CSV printer list export.

### Phase 10 - Production Packaging

- MSI/MSIX decision;
- code signing;
- installer folders and ACLs;
- versioning;
- deployment notes;
- smoke test checklist.

## 14. Decizie de start build

Inainte de a incepe implementarea trebuie doar confirmata tinta .NET:

- recomandat: instalam .NET 10 SDK si targetam `net10.0-windows`;
- alternativ: targetam `net8.0-windows` pentru ce este instalat acum.

Restul deciziilor sunt clare:

- Technician Tool only;
- require administrator at startup;
- no repair automation;
- diagnostics plus technician guidance;
- SNMP v2c and v3;
- Clean Architecture;
- WPF desktop app.
