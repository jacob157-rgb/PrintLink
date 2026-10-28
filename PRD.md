# Product Requirements Document (PRD)

## L-Net PrintLink — Windows Printer Sharing & Reliability

- **Document Version:** v1.0.0
- **Date:** 26 October 2026
- **Status:** Approved Draft / Specification
- **Product Owner:** L-Net
- **Target Platform:** Windows 10 (Build 19041+) / Windows 11 (21H2+)
- **Runtime & Stack:** C# / .NET 10 LTS (`net10.0-windows`) / WPF Desktop / .NET Worker Service (Windows Service) / SQLite / Windows Print Spooler APIs (`winspool.drv`)

*Note: For the official documentation and architecture references, see:*
- [docs/PRD.md](docs/PRD.md)
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- [docs/SECURITY.md](docs/SECURITY.md)
- [docs/POC_PLAN.md](docs/POC_PLAN.md)

---

## 1. Executive Summary & Product Vision

**L-Net PrintLink** is an enterprise-grade, lightweight Windows desktop application and background service designed to solve common, persistent issues in Windows local network printer sharing. 

In many small-to-medium business, branch-office, and retail environments, native Windows SMB printer sharing often suffers from:
- Silent connection drops after sleep/wake or network renegotiation.
- Complex and fragile Windows credential / RPC / "Point and Print" permission errors (e.g., RPC error `0x0000011b`, `0x00000709`).
- Lack of visibility into job lifecycle, print queue backpressure, and spooler failures.
- Inability to reliably support mixed printer fleets (office laser/inkjet, USB receipt thermal printers, label printers).

PrintLink decouples desktop GUI management from a robust background service running as a Windows Service, operating under a resilient client-server architecture with structured monitoring, mutual authentication, persistent job state tracking, auto-reconnection with exponential backoff + jitter, and diagnostic tooling.

---

## 2. Product Operating Modes

A single universal installer and unified binary distribution supports three operating modes:

| Mode | Role & Capabilities |
| :--- | :--- |
| **1. Server** | Enumerates host-installed Windows printers, allows administrators to selectively publish shares with friendly names, authenticates remote clients, enforces authorization ACLs, spools print jobs to local Windows Print Spooler, and reports job telemetry. |
| **2. Client** | Discovers or manually connects to PrintLink Server(s) via IP/hostname over TLS, authenticates using managed credentials, registers remote printers into the client operating system, routes client print jobs, tracks job states, and auto-reconnects on network drops. |
| **3. Server + Client (Combined)** | Operates simultaneously as a Server (sharing local attached printers) and as a Client (accessing shared printers on other branch machines). Enforces loop-detection to prevent re-sharing remote printers back into the network. |

---

## 3. High-Level System Architecture

```text
+--------------------------------------------------------------------------------+
|                             PrintLink Ecosystem                                |
+--------------------------------------------------------------------------------+

 [ CLIENT MACHINE ]                                       [ SERVER MACHINE ]
 +----------------------------------+                     +----------------------------------+
 | PrintLink Desktop (WPF GUI)      |                     | PrintLink Desktop (WPF GUI)      |
 | - Dashboard & Tray Manager       |                     | - Admin Dashboard & Setup Wizard |
 +-----------------+----------------+                     +-----------------+----------------+
                   | (Named Pipe / IPC)                                     | (Named Pipe / IPC)
 +-----------------v----------------+                     +-----------------v----------------+
 | PrintLink Service (Worker)       |   TLS 1.3 (HTTPS)   | PrintLink Service (Worker)       |
 | - Network Client Engine          | <=================> | - Kestrel Secure API & Spooler   |
 | - Reconnect Handler (Backoff)    |   Control & Data    | - Client Auth & Access Control   |
 | - Local Job Cache (SQLite)       |                     | - Job Lifecycle Manager (SQLite) |
 +-----------------+----------------+                     +-----------------+----------------+
                   |                                                        |
 +-----------------v----------------+                     +-----------------v----------------+
 | Windows Client Print Subsystem   |                     | Windows Print Spooler Service    |
 | - Virtual Printer / IPP Client   |                     | - winspool.drv (Local Spooler)   |
 | - Windows Apps (Word/PDF/Notepad)|                     | - Physical / Virtual Drivers     |
 +----------------------------------+                     +-----------------+----------------+
                                                                            |
                                                                  [ Physical Printer / USB / LAN ]
```

---

## 4. Key Personas & Roles

1. **Administrator (IT / Office Manager):**
   - Installs PrintLink on the host machine.
   - Selects operating mode via First-Run Wizard.
   - Chooses which local printers to publish, giving them readable friendly names.
   - Generates, authorizes, and revokes client credentials/tokens.
   - Configures network bindings, TLS certificates, service auto-start, and retention policies.
   - Inspects audit logs and exports sanitized diagnostic packages for support.

