# ERP 2026 – Project Context & Architectural Reference Guide
**File**: `ERP_PROJECT_CONTEXT.md`  
**Status**: Official System Reference & Architectural Source of Truth  
**Target Environment**: .NET 8, C# 12, Entity Framework Core 8, SQL Server 2022, ASP.NET Core MVC (Razor + Vue.js components + Bootstrap 5 RTL/LTR)  
**Last Updated**: September 2026  

---

## 1. Executive Summary & System Identity

The **ERP 2026** platform is an enterprise-grade, multi-branch, multi-company ERP system engineered for commercial, industrial, and retail operations in Egypt and the MENA region. The system is built on **Clean Architecture** principles, prioritizing data isolation, strong auditability, rigorous accounting compliance, high performance, and full bilingual support (Arabic default, English secondary).

### Core Architectural Pillars
1. **Multi-Branch Security & Data Isolation**: Strict, automatic row-level branch isolation using EF Core Global Query Filters and ChangeTracker security hooks.
2. **Sister Companies Support**: Multi-company organizational model supporting corporate headquarters and independent sister companies/operating units.
3. **Fiscal Year Life-Cycle Governance**: Enforced fiscal period boundaries; operations in closed or inactive fiscal years are strictly blocked.
4. **Comprehensive Central Audit Trail**: Immutable tracking of all entity mutations (Insert, Update, Delete, Post, Unpost) with full JSON diff snapshots and environmental diagnostics.
5. **Rigorous Double-Entry General Ledger**: Unified Chart of Accounts (COA) supporting hierarchical parent-child structures, control accounts, sub-ledgers, multi-currency valuation, and transactional locking.
6. **Optimistic Concurrency Control**: SQL Server rowversion/timestamp tokens across all master and transactional entities to eliminate race conditions.
7. **Thread-Safe Branch-Aware Numbering**: High-concurrency code generation with database-level row locks (`UPDLOCK, ROWLOCK`).
8. **Adaptive Bilingual UI**: Native RTL/LTR layout switching, unified Arabic/English resource management, and responsive client-side data grids.

---

## 2. Solution Structure & Layering

The codebase follows the Clean Architecture design pattern:

```
o:\Tamer\ERP_2026\ERP\
├── ERP.sln
├── ERP.Domain/                  # Core Business Entities, Enums, Interfaces, Value Objects
│   ├── Entities/                # Domain models (GLAccount, JournalEntry, Customer, Branch, etc.)
│   ├── Enums/                   # AccountNature, AccountCategory, SubLedgerType, SourceDocumentType, etc.
│   └── Interfaces/              # Marker contracts (IMustHaveBranch, IConcurrencyAware, IAuditableEntity)
├── ERP.Application/             # Business Contracts, DTOs, Service Interfaces, Validation Logic
│   ├── DTOs/                    # AccountImport, Reports, Lookups, CodeSequences
│   └── Interfaces/              # ICurrentUserContext, IAuditService, IJournalEntryValidationService, etc.
├── ERP.Infrastructure/          # Persistence, Identity, EF Core Configurations, Migrations, External Services
│   ├── Data/                    # ErpDbContext, SeedData, Fluent API Mappings
│   ├── Identity/                # ApplicationUser, ApplicationRole
│   ├── Migrations/              # Versioned EF Core migrations
│   └── Services/                # Service implementations (AuditService, CodeSequenceService, etc.)
├── ERP.Web/                     # Presentation Layer (ASP.NET Core MVC, Razor Views, Controllers, APIs)
│   ├── Controllers/             # MVC & API Controllers
│   ├── Authorization/           # [HasPermission] attributes and filters
│   ├── Resources/               # SharedResource.ar.resx, SharedResource.en.resx
│   ├── Views/                   # Razor Views structured by module
│   └── wwwroot/                 # Client assets (Bootstrap 5 RTL, Vue.js, DataTables, erp-lookup.js)
└── ERP.Tests.Unit/              # Comprehensive xUnit test suite
```

