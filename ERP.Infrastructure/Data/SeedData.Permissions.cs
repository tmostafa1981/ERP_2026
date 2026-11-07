using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Data
{
    public static partial class SeedData
    {
        public static async Task EnsurePermissionsAndRolesAsync(ErpDbContext context, RoleManager<ApplicationRole>? roleManager = null)
        {
            // ─── 1. Seed Roles ────────────────────────────────────────────────────────
            var defaultRoles = new (string Name, string NameAr, string NameEn, string Desc)[]
            {
                ("Admin", "مدير النظام", "System Administrator", "Full system access & administration"),
                ("Company Manager", "مدير شركة", "Company Manager", "Managerial access for sister companies"),
                ("Branch Manager", "مدير فرع", "Branch Manager", "Operational branch management"),
                ("Account Manager", "مدير الحسابات", "Account Manager", "Financial and accounting control"),
                ("Sales Manager", "مدير المبيعات", "Sales Manager", "Sales module supervision"),
                ("Sales User", "مستخدم مبيعات", "Sales User", "Sales entry & processing"),
                ("Purchase Manager", "مدير المشتريات", "Purchase Manager", "Purchase module supervision"),
                ("Purchase User", "مستخدم مشتريات", "Purchase User", "Purchase entry & orders"),
                ("Inventory Manager", "مدير المخازن", "Inventory Manager", "Stock control & transfer manager"),
                ("Inventory User", "أمين مخزن", "Inventory Storekeeper", "Stock entry & issue"),
                ("Cashier", "أمين صندوق", "Cashier", "POS & daily cash voucher handler"),
                ("Auditor", "مراجع حسابات", "Auditor", "Read-only financial audit & report inspector"),
                ("Viewer", "مستعرض بيانات", "Data Viewer", "General read-only viewing access")
            };

            foreach (var r in defaultRoles)
            {
                var roleExists = roleManager != null
                    ? await roleManager.FindByNameAsync(r.Name)
                    : await context.Roles.FirstOrDefaultAsync(ro => ro.Name == r.Name);

                if (roleExists == null)
                {
                    var newRole = new ApplicationRole(r.Name)
                    {
                        NameAr = r.NameAr,
                        NameEn = r.NameEn,
                        Description = r.Desc,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    if (roleManager != null)
                    {
                        await roleManager.CreateAsync(newRole);
                    }
                    else
                    {
                        newRole.NormalizedName = r.Name.ToUpper();
                        context.Roles.Add(newRole);
                        await context.SaveChangesAsync();
                    }
                }
            }

            // ─── 2. Seed Permissions ──────────────────────────────────────────────────
            var permissionDefinitions = new (string Code, string Module, string Screen, string Action, string NameAr, string NameEn, int Sort)[]
            {
                // Administration
                ("Administration.Users.View", "Administration", "Users", "View", "عرض المستخدمين", "View Users", 1),
                ("Administration.Users.Add", "Administration", "Users", "Add", "إضافة مستخدم", "Add User", 2),
                ("Administration.Users.Edit", "Administration", "Users", "Edit", "تعديل مستخدم", "Edit User", 3),
                ("Administration.Users.Delete", "Administration", "Users", "Delete", "حذف مستخدم", "Delete User", 4),
                ("Administration.Users.Export", "Administration", "Users", "Export", "تصدير المستخدمين", "Export Users", 5),

                ("Administration.Roles.View", "Administration", "Roles", "View", "عرض الأدوار", "View Roles", 6),
                ("Administration.Roles.Add", "Administration", "Roles", "Add", "إضافة دور", "Add Role", 7),
                ("Administration.Roles.Edit", "Administration", "Roles", "Edit", "تعديل دور", "Edit Role", 8),
                ("Administration.Roles.Delete", "Administration", "Roles", "Delete", "حذف دور", "Delete Role", 9),

                ("Administration.Permissions.View", "Administration", "Permissions", "View", "عرض الصلاحيات", "View Permissions", 10),
                ("Administration.Permissions.Edit", "Administration", "Permissions", "Edit", "تخصيص الصلاحيات", "Edit Permissions", 11),

                ("Administration.UserBranch.View", "Administration", "UserBranch", "View", "عرض ربط الفروع", "View User Branches", 14),
                ("Administration.UserBranch.Edit", "Administration", "UserBranch", "Edit", "تعديل ربط الفروع", "Edit User Branches", 15),

                ("Administration.PaymentMethodPermissions.View", "Administration", "PaymentMethodPermissions", "View", "عرض صلاحيات وسائل الدفع", "View Payment Method Permissions", 16),
                ("Administration.PaymentMethodPermissions.Edit", "Administration", "PaymentMethodPermissions", "Edit", "تعديل صلاحيات وسائل الدفع", "Edit Payment Method Permissions", 17),

                // Organization
                ("Organization.Company.View", "Organization", "Company", "View", "عرض بيانات الشركة", "View Company Info", 20),
                ("Organization.Company.Edit", "Organization", "Company", "Edit", "تعديل بيانات الشركة", "Edit Company Info", 21),

                ("Organization.Branch.View", "Organization", "Branch", "View", "عرض الفروع", "View Branches", 22),
                ("Organization.Branch.Add", "Organization", "Branch", "Add", "إضافة فرع", "Add Branch", 23),
                ("Organization.Branch.Edit", "Organization", "Branch", "Edit", "تعديل فرع", "Edit Branch", 24),
                ("Organization.Branch.Delete", "Organization", "Branch", "Delete", "حذف فرع", "Delete Branch", 25),

                ("Organization.FiscalYear.View", "Organization", "FiscalYear", "View", "عرض السنوات المالية", "View Fiscal Years", 26),
                ("Organization.FiscalYear.Add", "Organization", "FiscalYear", "Add", "إضافة سنة مالية", "Add Fiscal Year", 27),
                ("Organization.FiscalYear.Edit", "Organization", "FiscalYear", "Edit", "تعديل سنة مالية", "Edit Fiscal Year", 28),
                ("Organization.FiscalYear.Close", "Organization", "FiscalYear", "Approve", "إغلاق سنة مالية", "Close Fiscal Year", 29),

                // Sales
                ("Sales.SalesInvoice.View", "Sales", "SalesInvoice", "View", "عرض فواتير المبيعات", "View Sales Invoices", 30),
                ("Sales.SalesInvoice.Add", "Sales", "SalesInvoice", "Add", "إضافة فاتورة مبيعات", "Add Sales Invoice", 31),
                ("Sales.SalesInvoice.Edit", "Sales", "SalesInvoice", "Edit", "تعديل فاتورة مبيعات", "Edit Sales Invoice", 32),
                ("Sales.SalesInvoice.Delete", "Sales", "SalesInvoice", "Delete", "حذف فاتورة مبيعات", "Delete Sales Invoice", 33),
                ("Sales.SalesInvoice.Print", "Sales", "SalesInvoice", "Print", "طباعة فاتورة مبيعات", "Print Sales Invoice", 34),
                ("Sales.SalesInvoice.Approve", "Sales", "SalesInvoice", "Approve", "اعتماد فاتورة مبيعات", "Approve Sales Invoice", 35),
                ("Sales.SalesInvoice.Cancel", "Sales", "SalesInvoice", "Cancel", "إلغاء فاتورة مبيعات", "Cancel Sales Invoice", 36),
                ("Sales.SalesInvoice.Post", "Sales", "SalesInvoice", "Post", "ترحيل فاتورة مبيعات", "Post Sales Invoice", 37),
                ("Sales.SalesInvoice.Export", "Sales", "SalesInvoice", "Export", "تصدير فواتير المبيعات", "Export Sales Invoices", 38),

                // Purchases
                ("Purchases.PurchaseInvoice.View", "Purchases", "PurchaseInvoice", "View", "عرض فواتير المشتريات", "View Purchase Invoices", 40),
                ("Purchases.PurchaseInvoice.Add", "Purchases", "PurchaseInvoice", "Add", "إضافة فاتورة مشتريات", "Add Purchase Invoice", 41),
                ("Purchases.PurchaseInvoice.Edit", "Purchases", "PurchaseInvoice", "Edit", "تعديل فاتورة مشتريات", "Edit Purchase Invoice", 42),
                ("Purchases.PurchaseInvoice.Delete", "Purchases", "PurchaseInvoice", "Delete", "حذف فاتورة مشتريات", "Delete Purchase Invoice", 43),
                ("Purchases.PurchaseInvoice.Print", "Purchases", "PurchaseInvoice", "Print", "طباعة فاتورة مشتريات", "Print Purchase Invoice", 44),
                ("Purchases.PurchaseInvoice.Approve", "Purchases", "PurchaseInvoice", "Approve", "اعتماد فاتورة مشتريات", "Approve Purchase Invoice", 45),
                ("Purchases.PurchaseInvoice.Cancel", "Purchases", "PurchaseInvoice", "Cancel", "إلغاء فاتورة مشتريات", "Cancel Purchase Invoice", 46),
                ("Purchases.PurchaseInvoice.Post", "Purchases", "PurchaseInvoice", "Post", "ترحيل فاتورة مشتريات", "Post Purchase Invoice", 47),

                // Inventory
                ("Inventory.Items.View", "Inventory", "Items", "View", "عرض أصناف المخزون", "View Inventory Items", 50),
                ("Inventory.Items.Add", "Inventory", "Items", "Add", "إضافة صنف", "Add Inventory Item", 51),
                ("Inventory.Items.Edit", "Inventory", "Items", "Edit", "تعديل صنف", "Edit Inventory Item", 52),
                ("Inventory.Items.Delete", "Inventory", "Items", "Delete", "حذف صنف", "Delete Inventory Item", 53),
                ("Inventory.StockTransfer.View", "Inventory", "StockTransfer", "View", "عرض التحويلات المخزنية", "View Stock Transfers", 54),
                ("Inventory.StockTransfer.Add", "Inventory", "StockTransfer", "Add", "إضافة تحويل مخزني", "Add Stock Transfer", 55),
                ("Inventory.StockTransfer.Approve", "Inventory", "StockTransfer", "Approve", "اعتماد تحويل مخزني", "Approve Stock Transfer", 56),

                // Accounting
                ("Accounting.ChartOfAccounts.View", "Accounting", "ChartOfAccounts", "View", "عرض شجرة الحسابات", "View Chart of Accounts", 55),
                ("Accounting.ChartOfAccounts.Add", "Accounting", "ChartOfAccounts", "Add", "إضافة حساب/مجموعة", "Add Account/Group", 56),
                ("Accounting.ChartOfAccounts.Edit", "Accounting", "ChartOfAccounts", "Edit", "تعديل حساب/مجموعة", "Edit Account/Group", 57),
                ("Accounting.ChartOfAccounts.Delete", "Accounting", "ChartOfAccounts", "Delete", "حذف حساب/مجموعة", "Delete Account/Group", 58),

                ("Accounting.JournalVoucher.View", "Accounting", "JournalVoucher", "View", "عرض قيود اليومية", "View Journal Vouchers", 60),
                ("Accounting.JournalVoucher.Add", "Accounting", "JournalVoucher", "Add", "إضافة قيد يومية", "Add Journal Voucher", 61),
                ("Accounting.JournalVoucher.Edit", "Accounting", "JournalVoucher", "Edit", "تعديل قيد يومية", "Edit Journal Voucher", 62),
                ("Accounting.JournalVoucher.Post", "Accounting", "JournalVoucher", "Post", "ترحيل قيد يومية", "Post Journal Voucher", 63),
                ("Accounting.JournalVoucher.Delete", "Accounting", "JournalVoucher", "Delete", "إلغاء وحذف قيد يومية", "Delete Journal Voucher", 64),

                // Cost Centers
                ("Accounting.CostCenters.View", "Accounting", "CostCenters", "View", "عرض مراكز التكلفة", "View Cost Centers", 65),
                ("Accounting.CostCenters.Add", "Accounting", "CostCenters", "Add", "إضافة مركز تكلفة", "Add Cost Center", 66),
                ("Accounting.CostCenters.Edit", "Accounting", "CostCenters", "Edit", "تعديل مركز تكلفة", "Edit Cost Center", 67),
                ("Accounting.CostCenters.Delete", "Accounting", "CostCenters", "Delete", "حذف مركز تكلفة", "Delete Cost Center", 68),

                // Master Data - Locations
                ("MasterData.Governorates.View", "MasterData", "Governorates", "View", "عرض المحافظات", "View Governorates", 70),
                ("MasterData.Governorates.Add", "MasterData", "Governorates", "Add", "إضافة محافظة", "Add Governorate", 71),
                ("MasterData.Governorates.Edit", "MasterData", "Governorates", "Edit", "تعديل محافظة", "Edit Governorate", 72),
                ("MasterData.Governorates.Delete", "MasterData", "Governorates", "Delete", "حذف محافظة", "Delete Governorate", 73),

                ("MasterData.Cities.View", "MasterData", "Cities", "View", "عرض المدن", "View Cities", 74),
                ("MasterData.Cities.Add", "MasterData", "Cities", "Add", "إضافة مدينة", "Add City", 75),
                ("MasterData.Cities.Edit", "MasterData", "Cities", "Edit", "تعديل مدينة", "Edit City", 76),
                ("MasterData.Cities.Delete", "MasterData", "Cities", "Delete", "حذف مدينة", "Delete City", 77),

                // Master Data - Commercial
                ("MasterData.Salesmen.View", "MasterData", "Salesmen", "View", "عرض المناديب", "View Salesmen", 80),
                ("MasterData.Salesmen.Add", "MasterData", "Salesmen", "Add", "إضافة مندوب", "Add Salesman", 81),
                ("MasterData.Salesmen.Edit", "MasterData", "Salesmen", "Edit", "تعديل مندوب", "Edit Salesman", 82),
                ("MasterData.Salesmen.Delete", "MasterData", "Salesmen", "Delete", "حذف مندوب", "Delete Salesman", 83),

                ("MasterData.Currencies.View", "MasterData", "Currencies", "View", "عرض العملات", "View Currencies", 84),
                ("MasterData.Currencies.Add", "MasterData", "Currencies", "Add", "إضافة عملة", "Add Currency", 85),
                ("MasterData.Currencies.Edit", "MasterData", "Currencies", "Edit", "تعديل عملة", "Edit Currency", 86),
                ("MasterData.Currencies.Delete", "MasterData", "Currencies", "Delete", "حذف عملة", "Delete Currency", 87),

                // Master Data - Business Partners
                ("MasterData.CustomerCategories.View", "MasterData", "CustomerCategories", "View", "عرض تصنيفات العملاء", "View Customer Categories", 90),
                ("MasterData.CustomerCategories.Add", "MasterData", "CustomerCategories", "Add", "إضافة تصنيف عملاء", "Add Customer Category", 91),
                ("MasterData.CustomerCategories.Edit", "MasterData", "CustomerCategories", "Edit", "تعديل تصنيف عملاء", "Edit Customer Category", 92),
                ("MasterData.CustomerCategories.Delete", "MasterData", "CustomerCategories", "Delete", "حذف تصنيف عملاء", "Delete Customer Category", 93),

                ("MasterData.Customers.View", "MasterData", "Customers", "View", "عرض العملاء", "View Customers", 94),
                ("MasterData.Customers.Add", "MasterData", "Customers", "Add", "إضافة عميل", "Add Customer", 95),
                ("MasterData.Customers.Edit", "MasterData", "Customers", "Edit", "تعديل عميل", "Edit Customer", 96),
                ("MasterData.Customers.Delete", "MasterData", "Customers", "Delete", "حذف عميل", "Delete Customer", 97),

                ("MasterData.VendorCategories.View", "MasterData", "VendorCategories", "View", "عرض تصنيفات الموردين", "View Vendor Categories", 100),
                ("MasterData.VendorCategories.Add", "MasterData", "VendorCategories", "Add", "إضافة تصنيف موردين", "Add Vendor Category", 101),
                ("MasterData.VendorCategories.Edit", "MasterData", "VendorCategories", "Edit", "تعديل تصنيف موردين", "Edit Vendor Category", 102),
                ("MasterData.VendorCategories.Delete", "MasterData", "VendorCategories", "Delete", "حذف تصنيف موردين", "Delete Vendor Category", 103),

                ("MasterData.Vendors.View", "MasterData", "Vendors", "View", "عرض الموردين", "View Vendors", 104),
                ("MasterData.Vendors.Add", "MasterData", "Vendors", "Add", "إضافة مورد", "Add Vendor", 105),
                ("MasterData.Vendors.Edit", "MasterData", "Vendors", "Edit", "تعديل مورد", "Edit Vendor", 106),
                ("MasterData.Vendors.Delete", "MasterData", "Vendors", "Delete", "حذف مورد", "Delete Vendor", 107),

                // Master Data - Items & Inventory
                ("MasterData.Units.View", "MasterData", "Units", "View", "عرض الوحدات", "View Units", 110),
                ("MasterData.Units.Add", "MasterData", "Units", "Add", "إضافة وحدة", "Add Unit", 111),
                ("MasterData.Units.Edit", "MasterData", "Units", "Edit", "تعديل وحدة", "Edit Unit", 112),
                ("MasterData.Units.Delete", "MasterData", "Units", "Delete", "حذف وحدة", "Delete Unit", 113),

                ("MasterData.ItemGroups.View", "MasterData", "ItemGroups", "View", "عرض مجموعات الأصناف", "View Item Groups", 114),
                ("MasterData.ItemGroups.Add", "MasterData", "ItemGroups", "Add", "إضافة مجموعة أصناف", "Add Item Group", 115),
                ("MasterData.ItemGroups.Edit", "MasterData", "ItemGroups", "Edit", "تعديل مجموعة أصناف", "Edit Item Group", 116),
                ("MasterData.ItemGroups.Delete", "MasterData", "ItemGroups", "Delete", "حذف مجموعة أصناف", "Delete Item Group", 117),

                ("MasterData.Items.View", "MasterData", "Items", "View", "عرض بيانات الأصناف", "View Items", 118),
                ("MasterData.Items.Add", "MasterData", "Items", "Add", "إضافة صنف جديد", "Add Item", 119),
                ("MasterData.Items.Edit", "MasterData", "Items", "Edit", "تعديل صنف", "Edit Item", 120),
                ("MasterData.Items.Delete", "MasterData", "Items", "Delete", "حذف صنف", "Delete Item", 121),

                ("MasterData.ItemUnits.View", "MasterData", "ItemUnits", "View", "عرض وحدات الأصناف", "View Item Units", 122),
                ("MasterData.ItemUnits.Add", "MasterData", "ItemUnits", "Add", "إضافة وحدة لصنف", "Add Item Unit", 123),
                ("MasterData.ItemUnits.Edit", "MasterData", "ItemUnits", "Edit", "تعديل وحدة صنف", "Edit Item Unit", 124),
                ("MasterData.ItemUnits.Delete", "MasterData", "ItemUnits", "Delete", "حذف وحدة صنف", "Delete Item Unit", 125),

                ("MasterData.Warehouses.View", "MasterData", "Warehouses", "View", "عرض المخازن", "View Warehouses", 126),
                ("MasterData.Warehouses.Add", "MasterData", "Warehouses", "Add", "إضافة مخزن", "Add Warehouse", 127),
                ("MasterData.Warehouses.Edit", "MasterData", "Warehouses", "Edit", "تعديل مخزن", "Edit Warehouse", 128),
                ("MasterData.Warehouses.Delete", "MasterData", "Warehouses", "Delete", "حذف مخزن", "Delete Warehouse", 129),

                // Cash & Bank Vouchers
                ("Cash.ReceiptVoucher.View", "Cash", "ReceiptVoucher", "View", "عرض سندات القبض", "View Receipt Vouchers", 130),
                ("Cash.ReceiptVoucher.Add", "Cash", "ReceiptVoucher", "Add", "إضافة سند قبض", "Add Receipt Voucher", 131),
                ("Cash.ReceiptVoucher.Edit", "Cash", "ReceiptVoucher", "Edit", "تعديل سند قبض", "Edit Receipt Voucher", 132),
                ("Cash.ReceiptVoucher.Delete", "Cash", "ReceiptVoucher", "Delete", "حذف سند قبض", "Delete Receipt Voucher", 133),
                ("Cash.ReceiptVoucher.Post", "Cash", "ReceiptVoucher", "Post", "ترحيل سند قبض", "Post Receipt Voucher", 134),
                ("Cash.ReceiptVoucher.Unpost", "Cash", "ReceiptVoucher", "Unpost", "إلغاء ترحيل سند قبض", "Unpost Receipt Voucher", 135),
                ("Cash.ReceiptVoucher.Cancel", "Cash", "ReceiptVoucher", "Cancel", "إلغاء سند قبض", "Cancel Receipt Voucher", 136),
                ("Cash.ReceiptVoucher.Print", "Cash", "ReceiptVoucher", "Print", "طباعة سند قبض", "Print Receipt Voucher", 137),

                ("Cash.PaymentVoucher.View", "Cash", "PaymentVoucher", "View", "عرض سندات الصرف", "View Payment Vouchers", 140),
                ("Cash.PaymentVoucher.Add", "Cash", "PaymentVoucher", "Add", "إضافة سند صرف", "Add Payment Voucher", 141),
                ("Cash.PaymentVoucher.Edit", "Cash", "PaymentVoucher", "Edit", "تعديل سند صرف", "Edit Payment Voucher", 142),
                ("Cash.PaymentVoucher.Delete", "Cash", "PaymentVoucher", "Delete", "حذف سند صرف", "Delete Payment Voucher", 143),
                ("Cash.PaymentVoucher.Post", "Cash", "PaymentVoucher", "Post", "ترحيل سند صرف", "Post Payment Voucher", 144),
                ("Cash.PaymentVoucher.Unpost", "Cash", "PaymentVoucher", "Unpost", "إلغاء ترحيل سند صرف", "Unpost Payment Voucher", 145),
                ("Cash.PaymentVoucher.Cancel", "Cash", "PaymentVoucher", "Cancel", "إلغاء سند صرف", "Cancel Payment Voucher", 146),
                ("Cash.PaymentVoucher.Print", "Cash", "PaymentVoucher", "Print", "طباعة سند صرف", "Print Payment Voucher", 147),

                // Reports
                ("Reports.GeneralReports.View", "Reports", "GeneralReports", "View", "عرض فهرس التقارير العامة", "View General Reports Index", 150),
                ("Reports.AccountStatement.View", "Reports", "AccountStatement", "View", "عرض كشف حساب مالي", "View Account Statement", 151),
                ("Reports.CustomerStatement.View", "Reports", "CustomerStatement", "View", "عرض كشف حساب عميل", "View Customer Statement", 152),
                ("Reports.VendorStatement.View", "Reports", "VendorStatement", "View", "عرض كشف حساب مورد", "View Vendor Statement", 153),
                ("Reports.TrialBalance.View", "Reports", "TrialBalance", "View", "عرض ميزان المراجعة", "View Trial Balance", 154),
                ("Reports.GeneralLedger.View", "Reports", "GeneralLedger", "View", "عرض دفتر الأستاذ العام", "View General Ledger", 155),
                ("Reports.IncomeStatement.View", "Reports", "IncomeStatement", "View", "عرض قائمة الدخل", "View Income Statement", 156),
                ("Reports.BalanceSheet.View", "Reports", "BalanceSheet", "View", "عرض الميزانية العمومية", "View Balance Sheet", 157),
                ("Reports.SalesReports.View", "Reports", "SalesReports", "View", "عرض تقارير المبيعات", "View Sales Reports", 158),
                ("Reports.PurchaseReports.View", "Reports", "PurchaseReports", "View", "عرض تقارير المشتريات", "View Purchase Reports", 159),
                ("Reports.InventoryReports.View", "Reports", "InventoryReports", "View", "عرض تقارير المخازن", "View Inventory Reports", 160),

                // Audit History
                ("Administration.AuditHistory.View", "Administration", "AuditHistory", "View", "عرض سجل التدقيق والرقابة", "View Audit History", 170),

                // System Testing & Feedback
                ("Administration.SystemFeedback.View",   "Administration", "SystemFeedback", "View",   "عرض سجل ملاحظات واختبارات النظام", "View System Feedback & Testing", 171),
                ("Administration.SystemFeedback.Create", "Administration", "SystemFeedback", "Create", "إضافة ملاحظة أو اختبار جديد", "Create System Feedback & Testing", 172),
                ("Administration.SystemFeedback.Reply",  "Administration", "SystemFeedback", "Reply",  "الرد على الملاحظات وتحديث حالة الطلب (للمبرمجين)", "Reply to System Feedback & Change Status", 173)
            };

            var advancedReportAndScreenPermissions = new (string Code, string Module, string Screen, string Action, string NameAr, string NameEn, string Description, int Sort)[]
            {
                // ── Sales Reports ─────────────────────────────────────────────────────────────
                ("Sales.SalesReportsCatalog.View", "Sales", "SalesReportsCatalog", "View", "عرض فهرس تقارير المبيعات", "View Sales Reports Catalog", "فهرس تقارير المبيعات", 200),
                ("Sales.SalesSummary.View", "Sales", "SalesSummary", "View", "عرض تقرير ملخص المبيعات", "View Sales Summary", "ملخص المبيعات حسب الفترة والفرع", 201),
                ("Sales.SalesSummary.Print", "Sales", "SalesSummary", "Print", "طباعة تقرير ملخص المبيعات", "Print Sales Summary", "ملخص المبيعات حسب الفترة والفرع", 202),
                ("Sales.SalesSummary.Export", "Sales", "SalesSummary", "Export", "تصدير تقرير ملخص المبيعات", "Export Sales Summary", "ملخص المبيعات حسب الفترة والفرع", 203),

                ("Sales.InvoiceDetails.View", "Sales", "InvoiceDetails", "View", "عرض تفاصيل فواتير المبيعات", "View Sales Invoice Details", "تفاصيل فواتير البيع", 204),
                ("Sales.InvoiceDetails.Print", "Sales", "InvoiceDetails", "Print", "طباعة تفاصيل فواتير المبيعات", "Print Sales Invoice Details", "تفاصيل فواتير البيع", 205),
                ("Sales.InvoiceDetails.Export", "Sales", "InvoiceDetails", "Export", "تصدير تفاصيل فواتير المبيعات", "Export Sales Invoice Details", "تفاصيل فواتير البيع", 206),

                ("Sales.ByItem.View", "Sales", "ByItem", "View", "عرض المبيعات حسب الصنف", "View Sales By Item", "المبيعات حسب الصنف", 207),
                ("Sales.ByItem.Print", "Sales", "ByItem", "Print", "طباعة المبيعات حسب الصنف", "Print Sales By Item", "المبيعات حسب الصنف", 208),
                ("Sales.ByItem.Export", "Sales", "ByItem", "Export", "تصدير المبيعات حسب الصنف", "Export Sales By Item", "المبيعات حسب الصنف", 209),

                ("Sales.ByCustomer.View", "Sales", "ByCustomer", "View", "عرض المبيعات حسب العميل", "View Sales By Customer", "المبيعات حسب العميل", 210),
                ("Sales.ByCustomer.Print", "Sales", "ByCustomer", "Print", "طباعة المبيعات حسب العميل", "Print Sales By Customer", "المبيعات حسب العميل", 211),
                ("Sales.ByCustomer.Export", "Sales", "ByCustomer", "Export", "تصدير المبيعات حسب العميل", "Export Sales By Customer", "المبيعات حسب العميل", 212),

                ("Sales.BySalesman.View", "Sales", "BySalesman", "View", "عرض المبيعات حسب المندوب", "View Sales By Salesman", "المبيعات حسب المندوب", 213),
                ("Sales.BySalesman.Print", "Sales", "BySalesman", "Print", "طباعة المبيعات حسب المندوب", "Print Sales By Salesman", "المبيعات حسب المندوب", 214),
                ("Sales.BySalesman.Export", "Sales", "BySalesman", "Export", "تصدير المبيعات حسب المندوب", "Export Sales By Salesman", "المبيعات حسب المندوب", 215),

                ("Sales.ReturnsAndNet.View", "Sales", "ReturnsAndNet", "View", "عرض مرتجعات المبيعات والصافي", "View Sales Returns and Net", "مرتجعات المبيعات وصافي المبيعات", 216),
                ("Sales.ReturnsAndNet.Print", "Sales", "ReturnsAndNet", "Print", "طباعة مرتجعات المبيعات والصافي", "Print Sales Returns and Net", "مرتجعات المبيعات وصافي المبيعات", 217),
                ("Sales.ReturnsAndNet.Export", "Sales", "ReturnsAndNet", "Export", "تصدير مرتجعات المبيعات والصافي", "Export Sales Returns and Net", "مرتجعات المبيعات وصافي المبيعات", 218),

                ("Sales.BranchComparison.View", "Sales", "BranchComparison", "View", "عرض مقارنة المبيعات بين الفروع", "View Sales Branch Comparison", "مقارنة المبيعات بين الفروع والفترات", 219),
                ("Sales.BranchComparison.Print", "Sales", "BranchComparison", "Print", "طباعة مقارنة المبيعات بين الفروع", "Print Sales Branch Comparison", "مقارنة المبيعات بين الفروع والفترات", 220),
                ("Sales.BranchComparison.Export", "Sales", "BranchComparison", "Export", "تصدير مقارنة المبيعات بين الفروع", "Export Sales Branch Comparison", "مقارنة المبيعات بين الفروع والفترات", 221),

                ("Sales.TopSellingAndStagnant.View", "Sales", "TopSellingAndStagnant", "View", "عرض الأصناف الأكثر مبيعاً والراكدة", "View Top Selling and Stagnant Items", "الأصناف الأكثر مبيعاً والراكدة", 222),
                ("Sales.TopSellingAndStagnant.Print", "Sales", "TopSellingAndStagnant", "Print", "طباعة الأصناف الأكثر مبيعاً والراكدة", "Print Top Selling and Stagnant Items", "الأصناف الأكثر مبيعاً والراكدة", 223),
                ("Sales.TopSellingAndStagnant.Export", "Sales", "TopSellingAndStagnant", "Export", "تصدير الأصناف الأكثر مبيعاً والراكدة", "Export Top Selling and Stagnant Items", "الأصناف الأكثر مبيعاً والراكدة", 224),

                ("Sales.FulfillmentTracking.View", "Sales", "FulfillmentTracking", "View", "عرض متابعة الفواتير وتسليم المخزون", "View Sales Fulfillment Tracking", "متابعة الفواتير وتسليم المخزون", 225),
                ("Sales.FulfillmentTracking.Print", "Sales", "FulfillmentTracking", "Print", "طباعة متابعة الفواتير وتسليم المخزون", "Print Sales Fulfillment Tracking", "متابعة الفواتير وتسليم المخزون", 226),
                ("Sales.FulfillmentTracking.Export", "Sales", "FulfillmentTracking", "Export", "تصدير متابعة الفواتير وتسليم المخزون", "Export Sales Fulfillment Tracking", "متابعة الفواتير وتسليم المخزون", 227),

                // ── Purchase Reports ──────────────────────────────────────────────────────────
                ("Purchases.PurchaseReportsCatalog.View", "Purchases", "PurchaseReportsCatalog", "View", "عرض فهرس تقارير المشتريات", "View Purchase Reports Catalog", "فهرس تقارير المشتريات", 230),
                ("Purchases.PurchaseSummary.View", "Purchases", "PurchaseSummary", "View", "عرض تقرير ملخص المشتريات", "View Purchase Summary", "ملخص المشتريات حسب الفترة والفرع", 231),
                ("Purchases.PurchaseSummary.Print", "Purchases", "PurchaseSummary", "Print", "طباعة تقرير ملخص المشتريات", "Print Purchase Summary", "ملخص المشتريات حسب الفترة والفرع", 232),
                ("Purchases.PurchaseSummary.Export", "Purchases", "PurchaseSummary", "Export", "تصدير تقرير ملخص المشتريات", "Export Purchase Summary", "ملخص المشتريات حسب الفترة والفرع", 233),

                ("Purchases.InvoiceDetails.View", "Purchases", "InvoiceDetails", "View", "عرض تفاصيل فواتير المشتريات", "View Purchase Invoice Details", "تفاصيل فواتير الشراء", 234),
                ("Purchases.InvoiceDetails.Print", "Purchases", "InvoiceDetails", "Print", "طباعة تفاصيل فواتير المشتريات", "Print Purchase Invoice Details", "تفاصيل فواتير الشراء", 235),
                ("Purchases.InvoiceDetails.Export", "Purchases", "InvoiceDetails", "Export", "تصدير تفاصيل فواتير المشتريات", "Export Purchase Invoice Details", "تفاصيل فواتير الشراء", 236),

                ("Purchases.ByItem.View", "Purchases", "ByItem", "View", "عرض المشتريات حسب الصنف", "View Purchases By Item", "المشتريات حسب الصنف", 237),
                ("Purchases.ByItem.Print", "Purchases", "ByItem", "Print", "طباعة المشتريات حسب الصنف", "Print Purchases By Item", "المشتريات حسب الصنف", 238),
                ("Purchases.ByItem.Export", "Purchases", "ByItem", "Export", "تصدير المشتريات حسب الصنف", "Export Purchases By Item", "المشتريات حسب الصنف", 239),

                ("Purchases.ByVendor.View", "Purchases", "ByVendor", "View", "عرض المشتريات حسب المورد", "View Purchases By Vendor", "المشتريات حسب المورد", 240),
                ("Purchases.ByVendor.Print", "Purchases", "ByVendor", "Print", "طباعة المشتريات حسب المورد", "Print Purchases By Vendor", "المشتريات حسب المورد", 241),
                ("Purchases.ByVendor.Export", "Purchases", "ByVendor", "Export", "تصدير المشتريات حسب المورد", "Export Purchases By Vendor", "المشتريات حسب المورد", 242),

                ("Purchases.ReturnsAndNet.View", "Purchases", "ReturnsAndNet", "View", "عرض مرتجعات المشتريات والصافي", "View Purchase Returns and Net", "مرتجعات المشتريات وصافي المشتريات", 243),
                ("Purchases.ReturnsAndNet.Print", "Purchases", "ReturnsAndNet", "Print", "طباعة مرتجعات المشتريات والصافي", "Print Purchase Returns and Net", "مرتجعات المشتريات وصافي المشتريات", 244),
                ("Purchases.ReturnsAndNet.Export", "Purchases", "ReturnsAndNet", "Export", "تصدير مرتجعات المشتريات والصافي", "Export Purchase Returns and Net", "مرتجعات المشتريات وصافي المشتريات", 245),

                ("Purchases.PriceHistory.View", "Purchases", "PriceHistory", "View", "عرض مقارنة أسعار شراء الصنف", "View Purchase Price History", "مقارنة أسعار شراء الصنف", 246),
                ("Purchases.PriceHistory.Print", "Purchases", "PriceHistory", "Print", "طباعة مقارنة أسعار شراء الصنف", "Print Purchase Price History", "مقارنة أسعار شراء الصنف", 247),
                ("Purchases.PriceHistory.Export", "Purchases", "PriceHistory", "Export", "تصدير مقارنة أسعار شراء الصنف", "Export Purchase Price History", "مقارنة أسعار شراء الصنف", 248),

                ("Purchases.PendingStock.View", "Purchases", "PendingStock", "View", "عرض فواتير بانتظار استلام المخزون", "View Pending Stock Receipts", "بضائع بانتظار استلام المخزون", 249),
                ("Purchases.PendingStock.Print", "Purchases", "PendingStock", "Print", "طباعة فواتير بانتظار استلام المخزون", "Print Pending Stock Receipts", "بضائع بانتظار استلام المخزون", 250),
                ("Purchases.PendingStock.Export", "Purchases", "PendingStock", "Export", "تصدير فواتير بانتظار استلام المخزون", "Export Pending Stock Receipts", "بضائع بانتظار استلام المخزون", 251),

                ("Purchases.ReceiptTracking.View", "Purchases", "ReceiptTracking", "View", "عرض متابعة الفواتير واستلام المخزون", "View Purchase Receipt Tracking", "متابعة الفواتير واستلام المخزون", 252),
                ("Purchases.ReceiptTracking.Print", "Purchases", "ReceiptTracking", "Print", "طباعة متابعة الفواتير واستلام المخزون", "Print Purchase Receipt Tracking", "متابعة الفواتير واستلام المخزون", 253),
                ("Purchases.ReceiptTracking.Export", "Purchases", "ReceiptTracking", "Export", "تصدير متابعة الفواتير واستلام المخزون", "Export Purchase Receipt Tracking", "متابعة الفواتير واستلام المخزون", 254),

                // ── Inventory Reports & Features ──────────────────────────────────────────────
                ("Inventory.InventoryReportsCatalog.View", "Inventory", "InventoryReportsCatalog", "View", "عرض فهرس تقارير المخازن", "View Inventory Reports Catalog", "فهرس تقارير المخازن", 260),
                ("Inventory.StockBalances.View", "Inventory", "StockBalances", "View", "عرض أرصدة وحركة المخزون", "View Stock Balances", "رصيد المخزون وحركة المخازن", 261),
                ("Inventory.StockBalances.Print", "Inventory", "StockBalances", "Print", "طباعة أرصدة وحركة المخزون", "Print Stock Balances", "رصيد المخزون وحركة المخازن", 262),
                ("Inventory.StockBalances.Export", "Inventory", "StockBalances", "Export", "تصدير أرصدة وحركة المخزون", "Export Stock Balances", "رصيد المخزون وحركة المخازن", 263),

                ("Inventory.ItemStockCard.View", "Inventory", "ItemStockCard", "View", "عرض كارت الصنف وحركته", "View Item Stock Card", "كارت الصنف وحركته التفصيلية", 264),
                ("Inventory.ItemStockCard.Print", "Inventory", "ItemStockCard", "Print", "طباعة كارت الصنف وحركته", "Print Item Stock Card", "كارت الصنف وحركته التفصيلية", 265),
                ("Inventory.ItemStockCard.Export", "Inventory", "ItemStockCard", "Export", "تصدير كارت الصنف وحركته", "Export Item Stock Card", "كارت الصنف وحركته التفصيلية", 266),

                ("Inventory.InwardOutward.View", "Inventory", "InwardOutward", "View", "عرض حركة الوارد والمنصرف لمخزن", "View Inward and Outward Movements", "حركة الوارد والمنصرف لمخزن", 267),
                ("Inventory.InwardOutward.Print", "Inventory", "InwardOutward", "Print", "طباعة حركة الوارد والمنصرف لمخزن", "Print Inward and Outward Movements", "حركة الوارد والمنصرف لمخزن", 268),
                ("Inventory.InwardOutward.Export", "Inventory", "InwardOutward", "Export", "تصدير حركة الوارد والمنصرف لمخزن", "Export Inward and Outward Movements", "حركة الوارد والمنصرف لمخزن", 269),

                ("Inventory.WarehouseTransfers.View", "Inventory", "WarehouseTransfers", "View", "عرض التحويلات بين المخازن", "View Warehouse Transfers", "حركة التحويلات بين المخازن", 270),
                ("Inventory.WarehouseTransfers.Print", "Inventory", "WarehouseTransfers", "Print", "طباعة التحويلات بين المخازن", "Print Warehouse Transfers", "حركة التحويلات بين المخازن", 271),
                ("Inventory.WarehouseTransfers.Export", "Inventory", "WarehouseTransfers", "Export", "تصدير التحويلات بين المخازن", "Export Warehouse Transfers", "حركة التحويلات بين المخازن", 272),

                ("Inventory.ReturnsAndAdjustments.View", "Inventory", "ReturnsAndAdjustments", "View", "عرض التسويات والمرتجعات", "View Returns and Adjustments", "تقرير التسويات والمرتجعات", 273),
                ("Inventory.ReturnsAndAdjustments.Print", "Inventory", "ReturnsAndAdjustments", "Print", "طباعة التسويات والمرتجعات", "Print Returns and Adjustments", "تقرير التسويات والمرتجعات", 274),
                ("Inventory.ReturnsAndAdjustments.Export", "Inventory", "ReturnsAndAdjustments", "Export", "تصدير التسويات والمرتجعات", "Export Returns and Adjustments", "تقرير التسويات والمرتجعات", 275),

                ("Inventory.ReservedAndAvailable.View", "Inventory", "ReservedAndAvailable", "View", "عرض المخزون المحجوز والمتاح للبيع", "View Reserved and Available Stock", "المخزون المحجوز والمتاح للبيع", 276),
                ("Inventory.ReservedAndAvailable.Print", "Inventory", "ReservedAndAvailable", "Print", "طباعة المخزون المحجوز والمتاح للبيع", "Print Reserved and Available Stock", "المخزون المحجوز والمتاح للبيع", 277),
                ("Inventory.ReservedAndAvailable.Export", "Inventory", "ReservedAndAvailable", "Export", "تصدير المخزون المحجوز والمتاح للبيع", "Export Reserved and Available Stock", "المخزون المحجوز والمتاح للبيع", 278),

                ("Inventory.ReorderLevelAlert.View", "Inventory", "ReorderLevelAlert", "View", "عرض تنبيهات الرصيد وحد الطلب", "View Reorder Level Alerts", "تنبيهات الرصيد والوصول لحد الطلب", 279),
                ("Inventory.ReorderLevelAlert.Print", "Inventory", "ReorderLevelAlert", "Print", "طباعة تنبيهات الرصيد وحد الطلب", "Print Reorder Level Alerts", "تنبيهات الرصيد والوصول لحد الطلب", 280),
                ("Inventory.ReorderLevelAlert.Export", "Inventory", "ReorderLevelAlert", "Export", "تصدير تنبيهات الرصيد وحد الطلب", "Export Reorder Level Alerts", "تنبيهات الرصيد والوصول لحد الطلب", 281),

                ("Inventory.DeadStock.View", "Inventory", "DeadStock", "View", "عرض تقرير الأصناف الراكدة بالمخازن", "View Dead Stock Report", "تقرير الأصناف الراكدة بالمخازن", 282),
                ("Inventory.DeadStock.Print", "Inventory", "DeadStock", "Print", "طباعة تقرير الأصناف الراكدة بالمخازن", "Print Dead Stock Report", "تقرير الأصناف الراكدة بالمخازن", 283),
                ("Inventory.DeadStock.Export", "Inventory", "DeadStock", "Export", "تصدير تقرير الأصناف الراكدة بالمخازن", "Export Dead Stock Report", "تقرير الأصناف الراكدة بالمخازن", 284),

                ("Inventory.SerialNumbersReport.View", "Inventory", "SerialNumbersReport", "View", "عرض تقرير الأرقام التسلسلية", "View Serial Numbers Report", "تقرير الأرقام التسلسلية (Serials)", 285),
                ("Inventory.SerialNumbersReport.Print", "Inventory", "SerialNumbersReport", "Print", "طباعة تقرير الأرقام التسلسلية", "Print Serial Numbers Report", "تقرير الأرقام التسلسلية (Serials)", 286),
                ("Inventory.SerialNumbersReport.Export", "Inventory", "SerialNumbersReport", "Export", "تصدير تقرير الأرقام التسلسلية", "Export Serial Numbers Report", "تقرير الأرقام التسلسلية (Serials)", 287),

                ("Inventory.StockDiscrepancy.View", "Inventory", "StockDiscrepancy", "View", "عرض تقرير فروق الجرد والمخزون", "View Stock Discrepancy Report", "تقرير فروق الجرد والمخزون", 288),
                ("Inventory.StockDiscrepancy.Print", "Inventory", "StockDiscrepancy", "Print", "طباعة تقرير فروق الجرد والمخزون", "Print Stock Discrepancy Report", "تقرير فروق الجرد والمخزون", 289),
                ("Inventory.StockDiscrepancy.Export", "Inventory", "StockDiscrepancy", "Export", "تصدير تقرير فروق الجرد والمخزون", "Export Stock Discrepancy Report", "تقرير فروق الجرد والمخزون", 290),

                // ── Additional Inventory Features ─────────────────────────────────────────────
                ("Inventory.SerialNumbers.View", "Inventory", "SerialNumbers", "View", "استعلام وتتبع الأرقام التسلسلية", "View Serial Numbers Inquiry", "استعلام وتتبع الأرقام التسلسلية", 295),
                ("Inventory.DocumentAdjustments.View", "Inventory", "DocumentAdjustments", "View", "عرض مصروفات وخصومات المستندات", "View Document Adjustments", "مصروفات وخصومات المستندات", 300),
                ("Inventory.DocumentAdjustments.Add", "Inventory", "DocumentAdjustments", "Add", "إضافة مصروف / خصم مستند", "Add Document Adjustment", "مصروفات وخصومات المستندات", 301),
                ("Inventory.DocumentAdjustments.Approve", "Inventory", "DocumentAdjustments", "Approve", "اعتماد مصروف / خصم مستند", "Approve Document Adjustment", "مصروفات وخصومات المستندات", 302),
                ("Inventory.DocumentAdjustments.Cancel", "Inventory", "DocumentAdjustments", "Cancel", "إلغاء مصروف / خصم مستند", "Cancel Document Adjustment", "مصروفات وخصومات المستندات", 303)
            };

            var existingPermCodes = (await context.Permissions.Select(p => p.Code).ToListAsync()).ToHashSet();

            var newPerms = permissionDefinitions
                .Where(p => !existingPermCodes.Contains(p.Code))
                .Select(p => new Permission
                {
                    Code = p.Code,
                    Module = p.Module,
                    Screen = p.Screen,
                    Action = p.Action,
                    NameAr = p.NameAr,
                    NameEn = p.NameEn,
                    SortOrder = p.Sort
                })
                .Concat(advancedReportAndScreenPermissions
                    .Where(p => !existingPermCodes.Contains(p.Code))
                    .Select(p => new Permission
                    {
                        Code = p.Code,
                        Module = p.Module,
                        Screen = p.Screen,
                        Action = p.Action,
                        NameAr = p.NameAr,
                        NameEn = p.NameEn,
                        Description = p.Description,
                        SortOrder = p.Sort
                    }))
                .ToList();

            if (newPerms.Any())
            {
                context.Permissions.AddRange(newPerms);
                await context.SaveChangesAsync();
            }

            // Also update Description for existing permissions if it was null
            var permDescMap = advancedReportAndScreenPermissions.ToDictionary(p => p.Code, p => p.Description);
            var existingWithNullDesc = await context.Permissions
                .Where(p => permDescMap.Keys.Contains(p.Code) && (p.Description == null || p.Description == ""))
                .ToListAsync();
            if (existingWithNullDesc.Any())
            {
                foreach (var ep in existingWithNullDesc)
                {
                    if (permDescMap.TryGetValue(ep.Code, out var desc))
                    {
                        ep.Description = desc;
                    }
                }
                await context.SaveChangesAsync();
            }

            // Helper to get role
            async Task<ApplicationRole?> GetRoleAsync(string roleName)
            {
                return roleManager != null
                    ? await roleManager.FindByNameAsync(roleName)
                    : await context.Roles.FirstOrDefaultAsync(ro => ro.Name == roleName);
            }

            // ─── 3. Assign all permissions to Admin Role ──────────────────────────────
            var adminRole = await GetRoleAsync("Admin");
            if (adminRole != null)
            {
                var allPermissions = await context.Permissions.ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == adminRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingRolePerms = allPermissions
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = adminRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingRolePerms.Any())
                {
                    context.RolePermissions.AddRange(missingRolePerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Accounting, Cash, and Reports to Account Manager
            var accountManagerRole = await GetRoleAsync("Account Manager");
            if (accountManagerRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Accounting" || p.Module == "Cash" || p.Module == "Reports")
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == accountManagerRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = accountManagerRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Cash Voucher permissions to Cashier
            var cashierRole = await GetRoleAsync("Cashier");
            if (cashierRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Cash" && (p.Action == "View" || p.Action == "Add" || p.Action == "Print"))
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == cashierRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = cashierRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Sales permissions to Sales Manager
            var salesManagerRole = await GetRoleAsync("Sales Manager");
            if (salesManagerRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Sales")
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == salesManagerRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = salesManagerRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Purchase permissions to Purchase Manager
            var purchaseManagerRole = await GetRoleAsync("Purchase Manager");
            if (purchaseManagerRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Purchases")
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == purchaseManagerRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = purchaseManagerRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Inventory permissions to Inventory Manager
            var inventoryManagerRole = await GetRoleAsync("Inventory Manager");
            if (inventoryManagerRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Inventory")
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == inventoryManagerRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = inventoryManagerRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Read/Report permissions to Auditor
            var auditorRole = await GetRoleAsync("Auditor");
            if (auditorRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => (p.Module == "Accounting" || p.Module == "Cash" || p.Module == "Reports" || p.Module == "Administration" || p.Module == "Sales" || p.Module == "Purchases" || p.Module == "Inventory") &&
                                (p.Action == "View" || p.Action == "Print" || p.Action == "Export"))
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == auditorRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = auditorRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Fix typo in database if exists
            var typoPerm = await context.Permissions.FirstOrDefaultAsync(p => p.Code == "Cash.ReceiptVoucher.View" && p.NameAr.Contains("القبط"));
            if (typoPerm != null)
            {
                typoPerm.NameAr = "عرض سندات القبض";
                await context.SaveChangesAsync();
            }
        }
    }
}
