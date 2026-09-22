# Audit Trail System – ERP 2026

## Overview

The ERP Audit Trail is a **central, reusable system** that records all important operations (Create, Update, Delete) performed across all ERP screens. It is designed to answer the administrator's core questions:

> **Who** did **what**, **when**, **from which branch/company/fiscal year**, **on which screen/entity**, with **what changed**?

---

## Architecture

```
ERP.Domain
  └── Entities/AuditLog.cs        ← BIGINT PK entity
  └── Enums/AuditAction.cs        ← All auditable actions enum
  └── Interfaces/IAuditableEntity.cs  ← Marker interface

ERP.Application
  └── Interfaces/IAuditService.cs     ← Service contract
  └── Models/AuditLogEntry.cs         ← DTO for log entries

ERP.Infrastructure
  └── Services/AuditService.cs        ← Implementation
  └── Data/ErpDbContext.cs            ← DbSet<AuditLog> + indexes
  └── Migrations/AddAuditLogs         ← EF migration

ERP.Web
  └── Controllers/AuditController.cs  ← Read-only audit history screen
  └── Views/Audit/Index.cshtml        ← Grid with filters (AUD001)
  └── Views/Audit/Details.cshtml      ← Full detail + ChangedFields table
  └── Models/AuditViewModels.cs       ← ViewModels for audit screens
```

---

## Database Table: `AuditLogs`

| Column | Type | Description |
|--------|------|-------------|
| `Id` | BIGINT IDENTITY | Primary key |
| `TimestampUtc` | DATETIME2 | Always UTC |
| `UserId` | NVARCHAR(450) | Identity user ID |
| `EmployeeCode` | NVARCHAR(50) | Login identifier |
| `UserName` | NVARCHAR(256) | Display name |
| `CompanyId` | INT | Company context |
| `SisterCompanyId` | INT | Sister company (reserved) |
| `BranchId` | INT | Branch context |
| `FiscalYearId` | INT | Fiscal year context |
| `ScreenCode` | NVARCHAR(20) | e.g., "BRN001" |
| `ScreenName` | NVARCHAR(100) | e.g., "Branches" |
| `ModuleName` | NVARCHAR(100) | e.g., "Organization" |
| `EntityName` | NVARCHAR(100) | e.g., "Branch" |
| `EntityId` | NVARCHAR(100) | Primary key value as string |
| `Action` | NVARCHAR(50) | "Create"/"Update"/"Delete" etc. |
| `Description` | NVARCHAR(500) | Human-readable summary |
| `OldValues` | NVARCHAR(MAX) | JSON snapshot before change |
| `NewValues` | NVARCHAR(MAX) | JSON snapshot after change |
| `ChangedValues` | NVARCHAR(MAX) | JSON diff (Update only) |
| `IpAddress` | NVARCHAR(50) | Client IP (reverse-proxy aware) |
| `UserAgent` | NVARCHAR(500) | Browser/OS |
| `RequestId` | NVARCHAR(100) | ASP.NET trace ID |
| `CorrelationId` | NVARCHAR(100) | X-Correlation-ID header |
| `HttpMethod` | NVARCHAR(10) | GET/POST/etc. |
| `RequestPath` | NVARCHAR(500) | URL path |
| `Success` | BIT | True = success, False = failed |
| `ErrorMessage` | NVARCHAR(2000) | Error info if failed |

---

## Indexes

| Index Name | Columns |
|------------|---------|
| `IX_AuditLogs_TimestampUtc` | TimestampUtc |
| `IX_AuditLogs_UserId` | UserId |
| `IX_AuditLogs_Action` | Action |
| `IX_AuditLogs_Success` | Success |
| `IX_AuditLogs_Branch_Timestamp` | BranchId, TimestampUtc |
| `IX_AuditLogs_User_Timestamp` | UserId, TimestampUtc |
| `IX_AuditLogs_Screen_Timestamp` | ScreenCode, TimestampUtc |
| `IX_AuditLogs_Entity` | EntityName, EntityId |
| `IX_AuditLogs_Company_Branch_Timestamp` | CompanyId, BranchId, TimestampUtc |
| `IX_AuditLogs_Action_Success_Timestamp` | Action, Success, TimestampUtc |

---

## ChangedValues Format

For **Update** operations, `ChangedValues` contains **only the fields that actually changed**:

```json
{
  "Code": { "Old": "BR001", "New": "BR002" },
  "IsActive": { "Old": "True", "New": "False" }
}
```

**Rules:**
- Fields with identical old/new values are **not included**
- If zero fields changed → **no audit record is written** (no-op save)
- Sensitive fields are **never included**

---

## Sensitive Fields (Never Stored)

The following fields are automatically stripped from all JSON snapshots:

```
Password, ConfirmPassword, PasswordHash, SecurityStamp, ConcurrencyStamp,
Token, RefreshToken, AccessToken, SecretKey, ApiSecret, PrivateKey, ConnectionString
```

---

## Transaction Safety

The `AuditService` writes audit logs using a **separate `DbContext` scope**, obtained via `IServiceProvider.CreateScope()`. This means:

- If the **main business transaction rolls back**, the audit log is **still written**
- Failed operations (`Success=false`) are **always recorded**
- Audit never breaks the main operation (exceptions are caught and logged via `ILogger`)

---

## How to Add Audit to a New Controller

### Step 1: Inject `IAuditService`

```csharp
private readonly IAuditService _audit;
private const string SC_Code   = "INV001";   // Your screen code
private const string SC_Name   = "Invoices"; // Your screen name
private const string SC_Module = "Sales";    // Your module name

public InvoiceController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
{
    _audit = audit;
    // ...
}
```

### Step 2: Audit a Create operation

```csharp
await _context.Invoices.AddAsync(invoice);
await _context.SaveChangesAsync();

await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
    entityName: "Invoice",
    entityId:   invoice.Id.ToString(),
    newValues: new { invoice.Id, invoice.Number, invoice.TotalAmount, invoice.Status });
```

### Step 3: Audit an Update operation

```csharp
var entity = await _context.Invoices.FindAsync(id);

// Capture BEFORE changes:
var oldValues = new { entity.Id, entity.Number, entity.Status };

// Apply changes:
entity.Status = newStatus;
await _context.SaveChangesAsync();

// Log AFTER changes (automatically skipped if nothing changed):
await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
    entityName: "Invoice",
    entityId:   entity.Id.ToString(),
    oldValues:  oldValues,
    newValues: new { entity.Id, entity.Number, entity.Status });
```

### Step 4: Audit a Delete operation

```csharp
var entity = await _context.Invoices.FindAsync(id);

// Capture BEFORE delete:
var oldValues = new { entity.Id, entity.Number, entity.TotalAmount };

_context.Invoices.Remove(entity);
await _context.SaveChangesAsync();

await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
    entityName: "Invoice",
    entityId:   id.ToString(),
    oldValues:  oldValues);
```

---

## Audit History Screen (AUD001)

**URL:** `/Audit/Index`  
**Permission:** `Administration.AuditHistory.View`

### Filters Available
- Date From / Date To
- Employee Code, User Name
- Company, Branch, Fiscal Year
- Module, Screen Code
- Action (Create/Update/Delete/Login/…)
- Entity, Record ID
- Result (Success/Failed)

### Detail Screen
**URL:** `/Audit/Details/{id}`

Shows:
1. **General Information** – timestamp, user, org context
2. **Operation Information** – screen, entity, action, result, error
3. **Request Context** – IP, UserAgent, RequestId, HttpMethod, Path
4. **Changed Fields Table** – field-by-field diff with Old/New values highlighted
5. **OldValues JSON** – pretty-printed JSON panel
6. **NewValues JSON** – pretty-printed JSON panel

---

## Permissions

| Code | Description |
|------|-------------|
| `Administration.AuditHistory.View` | View the Audit History screen |
| `Administration.AuditHistory.Export` | Export audit data |

> **Important:** There is intentionally no `Delete` permission for Audit History. Audit records cannot be deleted through the UI.

---

## Screen Codes Reference

| Controller | Screen Code | Module |
|-----------|-------------|--------|
| BranchController | BRN001 | Organization |
| CompanyController | COM001 | Organization |
| FiscalYearController | FYR001 | Organization |
| UserController | USR001 | Administration |
| RoleController | ROL001 | Administration |
| UserBranchController | UBR001 | Administration |
| AuditController | AUD001 | Administration |

---

## Unit Tests

Tests are in `ERP.Tests.Unit/AuditServiceTests.cs`:

| Test | Scenario |
|------|----------|
| `LogCreateAsync_ShouldSaveAuditRecord_WithNewValues` | Create logs NewValues, no OldValues |
| `LogUpdateAsync_ShouldSaveChangedValues_WhenFieldsChange` | Update diff contains only changed fields |
| `LogUpdateAsync_ShouldSkipAudit_WhenNoFieldsChanged` | No-op save → zero records |
| `LogDeleteAsync_ShouldSaveOldValues_WithNoNewValues` | Delete logs OldValues, no NewValues |
| `LogCreateAsync_ShouldExcludeSensitiveFields` | PasswordHash/Token never in JSON |
| `LogUpdateAsync_ChangedValues_ShouldContainCorrectOldAndNew` | ChangedValues has Old/New per field |
| `LogFailedAsync_ShouldSetSuccessFalse_WithErrorMessage` | Failed ops mark Success=false |
| `LogAsync_ShouldPopulateUserAndOrgContext_FromCurrentUser` | User/Org context auto-read |