---

## 3. Organizational Structure & Multi-Branch Security

### 3.1 Corporate Hierarchy
* **Company**: Top-level legal entities (e.g., `COMP01` Sewtec Main Center, `COMP02` Sewtec Faisal, through 29 Sister Companies across Egypt).
* **Branch**: Operating units under each company (e.g., Machines branch, Spare Parts branch, Maintenance branch).
* **Fiscal Year**: Linked per organization with start date, end date, active flag, and closed flag.

### 3.2 Branch Security Implementation
1. **Marker Contract**:
   ```csharp
   public interface IMustHaveBranch
   {
       int BranchId { get; set; }
   }
   ```
2. **Global Query Filter (Row-Level Security)**:
   In `ErpDbContext.OnModelCreating`, every entity implementing `IMustHaveBranch` automatically receives:
   ```csharp
   builder.Entity<T>().HasQueryFilter(e => IsSuperAdmin || e.BranchId == CurrentBranchId);
   ```
   Child lines (such as `JournalEntryLine` or `VoucherLine`) inherit branch isolation via their parent header.
3. **ChangeTracker Enforcement**:
   In `ErpDbContext.SaveChanges` and `SaveChangesAsync`, `ApplyBranchSecurity()`:
   * Enforces `BranchId = CurrentBranchId` for non-superadmin users on insert.
   * Prevents non-superadmin users from modifying `BranchId` on existing records.
4. **Current User Context**:
   * `ICurrentUserContext` & `ICurrentUserService` resolve claims from `HttpContext`:
     * `BranchId`, `CompanyId`, `FiscalYearId`, `HasAllBranches`, `HasAllCompanies`.
     * `CanAccessBranch(int branchId)` and `CanAccessCompany(int companyId)`.

### 3.3 Single-Active-Branch Session Isolation Rule (قاعدة عزل بيانات جلسة الفرع النشط)
* **المبدأ الأساسي (Core Principle)**:
  حتى لو كان المستخدم يمتلك صلاحية الوصول لعدة فروع أو كل الفروع (`HasAllBranches == true` أو Admin / Corporate Manager)، فإن جلسة العمل النشطة في النظام تعتمد بشكل صارم ومغلق على **الفرع الحالي المختار (`CurrentUserContext.BranchId`)**.
  لا يُسمح إطلاقاً بتسريب أو عرض بيانات فروع أخرى داخل شاشات الحركات المالية، البيانات الأساسية، أو نوافذ الاختيار والبحث، طالما أن المستخدم يعمل داخل سياق فرع محدد. وإذا أراد المستخدم التعامل مع بيانات فرع آخر، يتوجب عليه التحويل الصريح للفرع المطلوب من خلال محوّل الفروع (Branch Switcher) في الشريط العلوي (Topbar).