2. **Standard User (Client Workstation):**
   - Connects to PrintLink server via IP/hostname or LAN discovery.
   - Enters client credential or validates connection token.
   - Selects available shared printers and submits print jobs from native Windows software.
   - Views personal print job statuses (`Queued`, `Sending`, `Spooling`, `Completed`, `Failed`, `Unknown`).
   - Receives clear error notifications with diagnostic IDs without needing administrative privileges.

---

## 5. Functional Requirements (FR)

### FR-01: First-Run Wizard & Mode Configuration (P0)
- The installer or initial launch must trigger an intuitive First-Run Wizard.
- Allows user to choose: **Server**, **Client**, or **Server + Client**.
- Dynamically validates system prerequisites: Windows OS build, .NET 10 runtime, Windows Print Spooler service state, and firewall status.
- Configuration is stored in persistent local database (`PrintLink.db`) and can be reconfigured or switched without data destruction.

### FR-02: Windows Service & Startup Management (P0)
- The core printing and communication engine must run as a **.NET Worker Service** registered as a Windows Service (`PrintLinkService`).
- Service start mode can be set to Automatic (starts at boot before user login).
- Desktop GUI (`PrintLink.Desktop`) runs as a standard user process and features an independent configuration to auto-launch into the System Tray upon user login.
- Closing the WPF Desktop GUI or signing out a desktop user **must not** stop the background service or interrupt in-flight print jobs.
- Desktop GUI interacts with the background service via secure local IPC (Windows Named Pipes with ACLs).

### FR-03: Local Printer Enumeration & Management (P0)
- Enumerate locally installed Windows printers via `winspool.drv` APIs (`EnumPrinters`, level 2/4).
- Retrieve and display: Printer Name, Driver Name, Port Name, IsDefault, Shared Status, and Spooler Status (Ready, Paused, Offline, Error).
- Server administrators can toggle sharing per printer and specify a Friendly Share Name and Comment.
- Explicitly flag unsupported printers or inaccessible network ports rather than failing silently.

### FR-04: Network Discovery & Connection Management (P0)
- Client can manually connect to servers using IPv4, IPv6, or DNS Hostname with custom port (Default: `8270`).
- LAN discovery via mDNS / UDP broadcast is optional convenience; manual IP/hostname connection is the primary and permanent fallback.
- Client tests server reachability, TLS endpoint identity, and credentials before saving connection.
- Supports storing multiple remote PrintLink servers.

### FR-05: Security, Authentication, & Authorization (P0)
- All network communications between Client and Server must use TLS (TLS 1.2/1.3).
- Remote access requires mutual token-based or API-key authentication managed by the Server administrator.
- Granular authorization: Server administrator can authorize a client for all shared printers or a specific subset.
- Secrets, tokens, and private keys must be protected using Windows DPAPI (`System.Security.Cryptography.ProtectedData`) or Windows Credential Manager (`wincred`). Plaintext passwords are strictly forbidden.
- Failed authentication attempts are subject to exponential rate limiting to mitigate brute-force attacks.
- Sensitive security events (failed login, client revocation, permission changes) must be recorded in the security audit log.

### FR-06: Print Workflow & Integration Strategy (P0)
- Prioritize a proven, end-to-end printing path from ordinary Windows applications (e.g., Notepad, Word, Acrobat) to the server.
- The proof of concept (PoC) must rigorously evaluate:
  1. **Direct API spooling** (Client sends document stream via HTTPS to Server; Server writes to local spooler via `StartDocPrinter`/`WritePrinter`).
  2. **Standard IPP / IPP-Everywhere** over HTTPS.
  3. **Client-side Virtual Port / Printer mapping** using standard Microsoft in-box drivers (e.g., MS Publisher Color / Generic PostScript / XPS).
- Server validates incoming payload size against configurable maximum limit (default: 50 MB, streaming chunk size: 64 KB) to prevent OOM/DoS.
- Server returns immediate ACK with a stable **PrintLink Job ID** (`UUIDv7`) upon successful receipt and database commitment.

### FR-07: Job Lifecycle & Reliability (P0)
- Every job is tracked via a deterministic state machine:
  - `Queued`: Placed in client queue awaiting network dispatch.
  - `Sending`: Actively streaming payload over TLS.
  - `Accepted`: Server acknowledged receipt and persisted job metadata.
  - `Spooling`: Server actively writing data to Windows Print Spooler (`winspool.drv`).
  - `Completed`: Windows Spooler reported successful job completion (spooling finished without spooler errors).
  - `Failed`: Spooler or network rejected job, with specific error code recorded.
  - `Unknown`: Network dropped mid-transmission or mid-acknowledgement.
- **Strict Duplicate Prevention:** Server checks `PrintLink Job ID` idempotency key before queuing.
- **No Blind Retries:** If a job enters `Unknown`, the client must NOT automatically resubmit without status reconciliation or explicit user confirmation.
- The UI explicitly clarifies that `Completed` indicates spooling success, as physical paper output cannot be guaranteed by standard Windows spooler drivers.

