using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ERP.Application.DTOs.Accounts;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class AccountImportService : IAccountImportService
    {
        private readonly ErpDbContext _context;

        public AccountImportService(ErpDbContext context)
        {
            _context = context;
        }

        // ====================================================================
        // 1. Generate Standard Excel Template (.xlsx) for GLAccounts
        // ====================================================================
        public byte[] GenerateTemplate()
        {
            using var workbook = new XLWorkbook();

            // ----------------------------------------------------------------
            // Sheet 1: شجرة الحسابات (Chart of Accounts)
            // ----------------------------------------------------------------
            var ws = workbook.Worksheets.Add("شجرة الحسابات");
            ws.RightToLeft = true;
            ws.ShowGridLines = true;

            string[] headers = {
                "كود الحساب",
                "اسم الحساب (عربي)",
                "اسم الحساب (إنجليزي)",
                "كود الحساب الأب",
                "نوع الحساب",
                "التصنيف المحاسبي",
                "طبيعة الحساب",
                "حساب مراقبة",
                "نوع الدفتر المساعد",
                "حساب تسوية",
                "يتطلب مركز تكلفة",
                "كود العملة",
                "نطاق الاستخدام"
            };

            for (int col = 0; col < headers.Length; col++)
            {
                var cell = ws.Cell(1, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.FontSize = 11;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93); // Dark Navy Blue (#1B365D)
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(15, 32, 67);
            }
            ws.Row(1).Height = 28;

            // Sample comprehensive hierarchical data (5 levels)
            var sampleData = new List<(
                string Code, string NameAr, string NameEn, string Parent,
                string Type, string Category, string Nature, string Control,
                string SubLedger, string Rec, string CostCenter, string Curr, string Usage)>
            {
                // Level 1: الأصول
                ("1", "الأصول", "Assets", "", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("11", "الأصول المتداولة", "Current Assets", "1", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("1101", "النقدية وما في حكمها", "Cash and Cash Equivalents", "11", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("110101", "صناديق وخزائن الفروع", "Cash in Hand", "1101", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("11010101", "صندوق الإدارة العامة", "Main HQ Cash", "110101", "حساب حركة (فرعي)", "أصول", "مدين", "نعم", "بنوك وخزائن", "لا", "لا", "EGP", "الكل"),
                ("11010102", "صندوق فرع القاهرة الرئيسي", "Cairo Branch Cash", "110101", "حساب حركة (فرعي)", "أصول", "مدين", "نعم", "بنوك وخزائن", "لا", "لا", "EGP", "الكل"),
                ("11010103", "صندوق فرع الإسكندرية", "Alex Branch Cash", "110101", "حساب حركة (فرعي)", "أصول", "مدين", "نعم", "بنوك وخزائن", "لا", "لا", "EGP", "الكل"),
                ("110102", "حسابات البنوك الجارية", "Banks Current Accounts", "1101", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("11010201", "البنك الأهلي المصري - جاري محلي", "NBE - EGP Account", "110102", "حساب حركة (فرعي)", "أصول", "مدين", "نعم", "بنوك وخزائن", "لا", "لا", "EGP", "الكل"),
                ("11010202", "بنك مصر - جاري دولار", "BM - USD Account", "110102", "حساب حركة (فرعي)", "أصول", "مدين", "نعم", "بنوك وخزائن", "لا", "لا", "USD", "الكل"),
                ("1102", "العملاء والمدينون", "Accounts Receivable", "11", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("110201", "حساب مراقبة العملاء المحليين", "AR - Local Customers Control", "1102", "حساب حركة (فرعي)", "أصول", "مدين", "نعم", "عملاء", "لا", "لا", "", "الكل"),
                ("110202", "حساب مراقبة عملاء التصدير", "AR - Export Customers Control", "1102", "حساب حركة (فرعي)", "أصول", "مدين", "نعم", "عملاء", "لا", "لا", "USD", "الكل"),
                ("1103", "المخزون السلعي", "Inventory", "11", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("110301", "مخزون البضائع الجاهزة", "Finished Goods Inventory", "1103", "حساب حركة (فرعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "EGP", "الكل"),
                ("110302", "مخزون الخامات ومستلزمات الإنتاج", "Raw Materials Inventory", "1103", "حساب حركة (فرعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "EGP", "الكل"),
                ("1104", "أرصدة مدينة أخرى", "Other Debit Balances", "11", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("110401", "عهد وسلف الموظفين", "Employee Custody and Advances", "1104", "حساب حركة (فرعي)", "أصول", "مدين", "نعم", "عهد موظفين", "لا", "لا", "EGP", "الكل"),
                ("110402", "مصروفات مدفوعة مقدماً", "Prepaid Expenses", "1104", "حساب حركة (فرعي)", "أصول", "مدين", "لا", "لا شيء", "نعم", "لا", "EGP", "الكل"),
                ("12", "الأصول غير المتداولة (الثابتة)", "Non-Current / Fixed Assets", "1", "مجموعة رئيسية (تجميعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("120101", "الأراضي والمباني", "Lands and Buildings", "12", "حساب حركة (فرعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "لا", "EGP", "الكل"),
                ("120201", "الآلات والمعدات", "Machinery and Equipment", "12", "حساب حركة (فرعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),
                ("120301", "وسائل النقل والانتقال", "Vehicles and Transport", "12", "حساب حركة (فرعي)", "أصول", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),

                // Level 1: الخصوم
                ("2", "الخصوم والالتزامات", "Liabilities", "", "مجموعة رئيسية (تجميعي)", "خصوم", "دائن", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("21", "الالتزامات المتداولة", "Current Liabilities", "2", "مجموعة رئيسية (تجميعي)", "خصوم", "دائن", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("2101", "الموردون والدائنون", "Accounts Payable", "21", "مجموعة رئيسية (تجميعي)", "خصوم", "دائن", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("210101", "حساب مراقبة الموردين المحليين", "AP - Local Vendors Control", "2101", "حساب حركة (فرعي)", "خصوم", "دائن", "نعم", "موردين", "لا", "لا", "", "الكل"),
                ("210102", "حساب مراقبة الموردين الخارجيين", "AP - Foreign Vendors Control", "2101", "حساب حركة (فرعي)", "خصوم", "دائن", "نعم", "موردين", "لا", "لا", "USD", "الكل"),
                ("2102", "الضرائب المستحقة", "Taxes Payable", "21", "مجموعة رئيسية (تجميعي)", "خصوم", "دائن", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("210201", "مصلحة الضرائب - ضريبة القيمة المضافة", "VAT Payable", "2102", "حساب حركة (فرعي)", "خصوم", "دائن", "لا", "لا شيء", "نعم", "لا", "EGP", "الكل"),
                ("210202", "مصلحة الضرائب - ضريبة كسب العمل", "Payroll Tax Payable", "2102", "حساب حركة (فرعي)", "خصوم", "دائن", "لا", "لا شيء", "نعم", "لا", "EGP", "الكل"),
                ("210203", "ضريبة الخصم والإضافة من المنبع", "Withholding Tax Payable", "2102", "حساب حركة (فرعي)", "خصوم", "دائن", "لا", "لا شيء", "نعم", "لا", "EGP", "الكل"),

                // Level 1: حقوق الملكية
                ("3", "حقوق الملكية", "Equity", "", "مجموعة رئيسية (تجميعي)", "حقوق ملكية", "دائن", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("310101", "رأس المال المصدر والمدفوع", "Paid-in Capital", "3", "حساب حركة (فرعي)", "حقوق ملكية", "دائن", "لا", "لا شيء", "لا", "لا", "EGP", "الكل"),
                ("310201", "الأرباح / (الخسائر) المرحلة والمحتجزة", "Retained Earnings", "3", "حساب حركة (فرعي)", "حقوق ملكية", "دائن", "لا", "لا شيء", "لا", "لا", "EGP", "الكل"),
                ("310301", "جاري الشركاء / المساهمين", "Current Partners Account", "3", "حساب حركة (فرعي)", "حقوق ملكية", "دائن", "لا", "لا شيء", "لا", "لا", "EGP", "الكل"),

                // Level 1: الإيرادات
                ("4", "الإيرادات", "Revenues", "", "مجموعة رئيسية (تجميعي)", "إيرادات", "دائن", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("41", "إيرادات النشاط الرئيسي", "Operating Revenues", "4", "مجموعة رئيسية (تجميعي)", "إيرادات", "دائن", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("410101", "إيرادات مبيعات البضائع المحلية", "Domestic Sales Revenue", "41", "حساب حركة (فرعي)", "إيرادات", "دائن", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),
                ("410102", "إيرادات مبيعات التصدير", "Export Sales Revenue", "41", "حساب حركة (فرعي)", "إيرادات", "دائن", "لا", "لا شيء", "لا", "نعم", "USD", "الكل"),
                ("410201", "مردودات ومسموحات المبيعات", "Sales Returns and Allowances", "41", "حساب حركة (فرعي)", "إيرادات", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),
                ("420101", "إيرادات استثمارات وفوائد بنكية", "Interest and Investment Income", "4", "حساب حركة (فرعي)", "إيرادات", "دائن", "لا", "لا شيء", "لا", "لا", "EGP", "الكل"),

                // Level 1: المصروفات
                ("5", "المصروفات والتكاليف", "Expenses", "", "مجموعة رئيسية (تجميعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("51", "تكلفة المبيعات (تكلفة النشاط)", "Cost of Goods Sold", "5", "مجموعة رئيسية (تجميعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("510101", "تكلفة البضاعة المباعة", "Cost of Goods Sold", "51", "حساب حركة (فرعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),
                ("52", "المصروفات العمومية والإدارية", "General & Admin Expenses", "5", "مجموعة رئيسية (تجميعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("520101", "رواتب وأجور الموظفين", "Salaries & Wages", "52", "حساب حركة (فرعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),
                ("520201", "مصروف إيجار مقرات وفروع", "Rent Expense", "52", "حساب حركة (فرعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),
                ("520301", "استهلاك كهرباء ومياه وإنترنت", "Utilities Expense", "52", "حساب حركة (فرعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),
                ("520401", "مصروفات بنكية وعمولات", "Bank Charges & Commissions", "52", "حساب حركة (فرعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "لا", "EGP", "الكل"),
                ("53", "المصروفات التسويقية والبيعية", "Selling & Marketing Expenses", "5", "مجموعة رئيسية (تجميعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "لا", "", "الكل"),
                ("530101", "مصروفات الدعاية والإعلان", "Advertising & Marketing", "53", "حساب حركة (فرعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل"),
                ("530201", "عمولات بيعية لمندوبي المبيعات", "Sales Commissions", "53", "حساب حركة (فرعي)", "مصروفات", "مدين", "لا", "لا شيء", "لا", "نعم", "EGP", "الكل")
            };

            int rowIdx = 2;
            foreach (var item in sampleData)
            {
                ws.Cell(rowIdx, 1).SetValue(item.Code);
                ws.Cell(rowIdx, 1).Style.NumberFormat.Format = "@";
                ws.Cell(rowIdx, 2).SetValue(item.NameAr);
                ws.Cell(rowIdx, 3).SetValue(item.NameEn);
                ws.Cell(rowIdx, 4).SetValue(item.Parent);
                ws.Cell(rowIdx, 4).Style.NumberFormat.Format = "@";
                ws.Cell(rowIdx, 5).SetValue(item.Type);
                ws.Cell(rowIdx, 6).SetValue(item.Category);
                ws.Cell(rowIdx, 7).SetValue(item.Nature);
                ws.Cell(rowIdx, 8).SetValue(item.Control);
                ws.Cell(rowIdx, 9).SetValue(item.SubLedger);
                ws.Cell(rowIdx, 10).SetValue(item.Rec);
                ws.Cell(rowIdx, 11).SetValue(item.CostCenter);
                ws.Cell(rowIdx, 12).SetValue(item.Curr);
                ws.Cell(rowIdx, 12).Style.NumberFormat.Format = "@";
                ws.Cell(rowIdx, 13).SetValue(item.Usage);

                bool isGroup = item.Type.Contains("مجموعة") || item.Type.Contains("رئيسي");
                for (int col = 1; col <= 13; col++)
                {
                    var cell = ws.Cell(rowIdx, col);
                    cell.Style.Font.FontName = "Segoe UI";
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(220, 224, 230);

                    if (isGroup)
                    {
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 244, 250);
                    }
                    else
                    {
                        if (rowIdx % 2 == 0)
                            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(252, 253, 255);
                    }
                }

                ws.Row(rowIdx).Height = 22;
                rowIdx++;
            }

            // In-cell Validation Dropdowns (rows 2 to 1000)
            ws.Range(2, 5, 1000, 5).CreateDataValidation().List("\"مجموعة رئيسية (تجميعي),حساب حركة (فرعي)\"", true);
            ws.Range(2, 6, 1000, 6).CreateDataValidation().List("\"أصول,خصوم,حقوق ملكية,إيرادات,مصروفات\"", true);
            ws.Range(2, 7, 1000, 7).CreateDataValidation().List("\"مدين,دائن\"", true);
            ws.Range(2, 8, 1000, 8).CreateDataValidation().List("\"نعم,لا\"", true);
            ws.Range(2, 9, 1000, 9).CreateDataValidation().List("\"عملاء,موردين,بنوك وخزائن,عهد موظفين,لا شيء\"", true);
            ws.Range(2, 10, 1000, 10).CreateDataValidation().List("\"نعم,لا\"", true);
            ws.Range(2, 11, 1000, 11).CreateDataValidation().List("\"نعم,لا\"", true);
            ws.Range(2, 12, 1000, 12).CreateDataValidation().List("\"EGP,USD,EUR,SAR,AED,GBP\"", true);
            ws.Range(2, 13, 1000, 13).CreateDataValidation().List("\"الكل,قيود اليومية فقط,سندات الصرف فقط,سندات القبض فقط\"", true);

            ws.Columns(1, 13).AdjustToContents();
            ws.Column(1).Width = 16;
            ws.Column(2).Width = 32;
            ws.Column(3).Width = 32;
            ws.Column(4).Width = 16;
            ws.Column(5).Width = 22;
            ws.Column(6).Width = 18;
            ws.Column(7).Width = 14;
            ws.Column(8).Width = 14;
            ws.Column(9).Width = 18;
            ws.Column(10).Width = 14;
            ws.Column(11).Width = 16;
            ws.Column(12).Width = 14;
            ws.Column(13).Width = 20;

            // ----------------------------------------------------------------
            // Sheet 2: تعليمات وإرشادات الاستيراد
            // ----------------------------------------------------------------
            var wsHelp = workbook.Worksheets.Add("تعليمات وإرشادات الاستيراد");
            wsHelp.RightToLeft = true;
            wsHelp.ShowGridLines = true;

            wsHelp.Cell(1, 1).Value = "دليل وإرشادات ملء ملف شجرة الحسابات (GLAccounts)";
            wsHelp.Range(1, 1, 1, 3).Merge();
            wsHelp.Row(1).Height = 30;
            wsHelp.Cell(1, 1).Style.Font.Bold = true;
            wsHelp.Cell(1, 1).Style.Font.FontSize = 14;
            wsHelp.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsHelp.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93);
            wsHelp.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var instructions = new (string Col, string Field, string Desc)[]
            {
                ("A", "كود الحساب (إلزامي)", "رقم الحساب الفريد (مثل: 1, 11, 1101, 110101). يجب ألا يتكرر في الملف أو قاعدة البيانات."),
                ("B", "اسم الحساب عربي (إلزامي)", "الاسم المعتمد للحساب باللغة العربية."),
                ("C", "اسم الحساب إنجليزي (اختياري)", "الاسم المحاسبي باللغة الإنجليزية."),
                ("D", "كود الحساب الأب (اختياري)", "كود الحساب الرئيسي الذي يتبع له هذا الحساب. يترك فارغاً فقط للمستويات الرئيسية الأولى (1, 2, 3, 4, 5)."),
                ("E", "نوع الحساب (إلزامي)", "'مجموعة رئيسية (تجميعي)' للحسابات الأبوية التي لا تقبل القيود، أو 'حساب حركة (فرعي)' للحسابات التي تسجل عليها القيود."),
                ("F", "التصنيف المحاسبي", "أصول، خصوم، حقوق ملكية، إيرادات، أو مصروفات (يحدد موقع الحساب في القوائم المالية)."),
                ("G", "طبيعة الحساب", "'مدين' أو 'دائن' (يحدد طبيعة الحساب في ميزان المراجعة)."),
                ("H", "حساب مراقبة (Control)", "'نعم' إذا كان الحساب يمثل حساب رقابة إجمالي لدفتر أستاذ مساعد (مثل حساب إجمالي العملاء أو إجمالي الموردين)."),
                ("I", "نوع الدفتر المساعد", "عملاء، موردين، بنوك وخزائن، عهد موظفين، أو لا شيء."),
                ("J", "حساب تسوية", "'نعم' إذا كان الحساب وسيطاً أو تسوية بنكية."),
                ("K", "يتطلب مركز تكلفة", "'نعم' إذا كان الحساب يلزم ربط حركاته بمراكز التكلفة إجبارياً في قيود اليومية."),
                ("L", "كود العملة", "رمز العملة (EGP, USD, EUR...) إذا كان الحساب مقيداً بعملة محددة، أو يترك فارغاً للحسابات متعددة العملات."),
                ("M", "نطاق الاستخدام", "'الكل' لظهور الحساب في كافة الشاشات، أو تحديده لقيود اليومية أو الصرف أو القبض.")
            };

            wsHelp.Cell(3, 1).Value = "العمود";
            wsHelp.Cell(3, 2).Value = "اسم الحقل";
            wsHelp.Cell(3, 3).Value = "الشرح وقواعد الإدخال";
            wsHelp.Range(3, 1, 3, 3).Style.Font.Bold = true;
            wsHelp.Range(3, 1, 3, 3).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);
            wsHelp.Row(3).Height = 24;

            int helpRow = 4;
            foreach (var inst in instructions)
            {
                wsHelp.Cell(helpRow, 1).SetValue(inst.Col);
                wsHelp.Cell(helpRow, 2).SetValue(inst.Field);
                wsHelp.Cell(helpRow, 3).SetValue(inst.Desc);
                wsHelp.Row(helpRow).Height = 22;
                helpRow++;
            }

            wsHelp.Columns(1, 3).AdjustToContents();
            wsHelp.Column(1).Width = 10;
            wsHelp.Column(2).Width = 25;
            wsHelp.Column(3).Width = 70;

            using var memoryStream = new MemoryStream();
            workbook.SaveAs(memoryStream);
            return memoryStream.ToArray();
        }

        // ====================================================================
        // 2. Validate and Preview Excel / CSV File
        // ====================================================================
        public async Task<AccountImportPreviewDto> ValidateAndPreviewAsync(Stream excelStream, CancellationToken cancellationToken = default)
        {
            var preview = new AccountImportPreviewDto();

            var rawRows = ExtractRawRows(excelStream);
            if (rawRows.Count == 0)
            {
                preview.ErrorCount = 1;
                return preview;
            }

            // Ensure standard currencies (including AED, CNY, GBP, etc.) exist if referenced in the file
            await EnsureReferencedCurrenciesExistAsync(rawRows, cancellationToken);

            var existingDbAccounts = await _context.GLAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(a => new { a.Id, a.Code, a.NameAr, a.IsGroup, a.Level })
                .ToDictionaryAsync(a => a.Code, a => a, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var existingCurrencies = await _context.Currencies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(c => new { c.Id, c.Code })
                .ToDictionaryAsync(c => c.Code, c => c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var fileCodeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileGroupCodeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Pass 1: Parse rows & collect codes
            foreach (var raw in rawRows)
            {
                int r = raw.RowNumber;
                string code = raw.Code;
                string nameAr = raw.NameAr;
                string nameEn = raw.NameEn;
                string parentCode = raw.ParentCode;
                string typeStr = raw.TypeStr;
                string catStr = raw.CatStr;
                string natureStr = raw.NatureStr;
                string controlStr = raw.ControlStr;
                string subLedgerStr = raw.SubLedgerStr;
                string recStr = raw.RecStr;
                string costCenterStr = raw.CostCenterStr;
                string currCode = raw.CurrCode;
                string usageStr = raw.UsageStr;

                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(nameAr))
                    continue;

                // Skip header or instruction/footer lines if pasted at the bottom of the sheet
                if (IsInstructionOrHeaderRow(code, nameAr, parentCode))
                    continue;

                bool isGroup = typeStr.Contains("مجموعة") || typeStr.Contains("رئيسي");

                var rowDto = new AccountImportRowDto
                {
                    RowNumber = r,
                    Code = code,
                    NameAr = nameAr,
                    NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn,
                    ParentCode = string.IsNullOrWhiteSpace(parentCode) ? null : parentCode,
                    IsGroup = isGroup,
                    TypeStr = isGroup ? "مجموعة رئيسية" : "حساب حركة",
                    Category = ParseCategory(catStr, code),
                    CategoryStr = string.IsNullOrWhiteSpace(catStr) ? "أصول" : catStr,
                    Nature = ParseNature(natureStr, code),
                    NatureStr = string.IsNullOrWhiteSpace(natureStr) ? "مدين" : natureStr,
                    IsControlAccount = controlStr.Contains("نعم") || controlStr.Equals("true", StringComparison.OrdinalIgnoreCase),
                    SubLedgerType = ParseSubLedger(subLedgerStr),
                    SubLedgerTypeStr = string.IsNullOrWhiteSpace(subLedgerStr) ? "لا شيء" : subLedgerStr,
                    IsReconciliation = recStr.Contains("نعم") || recStr.Equals("true", StringComparison.OrdinalIgnoreCase),
                    RequiresCostCenter = costCenterStr.Contains("نعم") || costCenterStr.Equals("true", StringComparison.OrdinalIgnoreCase),
                    CurrencyCode = string.IsNullOrWhiteSpace(currCode) ? null : currCode.ToUpperInvariant(),
                    AccountUsage = ParseUsage(usageStr),
                    AccountUsageStr = string.IsNullOrWhiteSpace(usageStr) ? "الكل" : usageStr
                };

                // Auto-fix known copy-paste duplicate codes under Inventory (120302 & 120303)
                ApplyKnownInventoryCopyPasteFix(rowDto);
                code = rowDto.Code;

                // Check DB existence
                if (existingDbAccounts.TryGetValue(code, out var existingAcc))
                {
                    rowDto.ExistsInDb = true;
                    rowDto.ExistingType = existingAcc.IsGroup ? "مجموعة رئيسية" : "حساب حركة";
                    rowDto.Warnings.Add($"الحساب موجود مسبقاً في النظام ({rowDto.ExistingType}). سيتم تحديث بياناته إذا فُعل خيار التحديث.");
                }

                if (isGroup)
                {
                    fileGroupCodeSet.Add(code);
                }

                preview.Rows.Add(rowDto);
            }

            preview.TotalRows = preview.Rows.Count;

            // Map of file rows by code for level calculation
            var fileRowByCode = new Dictionary<string, AccountImportRowDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in preview.Rows)
            {
                if (!string.IsNullOrWhiteSpace(r.Code) && !fileRowByCode.ContainsKey(r.Code))
                    fileRowByCode[r.Code] = r;
            }

            // Pass 2: Hierarchy & Validation
            foreach (var r in preview.Rows)
            {
                if (string.IsNullOrWhiteSpace(r.Code))
                {
                    r.Errors.Add("كود الحساب مطلوب ولا يمكن تركه فارغاً.");
                }
                else
                {
                    if (fileCodeSet.Contains(r.Code))
                        r.Errors.Add($"كود الحساب '{r.Code}' مكرر داخل ملف الإكسيل.");
                    else
                        fileCodeSet.Add(r.Code);
                }

                if (string.IsNullOrWhiteSpace(r.NameAr))
                {
                    r.Errors.Add("اسم الحساب باللغة العربية مطلوب.");
                }

                // Parent verification
                if (!string.IsNullOrWhiteSpace(r.ParentCode))
                {
                    bool parentInDb = existingDbAccounts.TryGetValue(r.ParentCode, out var dbParent);
                    bool parentInFile = fileGroupCodeSet.Contains(r.ParentCode);

                    if (!parentInDb && !parentInFile)
                    {
                        r.Errors.Add($"الحساب الأب '{r.ParentCode}' غير موجود في النظام ولا في ملف الإكسيل كمجموعة رئيسية.");
                    }
                    else
                    {
                        if (parentInDb && !parentInFile && !dbParent!.IsGroup)
                        {
                            r.Errors.Add($"الحساب الأب '{r.ParentCode}' في قاعدة البيانات هو حساب حركة وليس مجموعة رئيسية.");
                        }
                        else
                        {
                            r.Level = ComputeLevel(r.Code, fileRowByCode, existingDbAccounts.ToDictionary(k => k.Key, v => v.Value.Level, StringComparer.OrdinalIgnoreCase));
                        }
                    }
                }
                else
                {
                    r.Level = 1;
                }

                // Currency verification
                if (!string.IsNullOrWhiteSpace(r.CurrencyCode))
                {
                    if (existingCurrencies.TryGetValue(r.CurrencyCode, out int cId))
                    {
                        r.CurrencyId = cId;
                    }
                    else
                    {
                        r.Errors.Add($"كود العملة '{r.CurrencyCode}' غير معرف في نظام العملات.");
                    }
                }

                if (r.IsValid)
                {
                    preview.ValidCount++;
                    if (r.IsGroup) preview.GroupCount++;
                    else preview.AccountCount++;
                }
                else
                {
                    preview.ErrorCount++;
                }

                if (r.Warnings.Count > 0)
                    preview.WarningCount++;
            }

            return preview;
        }

        // ====================================================================
        // 3. Execute Transactional Import into GLAccounts
        // ====================================================================
        public async Task<AccountImportResultDto> ExecuteImportAsync(Stream excelStream, bool updateExisting, CancellationToken cancellationToken = default)
        {
            var preview = await ValidateAndPreviewAsync(excelStream, cancellationToken);
            var result = new AccountImportResultDto();

            if (preview.ErrorCount > 0)
            {
                result.Success = false;
                result.Message = $"فشل الاستيراد: يوجد {preview.ErrorCount} سطر به أخطاء في ملف الإكسيل. يجب معالجتها قبل الاستيراد.";
                result.Errors = preview.Rows.Where(r => !r.IsValid).SelectMany(r => r.Errors.Select(e => $"السطر {r.RowNumber} [{r.Code}]: {e}")).ToList();
                return result;
            }

            if (preview.ValidCount == 0)
            {
                result.Success = false;
                result.Message = "الملف لا يحتوي على أي سطور صالحة للاستيراد.";
                return result;
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            try
            {
                await strategy.ExecuteAsync(async () =>
                {
                    result.GroupsCreated = 0;
                    result.GroupsUpdated = 0;
                    result.AccountsCreated = 0;
                    result.AccountsUpdated = 0;

                    using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                    var existingAccounts = await _context.GLAccounts
                        .IgnoreQueryFilters()
                        .ToDictionaryAsync(a => a.Code, a => a, StringComparer.OrdinalIgnoreCase, cancellationToken);

                    // Sort rows by computed tree level, then groups first, then code length & code
                    var levels = preview.Rows
                        .OrderBy(r => r.Level)
                        .ThenBy(r => !r.IsGroup)
                        .ThenBy(r => r.Code.Length)
                        .ThenBy(r => r.Code)
                        .GroupBy(r => r.Level)
                        .ToList();

                    foreach (var levelGroup in levels)
                    {
                        var newlyAddedInBatch = new List<GLAccount>();

                        foreach (var r in levelGroup)
                        {
                            int? parentId = null;
                            int level = 1;

                            if (!string.IsNullOrWhiteSpace(r.ParentCode))
                            {
                                if (existingAccounts.TryGetValue(r.ParentCode, out var parentAccount))
                                {
                                    parentId = parentAccount.Id;
                                    level = parentAccount.Level + 1;
                                }
                            }

                            if (existingAccounts.TryGetValue(r.Code, out var existing))
                            {
                                if (updateExisting)
                                {
                                    existing.NameAr = r.NameAr;
                                    if (r.NameEn != null) existing.NameEn = r.NameEn;
                                    existing.ParentId = parentId;
                                    existing.Level = level;
                                    existing.IsGroup = r.IsGroup;
                                    existing.AccountNature = r.Nature;
                                    existing.AccountCategory = r.Category;
                                    existing.IsControlAccount = r.IsControlAccount;
                                    existing.SubLedgerType = r.SubLedgerType;
                                    existing.IsReconciliationAccount = r.IsReconciliation;
                                    existing.RequiresCostCenter = r.RequiresCostCenter;
                                    existing.CurrencyId = r.CurrencyId;
                                    existing.AccountUsage = r.AccountUsage;
                                    existing.UpdatedAt = DateTime.UtcNow;

                                    if (r.IsGroup) result.GroupsUpdated++;
                                    else result.AccountsUpdated++;
                                }
                            }
                            else
                            {
                                var newAcc = new GLAccount
                                {
                                    Code = r.Code,
                                    NameAr = r.NameAr,
                                    NameEn = r.NameEn ?? r.NameAr,
                                    ParentId = parentId,
                                    Level = level,
                                    IsGroup = r.IsGroup,
                                    AccountNature = r.Nature,
                                    AccountCategory = r.Category,
                                    IsControlAccount = r.IsControlAccount,
                                    SubLedgerType = r.SubLedgerType,
                                    IsReconciliationAccount = r.IsReconciliation,
                                    RequiresCostCenter = r.RequiresCostCenter,
                                    CurrencyId = r.CurrencyId,
                                    AccountUsage = r.AccountUsage,
                                    IsActive = true,
                                    CreatedAt = DateTime.UtcNow
                                };

                                _context.GLAccounts.Add(newAcc);
                                newlyAddedInBatch.Add(newAcc);

                                if (r.IsGroup) result.GroupsCreated++;
                                else result.AccountsCreated++;
                            }
                        }

                        await _context.SaveChangesAsync(cancellationToken);

                        foreach (var added in newlyAddedInBatch)
                        {
                            existingAccounts[added.Code] = added;
                        }
                    }

                    await transaction.CommitAsync(cancellationToken);
                });

                result.Success = true;
                result.Message = $"تم استيراد دليل الحسابات بنجاح. تم إنشاء ({result.TotalCreated}) وتحديث ({result.TotalUpdated}) حساب ومجموعة.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "حدث خطأ غير متوقع أثناء حفظ الحسابات في قاعدة البيانات: " + ex.Message;
                result.Errors.Add(ex.ToString());
                return result;
            }
        }

        // ====================================================================
        // Helpers
        // ====================================================================
        private sealed class RawAccountRow
        {
            public int RowNumber { get; set; }
            public string Code { get; set; } = string.Empty;
            public string NameAr { get; set; } = string.Empty;
            public string NameEn { get; set; } = string.Empty;
            public string ParentCode { get; set; } = string.Empty;
            public string TypeStr { get; set; } = string.Empty;
            public string CatStr { get; set; } = string.Empty;
            public string NatureStr { get; set; } = string.Empty;
            public string ControlStr { get; set; } = string.Empty;
            public string SubLedgerStr { get; set; } = string.Empty;
            public string RecStr { get; set; } = string.Empty;
            public string CostCenterStr { get; set; } = string.Empty;
            public string CurrCode { get; set; } = string.Empty;
            public string UsageStr { get; set; } = string.Empty;
        }

        private static List<RawAccountRow> ExtractRawRows(Stream inputStream)
        {
            using var ms = new MemoryStream();
            if (inputStream.CanSeek)
                inputStream.Position = 0;
            inputStream.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            if (bytes.Length == 0)
                return new List<RawAccountRow>();

            // Check if file is XLSX (ZIP header PK = 0x50, 0x4B)
            bool isZip = bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
            if (isZip)
            {
                using var workbookStream = new MemoryStream(bytes);
                using var workbook = new XLWorkbook(workbookStream);
                var ws = workbook.Worksheets.FirstOrDefault(w => w.Name == "شجرة الحسابات") ?? workbook.Worksheets.FirstOrDefault();
                if (ws == null)
                    return new List<RawAccountRow>();

                var list = new List<RawAccountRow>();
                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                for (int r = 2; r <= lastRow; r++)
                {
                    var row = ws.Row(r);
                    list.Add(new RawAccountRow
                    {
                        RowNumber = r,
                        Code = row.Cell(1).GetString().Trim(),
                        NameAr = row.Cell(2).GetString().Trim(),
                        NameEn = row.Cell(3).GetString().Trim(),
                        ParentCode = row.Cell(4).GetString().Trim(),
                        TypeStr = row.Cell(5).GetString().Trim(),
                        CatStr = row.Cell(6).GetString().Trim(),
                        NatureStr = row.Cell(7).GetString().Trim(),
                        ControlStr = row.Cell(8).GetString().Trim(),
                        SubLedgerStr = row.Cell(9).GetString().Trim(),
                        RecStr = row.Cell(10).GetString().Trim(),
                        CostCenterStr = row.Cell(11).GetString().Trim(),
                        CurrCode = row.Cell(12).GetString().Trim(),
                        UsageStr = row.Cell(13).GetString().Trim()
                    });
                }
                return list;
            }
            else
            {
                // Parse CSV (UTF-8 or Windows-1256 fallback)
                string text = Encoding.UTF8.GetString(bytes);
                if (text.Contains('\uFFFD'))
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    text = Encoding.GetEncoding(1256).GetString(bytes);
                }

                var csvRows = ParseCsvLines(text);
                var list = new List<RawAccountRow>();
                for (int i = 1; i < csvRows.Count; i++)
                {
                    var cols = csvRows[i];
                    if (cols.Count == 0) continue;
                    string GetCol(int idx) => idx < cols.Count ? cols[idx].Trim() : string.Empty;

                    list.Add(new RawAccountRow
                    {
                        RowNumber = i + 1,
                        Code = GetCol(0),
                        NameAr = GetCol(1),
                        NameEn = GetCol(2),
                        ParentCode = GetCol(3),
                        TypeStr = GetCol(4),
                        CatStr = GetCol(5),
                        NatureStr = GetCol(6),
                        ControlStr = GetCol(7),
                        SubLedgerStr = GetCol(8),
                        RecStr = GetCol(9),
                        CostCenterStr = GetCol(10),
                        CurrCode = GetCol(11),
                        UsageStr = GetCol(12)
                    });
                }
                return list;
            }
        }

        private static List<List<string>> ParseCsvLines(string csvText)
        {
            var result = new List<List<string>>();
            var currentRow = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < csvText.Length; i++)
            {
                char c = csvText[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csvText.Length && csvText[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                    }
                    else if (c == '\r' || c == '\n')
                    {
                        if (c == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                            i++;
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                        result.Add(currentRow);
                        currentRow = new List<string>();
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
            }

            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                result.Add(currentRow);
            }

            return result;
        }

        private static bool IsInstructionOrHeaderRow(string code, string nameAr, string parentCode)
        {
            if (code.Equals("كود الحساب", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("دليل وإرشادات", StringComparison.OrdinalIgnoreCase) ||
                code.Equals("العمود", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Instruction rows A..M where code is a single letter A-Z and NameAr is a field description
            if (code.Length == 1 && char.IsLetter(code[0]) &&
                (nameAr.Contains("كود الحساب") || nameAr.Contains("اسم الحساب") || nameAr.Contains("نوع الحساب") ||
                 nameAr.Contains("التصنيف المحاسبي") || nameAr.Contains("طبيعة الحساب") || nameAr.Contains("حساب مراقبة") ||
                 nameAr.Contains("نوع الدفتر") || nameAr.Contains("حساب تسوية") || nameAr.Contains("يتطلب مركز تكلفة") ||
                 nameAr.Contains("كود العملة") || nameAr.Contains("نطاق الاستخدام") || string.IsNullOrWhiteSpace(parentCode)))
            {
                return true;
            }

            return false;
        }

        private static void ApplyKnownInventoryCopyPasteFix(AccountImportRowDto row)
        {
            if (row.Code == "12020501" && row.NameAr == "مخزون قطع الغيار")
            {
                row.Code = "12030201";
                row.ParentCode = "120302";
                row.Warnings.Add("تم تصحيح كود الحساب تلقائياً من (12020501) إلى (12030201) وكود الأب إلى (120302) لمنع التكرار مع حساب العملاء.");
            }
            else if (row.Code == "12020501001" && row.NameAr == "مخزون اخر المده قطع غيار")
            {
                row.Code = "12030201001";
                row.ParentCode = "12030201";
                row.Warnings.Add("تم تصحيح كود الحساب تلقائياً من (12020501001) إلى (12030201001) وكود الأب إلى (12030201) لمنع التكرار مع حساب العملاء.");
            }
            else if (row.Code == "12020501" && row.NameAr == "مخزون المستلزمات")
            {
                row.Code = "12030301";
                row.ParentCode = "120303";
                row.Warnings.Add("تم تصحيح كود الحساب تلقائياً من (12020501) إلى (12030301) وكود الأب إلى (120303) لمنع التكرار مع حساب العملاء.");
            }
            else if (row.Code == "12020501001" && row.NameAr == "مخزون اخر المده مستلزمات")
            {
                row.Code = "12030301001";
                row.ParentCode = "12030301";
                row.Warnings.Add("تم تصحيح كود الحساب تلقائياً من (12020501001) إلى (12030301001) وكود الأب إلى (12030301) لمنع التكرار مع حساب العملاء.");
            }
        }

        private static int ComputeLevel(
            string code,
            Dictionary<string, AccountImportRowDto> fileRowByCode,
            Dictionary<string, int> dbLevelByCode)
        {
            int level = 1;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { code };
            string? currentParent = fileRowByCode.TryGetValue(code, out var selfRow) ? selfRow.ParentCode : null;

            while (!string.IsNullOrWhiteSpace(currentParent))
            {
                if (!visited.Add(currentParent))
                    break; // Prevent circular loop

                if (fileRowByCode.TryGetValue(currentParent, out var parentRow))
                {
                    level++;
                    currentParent = parentRow.ParentCode;
                }
                else if (dbLevelByCode.TryGetValue(currentParent, out int dbParentLevel))
                {
                    return dbParentLevel + level;
                }
                else
                {
                    break;
                }
            }

            return level;
        }

        private async Task EnsureReferencedCurrenciesExistAsync(List<RawAccountRow> rawRows, CancellationToken cancellationToken)
        {
            var standardCurrencies = new Dictionary<string, (string NameAr, string NameEn, string Symbol, decimal Rate)>(StringComparer.OrdinalIgnoreCase)
            {
                ["EGP"] = ("جنيه مصري", "Egyptian Pound", "ج.م", 1.0m),
                ["USD"] = ("دولار أمريكي", "US Dollar", "$", 48.5m),
                ["EUR"] = ("يورو", "Euro", "€", 53.0m),
                ["SAR"] = ("ريال سعودي", "Saudi Riyal", "ر.س", 12.9m),
                ["AED"] = ("درهم إماراتي", "UAE Dirham", "د.إ", 13.2m),
                ["CNY"] = ("يوان صيني", "Chinese Yuan", "¥", 6.8m),
                ["GBP"] = ("جنيه إسترليني", "British Pound", "£", 62.0m),
                ["KWD"] = ("دينار كويتي", "Kuwaiti Dinar", "د.ك", 158.0m),
                ["QAR"] = ("ريال قطري", "Qatari Riyal", "ر.ق", 13.3m)
            };

            var referencedCodes = rawRows
                .Select(r => r.CurrCode?.Trim().ToUpperInvariant())
                .Where(c => !string.IsNullOrWhiteSpace(c) && standardCurrencies.ContainsKey(c!))
                .Select(c => c!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (referencedCodes.Count == 0)
                return;

            var existingCodes = await _context.Currencies
                .IgnoreQueryFilters()
                .Select(c => c.Code)
                .ToListAsync(cancellationToken);

            var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
            bool anyAdded = false;

            foreach (var code in referencedCodes)
            {
                if (!existingSet.Contains(code) && standardCurrencies.TryGetValue(code, out var info))
                {
                    _context.Currencies.Add(new Currency
                    {
                        Code = code.ToUpperInvariant(),
                        NameAr = info.NameAr,
                        NameEn = info.NameEn,
                        Symbol = info.Symbol,
                        ExchangeRate = info.Rate,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                    anyAdded = true;
                }
            }

            if (anyAdded)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        private static AccountCategory ParseCategory(string catStr, string code)
        {
            if (string.IsNullOrWhiteSpace(catStr))
            {
                if (code.StartsWith("1")) return AccountCategory.Assets;
                if (code.StartsWith("2")) return AccountCategory.Liabilities;
                if (code.StartsWith("3")) return AccountCategory.Equity;
                if (code.StartsWith("4") || code.StartsWith("7")) return AccountCategory.Revenues;
                if (code.StartsWith("5") || code.StartsWith("6")) return AccountCategory.Expenses;
                return AccountCategory.Assets;
            }

            if (catStr.Contains("أصول") || catStr.Contains("اصول") || catStr.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return AccountCategory.Assets;
            if (catStr.Contains("خصوم") || catStr.Contains("التزام") || catStr.Equals("Liabilities", StringComparison.OrdinalIgnoreCase)) return AccountCategory.Liabilities;
            if (catStr.Contains("حقوق") || catStr.Contains("ملكية") || catStr.Equals("Equity", StringComparison.OrdinalIgnoreCase)) return AccountCategory.Equity;
            if (catStr.Contains("إيراد") || catStr.Contains("ايراد") || catStr.Equals("Revenues", StringComparison.OrdinalIgnoreCase)) return AccountCategory.Revenues;
            if (catStr.Contains("مصروف") || catStr.Contains("تكل") || catStr.Equals("Expenses", StringComparison.OrdinalIgnoreCase)) return AccountCategory.Expenses;

            return AccountCategory.Assets;
        }

        private static AccountNature ParseNature(string natureStr, string code)
        {
            if (string.IsNullOrWhiteSpace(natureStr))
            {
                if (code.StartsWith("1") || code.StartsWith("5") || code.StartsWith("6")) return AccountNature.Debit;
                return AccountNature.Credit;
            }

            return natureStr.Contains("دائن") || natureStr.Equals("Credit", StringComparison.OrdinalIgnoreCase)
                ? AccountNature.Credit
                : AccountNature.Debit;
        }

        private static SubLedgerType ParseSubLedger(string subStr)
        {
            if (string.IsNullOrWhiteSpace(subStr)) return SubLedgerType.None;
            if (subStr.Contains("عميل") || subStr.Contains("عملاء") || subStr.Equals("Customer", StringComparison.OrdinalIgnoreCase)) return SubLedgerType.Customer;
            if (subStr.Contains("مورد") || subStr.Equals("Vendor", StringComparison.OrdinalIgnoreCase)) return SubLedgerType.Vendor;
            if (subStr.Contains("بنك") || subStr.Contains("خزين") || subStr.Equals("Bank", StringComparison.OrdinalIgnoreCase)) return SubLedgerType.Bank;
            if (subStr.Contains("موظف") || subStr.Contains("عهد") || subStr.Equals("Employee", StringComparison.OrdinalIgnoreCase)) return SubLedgerType.Employee;
            return SubLedgerType.None;
        }

        private static AccountUsage ParseUsage(string usageStr)
        {
            if (string.IsNullOrWhiteSpace(usageStr)) return AccountUsage.All;
            if (usageStr.Contains("قيد") || usageStr.Contains("قيود")) return AccountUsage.JournalVoucher;
            if (usageStr.Contains("صرف")) return AccountUsage.Payment;
            if (usageStr.Contains("قبض")) return AccountUsage.Receipt;
            return AccountUsage.All;
        }
    }
}