* **مجالات التطبيق الإلزامية (Enforced Scopes)**:
  1. **شاشات الحركات المالية (Transaction Screens)**:
     * **قيود اليومية (`JournalEntriesController`)**: جدول العرض (`LoadTableData`)، التفاصيل (`Details`)، التعديل (`Edit`)، الحذف (`Delete`)، والترحيل (`Post`) مقصورة تماماً على فرع الجلسة الحالي (`query.Where(h => h.BranchId == currentBranchId)`). إنشاء أي قيد جديد (`Create`) يقفل تلقائياً على `BranchId = CurrentUserContext.BranchId`.
     * **سندات القبض والصرف (`CashBankVouchersController`)**: جدول العرض (`LoadTableData`)، التفاصيل (`Details`)، التعديل (`Edit`)، الترحيل (`PerformPostAsync`)، إلغاء الترحيل (`Unpost`)، الإلغاء (`Cancel`)، الحذف (`Delete`)، والطباعة (`Print`) مقصورة تماماً على فرع الجلسة الحالي.
  2. **شاشات البيانات الأساسية (Master Data Screens)**:
     * **العملاء (`CustomerController`)**: جدول العرض والبحث والترقيم التلقائي وتصنيفات العملاء مقصورة حصراً على الفرع الحالي. يُمنع إظهار أو إضافة أو تعديل أو حذف عميل ينتمي لفرع آخر داخل الجلسة النشطة.
     * **الموردون (`VendorController`)**: جدول العرض والبحث والترقيم وتصنيفات الموردين مقصورة حصراً على الفرع الحالي.
     * **المندوبون والمستودعات (`Salesmen`, `Warehouses`)**: تعزل تماماً بناءً على `CurrentUserContext.BranchId`.
  3. **البحث والاختيار والنوافذ المنبثقة (Lookups, Dropdowns & Modal Search)**:
     * خدمة وقوائم البحث الموحدة (`LookupService` / `LookupsController` / `LookupController` / Select2 / `erp-lookup.js`) تُطبّق عزل الفرع الصارم؛ فلا يمكن للمستخدم اختيار عميل أو مورد أو مستودع أو مندوب من فرع آخر داخل سطور القيود أو السندات.
     * حسابات الدليل المالي (`GLAccount`) في قوائم الاختيار تعرض فقط الحسابات العامة المشتركة (`BranchId == null`) أو الحسابات المخصصة حصرياً لفرع الجلسة الحالي (`BranchId == CurrentUserContext.BranchId`).

---

## 4. Security, Roles & Permissions System

### 4.1 Granular Permission Structure
Permissions follow the naming convention: `Module.Screen.Action`.
* **Modules**: `Administration`, `Organization`, `MasterData`, `Accounting`, `Cash`, `Sales`, `Purchases`, `Inventory`, `Reports`, `Settings`.
* **Actions**: `View`, `Add`, `Edit`, `Delete`, `Post`, `Unpost`, `Approve`, `Cancel`, `Print`, `Export`.

### 4.2 Permission Enforcement
* Controllers and action methods enforce permissions using the declarative attribute:
  ```csharp
  [HasPermission("Accounting.JournalVoucher.View")]
  [HasPermission("Accounting.JournalVoucher.Post")]
  ```
* Evaluated via `PermissionAuthorizationFilter` checking `RolePermission` and user-assigned overrides.
* In Razor views, dynamic visibility is controlled using:
  ```html
  @if (CurrentUserContext.HasPermission("Accounting.JournalVoucher.Post")) { ... }
  ```

---

## 5. Fiscal Year Life-Cycle & Validation

* **Entity**: `FiscalYear` (`Year`, `StartDate`, `EndDate`, `IsActive`, `IsClosed`).
* **Validation Invariant**:
  * No financial record, journal entry, or cash/bank voucher may be created, edited, posted, or unposted if `FiscalYear.IsClosed == true`.
  * Operations must fall within the range `StartDate <= TransactionDate <= EndDate` of an active fiscal year.

---

## 6. Central Audit Trail Architecture

* **Marker Interface**: `IAuditableEntity`.
* **Database Table**: `AuditLogs` (`BIGINT IDENTITY` PK).
* **Audit Metadata Captured**:
  * `TimestampUtc`, `UserId`, `EmployeeCode`, `UserName`.
  * `CompanyId`, `SisterCompanyId`, `BranchId`, `FiscalYearId`.
  * `ScreenCode`, `ScreenName`, `ModuleName`, `EntityName`, `EntityId`.
  * `Action` (`Create`, `Update`, `Delete`, `Post`, `Unpost`, etc.).
  * `OldValues` (JSON), `NewValues` (JSON), `ChangedValues` (JSON diff containing only modified fields).
  * `IpAddress`, `UserAgent`, `RequestId`, `CorrelationId`, `HttpMethod`, `RequestPath`, `Success`, `ErrorMessage`.
* **Immutability**: The `AuditLogs` table has no delete endpoint in the UI. Auditing failures never crash financial transactions.

---

## 7. Optimistic Concurrency Control