### FR-08: Network Interruption & Auto-Reconnection (P0)
- Active connection health is monitored via lightweight periodic heartbeats.
- In case of network interruption or server restart:
  - Client flags server as `Reconnecting`.
  - Reconnection retry loop employs **Exponential Backoff with Full Jitter** (Base: 2s, Max: 60s, Multiplier: 2.0).
  - Upon reconnection, client refreshes remote printer inventory and synchronizes job statuses.
  - Jobs in `Queued` state resume transmission; jobs in `Unknown` trigger reconciliation.

### FR-09: Persistent Storage & Retention (P0)
- Embedded SQLite engine (`Microsoft.Data.Sqlite`) in WAL mode (`Write-Ahead Logging`).
- Data stored:
  - Server configurations, network bindings, certificates.
  - Client identities, authentication tokens, and ACL mappings.
  - Historical job records (Metadata only: Job ID, client, printer, timestamp, size, page count, status, error code).
- **Zero Document Retention:** Document content/payload is purged immediately after spooler submission is finalized. Document data must never be saved in the database or written to disk caches longer than necessary.
- Configurable job history retention policy (Default: 30 days or max 10,000 records).

### FR-10: Structured Logging & Support Diagnostics (P0)
- Structured logging using rolling file sinks with size and retention caps (Default: 5 files x 10 MB).
- Error messages presented to the user contain a unique **Diagnostic Reference ID** (e.g., `PL-NET-502-A7F1`) mapped to the structured log entry.
- One-click **Export Diagnostic Bundle**:
  - Compresses relevant logs, system environment, spooler service status, and network reachability results into a ZIP file.
  - Automatically scrubs private keys, credentials, tokens, user passwords, and document names/contents.

### FR-11: Desktop UI & Dashboard (P0)
WPF Desktop interface structured into clear views:
1. **Dashboard:** Mode banner, service health status, connected servers/clients, active printer shares, recent jobs, alert ticker.
2. **Printers View:** Local printers list (with share toggles & friendly names) and Remote discovered printers list (with latency and status).
3. **Servers View (Client Mode):** Added servers, connection states (`Connected`, `Reconnecting`, `Auth Failed`, `Offline`), connection tester.
4. **Jobs View:** Comprehensive job history with filters (by date, printer, client, status), error inspector, and safe retry button.
5. **Clients & Access View (Server Mode):** Registered clients, allowed printers, last active timestamp, generate new token, revoke access.
6. **Diagnostics & Logs View:** Live log viewer with severity filter, network ping/TLS diagnostic test, export bundle button.
7. **Settings View:** Mode switcher, Windows Service startup toggle, GUI auto-start toggle, network port, TLS cert manager, database maintenance.
8. **System Tray Integration:** Quick status tooltip, pause/resume printing, open dashboard, exit GUI (with notice that background service remains active).

### FR-12: Thermal / Receipt / Label Printer Compatibility (P1/P2)
- Architecture maintains separation between standard document pipelines (EMF / XPS / PDF) and raw byte streams (ESC/POS, ZPL, TSPL).
- Initial MVP focuses on standard Windows Spooler compatibility; specialized raw pass-through adapters will be validated in P1 without breaking generic print paths.

---

## 6. Non-Functional Requirements (NFR)

- **Performance:** GUI cold startup under 2.0 seconds; memory footprint under 50 MB for Service and under 80 MB for Desktop; streaming chunk processing to keep memory flat even when handling 500 MB+ print jobs.
- **Reliability:** Service crash resilience; automatic recovery from Windows Spooler restarts; database integrity across ungraceful system shutdowns via SQLite WAL.
- **Security:** Zero plaintext credentials; TLS encryption in transit; DPAPI encryption at rest; least-privilege service execution (`NT SERVICE\PrintLinkService` or `LocalService`).
- **Compatibility:** Windows 10 (20H2+) and Windows 11 64-bit; compatible with standard Windows v3 and v4 printer drivers.
- **Accessibility:** WPF full keyboard navigation, high-contrast support, screen reader compatibility via AutomationProperties.

---

## 7. Acceptance Criteria for MVP

- [x] Solution and projects build cleanly under .NET 10 LTS (`net10.0-windows`).
- [ ] Desktop GUI (`PrintLink.Desktop`) and Background Service (`PrintLink.Service`) are separate processes communicating via local IPC.
- [ ] Setup Wizard allows selecting Server, Client, or Server+Client mode.
- [ ] Server enumerates local printers and successfully shares selected printers with friendly names.
- [ ] Client connects to Server via IP/hostname and passes token-based authentication.
- [ ] Unauthorized client requests are rejected and logged to the audit log.
- [ ] Client can view only permitted printers based on Server authorization rules.
- [ ] Proof of Concept proves successful print submission from a standard Windows app on Client to physical/virtual printer on Server.
- [ ] Background service starts on Windows boot and continues executing when GUI is closed or user logs off.
- [ ] Network disconnect triggers graceful reconnection loop with backoff; restores status upon network recovery.
- [ ] Uncertain jobs (`Unknown`) are never re-spooled automatically without reconciliation.
- [ ] Job metadata survives service and system restarts.
- [ ] Exported diagnostic logs are verified free of credentials, tokens, or document content.