* **Marker Interface**:
  ```csharp
  public interface IConcurrencyAware
  {
      byte[] RowVersion { get; set; }
  }
  ```
* **Mapping**: Configured with `.IsRowVersion()` in EF Core.
* **Controller Handling**:
  ```csharp
  try {
      await _context.SaveChangesAsync();
  } catch (DbUpdateConcurrencyException) {
      return Json(new { success = false, message = "تم تعديل السجل بواسطة مستخدم آخر. يرجى إعادة التحميل." });
  }
  ```

---

## 8. Thread-Safe Sequential Numbering Strategy

* **Entity**: `CodeSequence` (`EntityName`, `BranchId`, `Prefix`, `LastNumber`, `PaddingLength`, `IsAuto`, `IsManualAllowed`).
* **Service**: `ICodeSequenceService` (`CodeSequenceService`).
* **Concurrency Protection**:
  * SQL Server: Executes atomic row lock:
    ```sql
    SELECT LastNumber FROM CodeSequences WITH (UPDLOCK, ROWLOCK) 
    WHERE EntityName = @entityName AND (BranchId = @branchId OR (@branchId IS NULL AND BranchId IS NULL))
    ```
  * In-Memory Fallback: Protected by `SemaphoreSlim` for unit test isolation.

---

## 9. Chart of Accounts & General Ledger (Phase 4)

### 9.1 Account Hierarchy & Classification
* **Unified Entity**: `GLAccount` (Self-referencing tree via `ParentId`, `Level`, `Children`).
* **Account Category (`AccountCategory`)**:
  * `1 = Assets` (الأصول - Debit nature)
  * `2 = Liabilities` (الخصوم والالتزامات - Credit nature)
  * `3 = Equity` (حقوق الملكية - Credit nature)
  * `4 = Revenues` (الإيرادات - Credit nature)
  * `5 = Expenses` (المصروفات والتكاليف - Debit nature)
* **Account Nature (`AccountNature`)**: `Debit` (1), `Credit` (2).
* **Grouping vs Detail**:
  * `IsGroup = true`: Heading / summary account. Strictly forbidden from accepting direct journal lines.
  * `IsGroup = false`: Detail account accepting direct journal entries and vouchers.

### 9.2 Control Accounts & Sub-Ledger Integration
* **Control Account Flag**: `GLAccount.IsControlAccount` (`bool`).
* **Sub-Ledger Type (`SubLedgerType`)**:
  * `0 = None`
  * `1 = Customer` (عميل)
  * `2 = Vendor` (مورد)
  * `3 = Bank` (بنك)
  * `4 = Employee` (موظف)
* **Reconciliation Accounts**: `GLAccount.IsReconciliationAccount` (`bool`). When `true`, any journal entry line directing to this account **must** specify `SubLedgerType` and `SubLedgerId`.
* **Automatic Control Account Resolution**:
  * Default Accounts:
    * `110201` -> حساب مراقبة العملاء المحليين (`AR - Local Customers Control`).
    * `210101` -> حساب مراقبة الموردين المحليين (`AP - Local Vendors Control`).
  * `Customer.FinancialAccountId`: Defaults to the category's control account (`CustomerCategory.ControlAccountId`), or a specific assigned account.
  * `Vendor.FinancialAccountId`: Defaults to the category's control account (`VendorCategory.ControlAccountId`), or a specific assigned account.
  * When a customer or vendor is selected in any transaction, the system automatically retrieves their `FinancialAccountId` for posting.

### 9.3 Account Usage Restriction (`AccountUsage`)
Restricts accounts to specific document types:
* `1 = All`
* `2 = JournalVoucher` (قيود اليومية فقط)
* `3 = Payment` (سندات الصرف فقط)
* `4 = Receipt` (سندات القبض فقط)

### 9.4 Cost Centers
* **Entity**: `CostCenter` (Hierarchical tree, `IsTransactional`).
* **Enforcement**: If `GLAccount.RequiresCostCenter == true`, journal lines touching this account require a valid `CostCenterId`.

### 9.5 Journal Entries (`JournalEntryHeader` & `JournalEntryLine`)
* **Source Document Types (`SourceDocumentType`)**:
  * `1 = ManualJV` (قيد يومية يدوي)
  * `2 = SalesInvoice` (فاتورة مبيعات)
  * `3 = PurchaseInvoice` (فاتورة مشتريات)
  * `4 = ReceiptVoucher` (سند قبض)
  * `5 = PaymentVoucher` (سند صرف)
  * `6 = CurrencyRevaluation` (إعادة تقييم عملة)
* **Source Document Immutability**:
  Entries generated from source documents (`SourceDocumentType != ManualJV`) **cannot** be modified or deleted directly from the Journal Entries screen; they must be updated or cancelled from the originating source document.
* **Double-Entry Balance Invariant**:
  $$\sum \text{DebitLocal} == \sum \text{CreditLocal}$$
  Checked to 4 decimal places before committing.
* **Validation Pipeline (`IJournalEntryValidationService`)**:
  Validates: (1) Fiscal year not closed, (2) Double-entry local balance, (3) Minimum 2 lines, (4) Accounts active and non-group, (5) Branch scoping matching `header.BranchId`, (6) Currency matching, (7) Account usage authorization, (8) Cost center requirements, (9) Reconciliation/sub-ledger requirements, (10) User posting authorization (`CanPost`).

### 9.6 Branch-Scoped GL Accounts (حسابات الفروع والصناديق المخصصة)
* **Unified Tree Design**: `GLAccount` does **not** implement `IMustHaveBranch`. The Chart of Accounts remains unified across all branches.
* **Nullable Branch Assignment (`BranchId`)**:
  * `GLAccount.BranchId == null`: Global / Enterprise account (visible and usable in all branches).
  * `GLAccount.BranchId != null`: Branch-dedicated transactional account (e.g., local branch cashboxes `صندوق فرع القاهرة`, branch petty cash).
  * **Group Rule**: All group accounts (`IsGroup == true`) **must** have `BranchId == null` to maintain the unified hierarchy structure.
* **Ledger Integrity & Immutability**:
  * If an account has posted transactions (`JournalEntryLines`), changing its `BranchId` is locked to prevent corrupting historical branch financial statements.
* **Security & Lookup Scoping**:
  * Single-branch users are restricted to `BranchId == null || BranchId == user.BranchId`.
  * Multi-branch administrators (`HasAllBranches == true`) can filter accounts by branch in both the interactive Tree view and the DataTables list view.
  * Search lookups (`LookupService` / Select2) automatically filter out accounts belonging to other branches and label branch-specific accounts with branch badges.
* **Posting & Voucher Validation Guards**:
  * `JournalEntryValidationService` throws an `InvalidOperationException` if a journal line references an account scoped to a different branch than `header.BranchId`.
  * `CashBankVouchersController` validates that the treasury account belongs either to the selected branch or is enterprise-wide (`BranchId == null`).

### 9.7 Hierarchical Bottom-Up Roll-Up in Accounting Reports (الترصيد التجميعي الشجري)
* **Trial Balance Group Aggregation**:
  * Group accounts (`IsGroup == true`) cannot receive direct journal lines.
  * Their opening balances, period movements (Debit & Credit), and closing balances are dynamically calculated using a **Bottom-Up Roll-Up algorithm** sorted by `Level DESC`.
  * Every parent group aggregates the full balances of all its descendant child and grandchild accounts.
* **Level Filtering (`AccountLevel`)**:
  * When filtering by tree level (e.g. `AccountLevel = 2`), Level 1 and Level 2 groups display the complete rolled-up totals of their deeper sub-accounts.
* **Grand Totals Integrity**:
  * Grand totals in the Trial Balance are calculated across all leaf accounts to guarantee mathematical and accounting balance invariants ($\sum \text{Debit} == \sum \text{Credit}$) regardless of the chosen level filter.

---

## 10. Cash & Bank Voucher Module Architecture (Phase 5)

### 10.1 Unified Architecture
* Combines **Receipt Vouchers** (سندات القبض) and **Payment Vouchers** (سندات الصرف) into a single unified module with adaptive UI styling.
* Header Entity: `VoucherHeader` (`IMustHaveBranch`, `IAuditableEntity`, `IConcurrencyAware`).
* Detail Lines: `VoucherLine` (`IAuditableEntity`).

### 10.2 Supported Line Types
Each voucher line can target:
1. `Customer`: Automatically resolves `Customer.FinancialAccountId` (Control Account).
2. `Vendor`: Automatically resolves `Vendor.FinancialAccountId` (Control Account).
3. `Account`: Direct GL detail account from chart of accounts.
4. `Bank`: Bank financial account.

### 10.3 Automated Posting Logic
* **Receipt Voucher (سند قبض)**:
  * Debit: Treasury / Bank Account (`voucher.CashBankAccountId`).
  * Credit: Each detail line's resolved GL Account (`line.AccountId`).
* **Payment Voucher (سند صرف)**:
  * Debit: Each detail line's resolved GL Account (`line.AccountId`).
  * Credit: Treasury / Bank Account (`voucher.CashBankAccountId`).
* **Linking**:
  * Sets `voucher.Status = VoucherStatus.Posted`.
  * Generates `JournalEntryHeader` with `SourceDocumentType = ReceiptVoucher | PaymentVoucher` and `SourceDocumentId = voucher.Id`.
  * Stores `voucher.JournalEntryHeaderId = jvHeader.Id`.
* **Unposting**:
  * Deletes or reverses the linked `JournalEntryHeader`.
  * Resets `voucher.Status = VoucherStatus.Draft` and clears `JournalEntryHeaderId`.

---

## 11. Searchable Lookups & API Conventions

* **Endpoint**: `/api/Lookup/{type}` (e.g., `customer`, `vendor`, `account`, `costCenter`, `branch`, `currency`, `user`).
* **Parameters**: `q` (search query), `branchId` (branch filter), `page` (1-based pagination).
* **Security**: Enforces `ICurrentUserContext.CanAccessBranch(branchId)` on all lookups.
* **Payload**:
  ```json
  {
    "results": [
      { "id": 1, "text": "CUS001 - شركة الأمل (Al Amal Co)", "code": "CUS001", "name": "شركة الأمل" }
    ],
    "pagination": { "more": false }
  }
  ```
* **Client Helper**: Standardized in `erp-lookup.js` with debounce (300ms) and minimum 1-character search.

---

## 12. Multi-Currency Engine

* Base currency is Egyptian Pound (`EGP`), ExchangeRate = 1.0.
* Supported transaction currencies: `USD`, `EUR`, `SAR`, etc.
* Invariant: All financial transactions record both foreign amounts and converted local amounts:
  $$\text{AmountLocal} = \text{Round}(\text{AmountForeign} \times \text{ExchangeRate}, 4)$$
* Accounts may specify a fixed `CurrencyId` (transactions must match this currency) or `null` (accepts multi-currency transactions).

---

## 13. UI Design & Localization Standards

* **Default Culture**: `ar-EG` (Arabic - Egypt). Secondary: `en-US`.
* **RTL Layout**: Automatic RTL stylesheet switching (`bootstrap.rtl.min.css`) based on current UI culture.
* **Component Styling**:
  * Primary / Submit: Indigo / Blue (`btn-primary`).
  * Success / Post / Receipt: Green (`btn-success`).
  * Warning / Draft: Amber / Yellow (`badge bg-warning`).
  * Danger / Delete / Payment: Red (`btn-danger`).
* **Print Views**: Clean A4 printable layouts with corporate headers, Arabic Tafqeet (تفقيط المبالغ بالكلمات), line details, and mandatory four-part signature blocks (Cashier, Accountant, Financial Manager, Recipient).

---

---

## 15. Payment Method Permissions & Excel Copy/Paste Architecture (صلاحيات وسائل الدفع ومحرك النسخ واللصق من إكسيل)

### 15.1 Line-Level Multi-Payment-Method Design (تصميم وسائل الدفع على مستوى السطر)
* **المفهوم المحاسبي**: لم يعد السند يقتصر على وسيلة دفع واحدة في الـ Header؛ بل يمكن للسند الواحد (سواء قبض أو صرف) أن يحتوي على سطور متباينة:
  * نقداً (`Cash`)
  * شيك واحد أو عدة شيكات (`Cheque`)
  * تحويل بنكي (`BankTransfer`)
  * أخرى (`Other`)
* **بيانات الشيك في السطر**:
  عند اختيار `PaymentMethod == Cheque` تظهر الحقول الديناميكية: `ChequeNumber`, `ChequeDate`, `DueDate`, `BankId`, `BankName`, `DrawerName`.
  وعند اختيار `Cash` أو `BankTransfer` أو `Other` تختفي حقول الشيك وتفرغ قيمها تلقائياً.
* **التوافق المستقبلي لحالات الشيك**:
  يدعم حقل `ChequeStatus` حالات: `None`, `UnderCollection` (تحت التحصيل), `Collected` (محصل), `Returned` (مرتد), `Cancelled` (ملغى).

### 15.2 User Payment Method Permissions (صلاحيات وسائل الدفع للمستخدمين)
* **الجدول والكيان**: `UserPaymentMethodPermission`
  * الحقول: `UserId`, `VoucherType?`, `PaymentMethod`, `CanView`, `CanCreate`, `CanEdit`, `CanDelete`, `CanPost`, `CanUnpost`.
  * الفهرس الفريد: `IX_UserPaymentMethodPermissions_UserId_VoucherType_PaymentMethod`.
* **الخدمة والتخزين المؤقت**:
  * واجهة `IPaymentMethodPermissionService` وتطبيقها `PaymentMethodPermissionService`.
  * تخزين مؤقت باستخدام `IMemoryCache` بمدة انزلاق 10 دقائق ومفتاح فريد لكل مستخدم ونوع سند مع إبطال الكاش عند أي حفظ.
  * **سياسة التوافق التراجعي (Backward Compatibility)**: المستخدمون الذين ليس لديهم أي قيود مسجلة في الجدول يتمتعون بصلاحية كاملة تلقائياً. مدير النظام العام (`SuperAdmin` / `HasAllBranches`) يتجاوز القيود.
  * **التحقق الصارم في العمليات**:
    * عند الإضافة (`Create`): يتم التحقق من امتلاك المستخدم صلاحية `CanCreate` لكل وسيلة دفع في سطور السند.
    * عند التعديل (`Edit`): يتم التحقق من صلاحية `CanEdit` لكل وسائل الدفع الموجودة سابقاً والجديدة.
    * عند الترحيل (`Post`): لا يمكن ترحيل سند مختلط (مثل نقدي + شيك) إلا إذا كان المستخدم يملك صلاحية `CanPost` لجميع وسائل الدفع الموجودة بالسطور.
    * عند إلغاء الترحيل أو الحذف أو الإلغاء (`Unpost`, `Delete`, `Cancel`): يتم التحقق الصارم بنفس المبدأ.
* **واجهة الإدارة والقوالب الجاهزة**:
  * شاشة الإدارة: `/PaymentMethodPermissions` مع مصفوفة تفاعلية سريعة.
  * قوالب سريعة بزر واحد: موظف خزينة (Cashier)، موظف شيكات (Cheque Officer)، موظف بنوك (Bank Officer)، صلاحية كاملة (Full Access)، ومسح الكل.

### 15.3 Reusable Excel Copy/Paste Engine (`erp-grid-paste.js`)
* **الأداء والمعالجة الفورية**: إمكانية إدخال ولصق 10، 20، 50 إلى 100 شيك أو بند من جدول Excel مباشرة داخل ثوانٍ معدودة.
* **التعرف التلقائي الذكي**:
  * التعرف على طريقة الدفع من العمود الأول باللغات العربية والإنجليزية والأرقام (`شيك`, `Cheque`, `نقدي`, `Cash`, `تحويل`, إلخ).
  * دعم الأرقام العربية والإنجليزية والفواصل والأقواس للقيم السالبة.
  * استخراج تواريخ الشيكات والاستحقاق بصيغ متعددة (`YYYY-MM-DD`, `DD/MM/YYYY`, `MM/DD/YYYY`).
* **حل المطابقات بدفعة واحدة (Single-Roundtrip Batch Resolution)**:
  * يرسل محرك اللصق جميع أكواد وأسماء البنوك والعملاء والموردين في طلب AJAX واحد (`POST /api/Lookups/resolve-batch`) بدلاً من إرسال عشرات الطلبات، مع احترام عزل الفرع الحالي.
* **نافذة المعاينة والتحقق التفاعلية**:
  * نافذة معاينة تعرض حالة كل سطر (صالح / به أخطاء) مع تفاصيل الخطأ (رقم الشيك مفقود، البنك غير موجود، المبلغ صفر).
  * خيار الاستبدال (`Replace`) أو الإضافة (`Append`).
  * تصفية السطور لعرض السطور التي بها أخطاء فقط لتصحيحها.
* **إعادة الاستخدام للمستقبل (Future Scalability)**:
  * المحرك مصمم كمكتبة عامة مستقلة (`ErpGridPaste`) يمكن استخدامها بنفس الطريقة وبمخططات بيانات مخصصة (`schemas`) في:
    1. قيود اليومية (`Journal Entries`)
    2. سندات القبض والصرف (`Receipt & Payment Vouchers`)
    3. فواتير المبيعات (`Sales Invoices`)
    4. فواتير المشتريات (`Purchase Invoices`)

---

## 16. Summary of Implemented Phases & Milestones

| Phase | Milestone | Core Deliverables | Status |
|---|---|---|---|
| **Phase 1** | Identity & Security | Users, Roles, Claims, Granular Permissions (`[HasPermission]`) | Completed |
| **Phase 2** | Organization & Multi-Branch | 29 Sister Companies, Multi-Branch Security, `IMustHaveBranch`, Fiscal Years | Completed |
| **Phase 3** | Master Data Foundation | Governorates, Cities, Salesmen, Currencies, Categories, Customers, Vendors, Units, Items, Warehouses, `CodeSequences` | Completed |
| **Phase 4** | Core Accounting & GL | Chart of Accounts, Control Accounts, Sub-Ledgers, Cost Centers, Journal Entries, Double-Entry Balance Engine, Audit Trail, Accounting Reports, Optimistic Concurrency | Completed |
| **Phase 5** | Cash & Bank Vouchers | Unified Receipt & Payment Vouchers, Auto Control Account Fetch, Auto GL Posting/Unposting, Multi-Currency, Cheque Tracking, Print & PDF | Completed |
| **Phase 6** | Payment Method Permissions & Excel Grid Engine | Line-level Payment Method Permissions Matrix, Cashier/Cheque/Bank Presets, Reusable Excel Copy/Paste Grid Engine, Batch Lookup Resolution | Completed |
| **Phase 7** | Sales & Purchases | Quotations, Orders, Invoices, Delivery Notes, Returns | Upcoming |
| **Phase 8** | Inventory & Costing | Stock Transactions, Costing Methods (FIFO/Weighted Avg), Transfers, Adjustments | Upcoming |

---
*This document is the official authoritative architectural blueprint for ERP 2026. All new modules, entities, endpoints, and database migrations must strictly adhere to the rules, naming conventions, and security patterns set forth herein.*
