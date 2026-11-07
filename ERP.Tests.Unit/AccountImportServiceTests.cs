using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP.Tests.Unit
{
    public class AccountImportServiceTests
    {
        private ErpDbContext CreateDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ErpDbContext(options);
        }

        [Fact]
        public void GenerateTemplate_ReturnsValidExcelWorkbookWithStandardSheets()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);
            var service = new AccountImportService(db);

            byte[] bytes = service.GenerateTemplate();

            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);

            using var stream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(stream);

            Assert.Equal(2, workbook.Worksheets.Count);
            Assert.Contains(workbook.Worksheets, w => w.Name == "شجرة الحسابات");
            Assert.Contains(workbook.Worksheets, w => w.Name == "تعليمات وإرشادات الاستيراد");

            var ws = workbook.Worksheet("شجرة الحسابات");
            Assert.Equal("كود الحساب", ws.Cell(1, 1).GetString());
            Assert.Equal("اسم الحساب (عربي)", ws.Cell(1, 2).GetString());
        }

        [Fact]
        public async Task ImportAccountsAsync_ValidData_InsertsGLAccountsSuccessfully()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            // Add standard currencies
            db.Currencies.Add(new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1m, IsActive = true });
            db.Currencies.Add(new Currency { Id = 2, Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", ExchangeRate = 50m, IsActive = true });
            await db.SaveChangesAsync();

            var service = new AccountImportService(db);

            // Generate template and feed it back to import
            byte[] templateBytes = service.GenerateTemplate();
            using var stream = new MemoryStream(templateBytes);

            // 1. Preview
            var preview = await service.ValidateAndPreviewAsync(stream);
            Assert.True(preview.CanExecute, $"Preview errors: {preview.ErrorCount}");
            Assert.NotEmpty(preview.Rows);

            // 2. Import
            stream.Position = 0;
            var result = await service.ExecuteImportAsync(stream, updateExisting: true);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.True(result.TotalCreated > 0);

            // Verify in DB
            var accounts = await db.GLAccounts.ToListAsync();
            Assert.NotEmpty(accounts);

            // Verify Level 1 root accounts exist
            var assets = accounts.FirstOrDefault(a => a.Code == "1");
            Assert.NotNull(assets);
            Assert.True(assets.IsGroup);
            Assert.Equal(1, assets.Level);
            Assert.Equal(AccountCategory.Assets, assets.AccountCategory);

            // Verify AR Control Account
            var ar = accounts.FirstOrDefault(a => a.Code == "110201");
            Assert.NotNull(ar);
            Assert.False(ar.IsGroup);
            Assert.True(ar.IsControlAccount);
            Assert.Equal(SubLedgerType.Customer, ar.SubLedgerType);
            Assert.NotNull(ar.ParentId);
        }

        [Fact]
        public async Task ImportAccountsAsync_CsvWithInstructionsAndDuplicateInventoryCodes_AutoFixesAndImportsCleanly()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);
            var service = new AccountImportService(db);

            string csvContent = @"كود الحساب,اسم الحساب (عربي),اسم الحساب (إنجليزي),كود الحساب الأب,نوع الحساب,التصنيف المحاسبي,طبيعة الحساب,حساب مراقبة,نوع الدفتر المساعد,حساب تسوية,يتطلب مركز تكلفة,كود العملة,نطاق الاستخدام
1,الاصول,Assets,,مجموعة رئيسية (تجميعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
12,الأصول المتداوله,Current assets,1,مجموعة رئيسية (تجميعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
1201,النقديه  ومافي حكمها,Cash and its equivalents,12,مجموعة رئيسية (تجميعي),أصول,مدين,لا,بنوك وخزائن,لا,لا,EGP,الكل
120101,بنوك,Banks,1201,مجموعة رئيسية (تجميعي),أصول,مدين,لا,بنوك وخزائن,لا,لا,EGP,الكل
12010105,بنوك درهم,Dirham banks,120101,مجموعة رئيسية (تجميعي),أصول,مدين,لا,بنوك وخزائن,لا,لا,AED,الكل
12010105001,بنك الامارت ا. ياسر دولة الامارت درهم - جسر السويس,""Emirates Bank, Yasser, United Arab Emirates, Dirham - Suez Bridge"",12010105,حساب حركة (فرعي),أصول,مدين,لا,بنوك وخزائن,لا,لا,AED,الكل
1202,العملاء,Customers,12,مجموعة رئيسية (تجميعي),أصول,مدين,لا,عهد موظفين,لا,لا,EGP,الكل
120205,العملاء يورو ماكينات,Euro Machines Customers,1202,مجموعة رئيسية (تجميعي),أصول,مدين,لا,عهد موظفين,لا,لا,EUR,الكل
12020501,صافي العملاء يورو ماكينات,Net customer Euro machines,120205,مجموعة رئيسية (تجميعي),أصول,مدين,لا,عهد موظفين,لا,لا,EUR,الكل
12020501001,عملاء يورو ماكينات,Euro Machines customers,12020501,حساب حركة (فرعي),أصول,مدين,لا,,لا,لا,EUR,الكل
1203,المخزون,Inventory,12,مجموعة رئيسية (تجميعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
120302,مخزون قطع غيار,spare parts stock,1203,مجموعة رئيسية (تجميعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
12020501,مخزون قطع الغيار,spare parts stock,120205,مجموعة رئيسية (تجميعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
12020501001,مخزون اخر المده قطع غيار,End-of-term stock of spare parts,12020501,حساب حركة (فرعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
120303,مخزون مستلزمات,Supplies inventory,1203,مجموعة رئيسية (تجميعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
12020501,مخزون المستلزمات,Supplies inventory,120205,مجموعة رئيسية (تجميعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
12020501001,مخزون اخر المده مستلزمات,End-of-term inventory supplies,12020501,حساب حركة (فرعي),أصول,مدين,لا,لا شيء,لا,لا,EGP,الكل
دليل وإرشادات ملء ملف شجرة الحسابات (GLAccounts),,
,,
العمود,اسم الحقل,الشرح وقواعد الإدخال
A,كود الحساب (إلزامي),""رقم الحساب الفريد (مثل: 1, 11, 1101, 110101). يجب ألا يتكرر في الملف أو قاعدة البيانات.""
";

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));
            var preview = await service.ValidateAndPreviewAsync(stream);

            Assert.Equal(0, preview.ErrorCount);
            Assert.True(preview.CanExecute);
            Assert.Contains(preview.Rows, r => r.Code == "12030201" && r.ParentCode == "120302");
            Assert.Contains(preview.Rows, r => r.Code == "12030201001" && r.ParentCode == "12030201");
            Assert.Contains(preview.Rows, r => r.Code == "12030301" && r.ParentCode == "120303");
            Assert.Contains(preview.Rows, r => r.Code == "12030301001" && r.ParentCode == "12030301");

            var result = await service.ExecuteImportAsync(stream, updateExisting: true);
            Assert.True(result.Success, result.Message);
            Assert.Equal(17, result.TotalCreated);
        }

        [Fact]
        public void GenerateCustomerAndVendorExcelFiles_WritesTemplatesToWorkspace()
        {
            string rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

            // 1. Generate Customers Excel File
            using (var wbCust = new XLWorkbook())
            {
                var ws = wbCust.Worksheets.Add("العملاء");
                ws.RightToLeft = true;
                ws.ShowGridLines = true;

                string[] headers = {
                    "كود الفرع", "كود العميل", "اسم العميل (عربي)", "اسم العميل (إنجليزي)",
                    "فئة العميل", "كود الحساب المالي", "كود العملة", "المندوب",
                    "الرقم الضريبي", "السجل التجاري", "نوع الممول", "المحافظة",
                    "المدينة", "العنوان", "الموبايل 1", "الموبايل 2",
                    "الهاتف", "البريد الإلكتروني", "ملاحظات"
                };

                for (int col = 0; col < headers.Length; col++)
                {
                    var cell = ws.Cell(1, col + 1);
                    cell.Value = headers[col];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Font.FontSize = 11;
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93);
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                }
                ws.Row(1).Height = 28;

                var samples = new[]
                {
                    new[] { "B001", "", "شركة النور للمنسوجات والملابس", "Al-Nour Textiles & Garments Co.", "", "12020101001", "EGP", "", "100-200-300", "45821", "شركة", "القاهرة", "مدينة نصر", "شارع عباس العقاد - مدينة نصر", "01001234567", "01221234567", "0222745678", "info@alnour-tex.com", "عميل ماكينات جملة" },
                    new[] { "B002", "", "مصنع التوحيد للملابس الجاهزة", "Al-Tawheed Ready Wear Factory", "", "12020201001", "EGP", "", "200-300-400", "78912", "شركة", "القليوبية", "شبرا الخيمة", "المنطقة الصناعية - شبرا الخيمة", "01112345678", "", "0242211223", "sales@tawheed.com", "عميل قطع غيار" },
                    new[] { "B001", "", "أحمد محمد عبد الرحمن", "Ahmed Mohamed Abdelrahman", "", "12020101001", "EGP", "", "", "", "فرد", "الجيزة", "السادس من أكتوبر", "المنطقة الصناعية الثالثة - 6 أكتوبر", "01551234567", "", "", "", "عميل تجزئة" },
                    new[] { "B001", "", "شركة الدلتا للتصدير والتوريدات", "Delta Export & Supplies", "", "12020301001", "USD", "", "300-400-500", "96325", "شركة", "الإسكندرية", "سموحة", "طريق الحرية - الإسكندرية", "01098765432", "", "034256789", "contact@delta-exp.com", "عميل تعامل بالدولار" }
                };

                for (int r = 0; r < samples.Length; r++)
                {
                    int rowIdx = r + 2;
                    for (int c = 0; c < samples[r].Length; c++)
                    {
                        var cell = ws.Cell(rowIdx, c + 1);
                        cell.Style.NumberFormat.Format = "@";
                        cell.SetValue(samples[r][c]);
                        cell.Style.Font.FontName = "Segoe UI";
                        cell.Style.Font.FontSize = 10;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(220, 224, 230);
                    }
                    ws.Row(rowIdx).Height = 22;
                }

                ws.Range(2, 7, 1000, 7).CreateDataValidation().List("\"EGP,USD,EUR,SAR,AED,CNY,GBP\"", true);
                ws.Range(2, 11, 1000, 11).CreateDataValidation().List("\"شركة,فرد,هيئة حكومية,أجنبي\"", true);
                ws.Columns(1, headers.Length).AdjustToContents();
                ws.Column(3).Width = 32;
                ws.Column(4).Width = 30;
                ws.Column(14).Width = 30;
                ws.Column(19).Width = 25;

                var wsHelp = wbCust.Worksheets.Add("تعليمات وإرشادات الاستيراد");
                wsHelp.RightToLeft = true;
                wsHelp.Cell(1, 1).Value = "دليل وإرشادات ملء ملف استيراد العملاء (Customers)";
                wsHelp.Range(1, 1, 1, 3).Merge();
                wsHelp.Row(1).Height = 30;
                wsHelp.Cell(1, 1).Style.Font.Bold = true;
                wsHelp.Cell(1, 1).Style.Font.FontSize = 14;
                wsHelp.Cell(1, 1).Style.Font.FontColor = XLColor.White;
                wsHelp.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93);
                wsHelp.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var instructions = new (string Col, string Field, string Desc)[]
                {
                    ("A", "كود الفرع (اختياري)", "كود الفرع (مثل: B001 للمركز الرئيسي ماكينات، B002 لقطع الغيار). إذا تُرك فارغاً يتم الاستيراد على الفرع الحالي للمستخدم."),
                    ("B", "كود العميل (اختياري)", "يُترك فارغاً ليقوم النظام بتوليد كود تلقائي متسلسل للفرع، أو يُكتب الكود اليدوي."),
                    ("C", "اسم العميل عربي (إلزامي)", "الاسم التجاري أو الرسمي للعميل باللغة العربية."),
                    ("D", "اسم العميل إنجليزي (اختياري)", "الاسم باللغة الإنجليزية (إذا تُرك فارغاً يتم نسخه تلقائياً من الاسم العربي)."),
                    ("E", "فئة العميل (اختياري)", "كود أو اسم فئة العملاء. إذا تُرك فارغاً يتم ربط العميل تلقائياً بالفئة الافتراضية للفرع."),
                    ("F", "كود الحساب المالي (اختياري)", "كود حساب العميل في شجرة الحسابات: 12020101001 (عملاء مصري ماكينات)، 12020201001 (عملاء مصري قطع غيار)، 12020301001 (عملاء دولار ماكينات)، 12020401001 (عملاء دولار قطع غيار)، 12020501001 (عملاء يورو ماكينات)."),
                    ("G", "كود العملة (اختياري)", "رمز العملة الافتراضية للعميل (EGP, USD, EUR, SAR, AED). افتراضياً EGP."),
                    ("H", "المندوب (اختياري)", "كود أو اسم مندوب المبيعات التابع لنفس الفرع."),
                    ("I", "الرقم الضريبي (اختياري)", "رقم التسجيل الضريبي للعميل."),
                    ("J", "السجل التجاري (اختياري)", "رقم السجل التجاري للعميل."),
                    ("K", "نوع الممول (اختياري)", "شركة، فرد، هيئة حكومية، أو أجنبي."),
                    ("L", "المحافظة (اختياري)", "اسم أو كود المحافظة (مثل: القاهرة، الجيزة، الإسكندرية، القليوبية)."),
                    ("M", "المدينة (اختياري)", "اسم أو كود المدينة التابعة للمحافظة."),
                    ("N", "العنوان (اختياري)", "العنوان التفصيلي للعميل."),
                    ("O", "الموبايل 1 (اختياري)", "رقم الهاتف المحمول الأساسي."),
                    ("P", "الموبايل 2 (اختياري)", "رقم الهاتف المحمول الإضافي."),
                    ("Q", "الهاتف (اختياري)", "رقم الهاتف الأرضي."),
                    ("R", "البريد الإلكتروني (اختياري)", "البريد الإلكتروني للعميل."),
                    ("S", "ملاحظات (اختياري)", "أي ملاحظات إضافية خاصة بالعميل.")
                };

                wsHelp.Cell(3, 1).Value = "العمود";
                wsHelp.Cell(3, 2).Value = "اسم الحقل";
                wsHelp.Cell(3, 3).Value = "الشرح وقواعد الإدخال";
                wsHelp.Range(3, 1, 3, 3).Style.Font.Bold = true;
                wsHelp.Range(3, 1, 3, 3).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);
                for (int i = 0; i < instructions.Length; i++)
                {
                    int r = i + 4;
                    wsHelp.Cell(r, 1).SetValue(instructions[i].Col);
                    wsHelp.Cell(r, 2).SetValue(instructions[i].Field);
                    wsHelp.Cell(r, 3).SetValue(instructions[i].Desc);
                }
                wsHelp.Column(1).Width = 10;
                wsHelp.Column(2).Width = 28;
                wsHelp.Column(3).Width = 85;

                wbCust.SaveAs(Path.Combine(rootDir, "نموذج_استيراد_العملاء.xlsx"));
            }

            // 2. Generate Vendors Excel File
            using (var wbVend = new XLWorkbook())
            {
                var ws = wbVend.Worksheets.Add("الموردين");
                ws.RightToLeft = true;
                ws.ShowGridLines = true;

                string[] headers = {
                    "كود الفرع", "كود المورد", "اسم المورد (عربي)", "اسم المورد (إنجليزي)",
                    "فئة المورد", "كود الحساب المالي", "كود العملة", "الرقم الضريبي",
                    "السجل التجاري", "نوع الممول", "المحافظة", "المدينة",
                    "العنوان", "الموبايل 1", "الموبايل 2", "الهاتف",
                    "البريد الإلكتروني", "ملاحظات"
                };

                for (int col = 0; col < headers.Length; col++)
                {
                    var cell = ws.Cell(1, col + 1);
                    cell.Value = headers[col];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Font.FontSize = 11;
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93);
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                }
                ws.Row(1).Height = 28;

                var samples = new[]
                {
                    new[] { "B001", "", "الشركة الدولية لتوريد الماكينات الصناعية", "International Industrial Machines Co.", "", "21010101001", "EGP", "410-520-630", "11223", "شركة", "القاهرة", "مصر الجديدة", "شارع جسر السويس - القاهرة", "01009988776", "01229988776", "0226345678", "supply@intl-machines.com", "مورد ماكينات محلي" },
                    new[] { "B002", "", "مؤسسة الأمل لقطع غيار ماكينات الخياطة", "Al-Amal Sewing Spare Parts Est.", "", "21010201001", "EGP", "510-620-730", "33445", "شركة", "القاهرة", "مدينة نصر", "شارع الرويعي - العتبة", "01119988776", "", "0225912345", "sales@alamal-parts.com", "مورد قطع غيار محلي" },
                    new[] { "B001", "", "شركة جاك العالمية للماكينات - الصين", "JACK Sewing Machine Co. Ltd", "", "21010301001", "USD", "", "", "أجنبي", "", "", "Taizhou, Zhejiang, China", "+8613800138000", "", "", "export@chinajack.com", "مورد خارجي بالدولار - ماكينات" },
                    new[] { "B001", "", "شركة ستروبل الألمانية للماكينات", "Strobel Spezialmaschinen GmbH", "", "21010501001", "EUR", "", "", "أجنبي", "", "", "Munich, Germany", "+4989123456", "", "", "info@strobel.de", "مورد خارجي باليورو - ماكينات" }
                };

                for (int r = 0; r < samples.Length; r++)
                {
                    int rowIdx = r + 2;
                    for (int c = 0; c < samples[r].Length; c++)
                    {
                        var cell = ws.Cell(rowIdx, c + 1);
                        cell.Style.NumberFormat.Format = "@";
                        cell.SetValue(samples[r][c]);
                        cell.Style.Font.FontName = "Segoe UI";
                        cell.Style.Font.FontSize = 10;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(220, 224, 230);
                    }
                    ws.Row(rowIdx).Height = 22;
                }

                ws.Range(2, 7, 1000, 7).CreateDataValidation().List("\"EGP,USD,EUR,SAR,AED,CNY,GBP\"", true);
                ws.Range(2, 10, 1000, 10).CreateDataValidation().List("\"شركة,فرد,هيئة حكومية,أجنبي\"", true);
                ws.Columns(1, headers.Length).AdjustToContents();
                ws.Column(3).Width = 34;
                ws.Column(4).Width = 30;
                ws.Column(13).Width = 30;
                ws.Column(18).Width = 25;

                var wsHelp = wbVend.Worksheets.Add("تعليمات وإرشادات الاستيراد");
                wsHelp.RightToLeft = true;
                wsHelp.Cell(1, 1).Value = "دليل وإرشادات ملء ملف استيراد الموردين (Vendors)";
                wsHelp.Range(1, 1, 1, 3).Merge();
                wsHelp.Row(1).Height = 30;
                wsHelp.Cell(1, 1).Style.Font.Bold = true;
                wsHelp.Cell(1, 1).Style.Font.FontSize = 14;
                wsHelp.Cell(1, 1).Style.Font.FontColor = XLColor.White;
                wsHelp.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93);
                wsHelp.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var instructions = new (string Col, string Field, string Desc)[]
                {
                    ("A", "كود الفرع (اختياري)", "كود الفرع (مثل: B001 للمركز الرئيسي ماكينات، B002 لقطع الغيار). إذا تُرك فارغاً يتم الاستيراد على الفرع الحالي للمستخدم."),
                    ("B", "كود المورد (اختياري)", "يُترك فارغاً ليقوم النظام بتوليد كود تلقائي متسلسل للفرع، أو يُكتب الكود اليدوي."),
                    ("C", "اسم المورد عربي (إلزامي)", "الاسم التجاري أو الرسمي للمورد باللغة العربية."),
                    ("D", "اسم المورد إنجليزي (اختياري)", "الاسم باللغة الإنجليزية (إذا تُرك فارغاً يتم نسخه تلقائياً من الاسم العربي)."),
                    ("E", "فئة المورد (اختياري)", "كود أو اسم فئة الموردين. إذا تُرك فارغاً يتم ربط المورد تلقائياً بالفئة الافتراضية للفرع."),
                    ("F", "كود الحساب المالي (اختياري)", "كود حساب المورد في شجرة الحسابات: 21010101001 (الموردين المصري ماكينات)، 21010201001 (الموردين المصري قطع غيار)، 21010301001 (الموردين الدولار ماكينات)، 21010401001 (الموردين الدولار قطع غيار)، 21010501001 (الموردين اليورو ماكينات)، 21010601001 (الموردين اليورو قطع غيار)."),
                    ("G", "كود العملة (اختياري)", "رمز العملة الافتراضية للمورد (EGP, USD, EUR, SAR, AED, CNY). افتراضياً EGP."),
                    ("H", "الرقم الضريبي (اختياري)", "رقم التسجيل الضريبي للمورد."),
                    ("I", "السجل التجاري (اختياري)", "رقم السجل التجاري للمورد."),
                    ("J", "نوع الممول (اختياري)", "شركة، فرد، هيئة حكومية، أو أجنبي."),
                    ("K", "المحافظة (اختياري)", "اسم أو كود المحافظة (للموردين المحليين)."),
                    ("L", "المدينة (اختياري)", "اسم أو كود المدينة التابعة للمحافظة."),
                    ("M", "العنوان (اختياري)", "العنوان التفصيلي للمورد."),
                    ("N", "الموبايل 1 (اختياري)", "رقم الهاتف المحمول الأساسي."),
                    ("O", "الموبايل 2 (اختياري)", "رقم الهاتف المحمول الإضافي."),
                    ("P", "الهاتف (اختياري)", "رقم الهاتف الأرضي أو الدولي."),
                    ("Q", "البريد الإلكتروني (اختياري)", "البريد الإلكتروني للمورد."),
                    ("R", "ملاحظات (اختياري)", "أي ملاحظات إضافية خاصة بالمورد.")
                };

                wsHelp.Cell(3, 1).Value = "العمود";
                wsHelp.Cell(3, 2).Value = "اسم الحقل";
                wsHelp.Cell(3, 3).Value = "الشرح وقواعد الإدخال";
                wsHelp.Range(3, 1, 3, 3).Style.Font.Bold = true;
                wsHelp.Range(3, 1, 3, 3).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);
                for (int i = 0; i < instructions.Length; i++)
                {
                    int r = i + 4;
                    wsHelp.Cell(r, 1).SetValue(instructions[i].Col);
                    wsHelp.Cell(r, 2).SetValue(instructions[i].Field);
                    wsHelp.Cell(r, 3).SetValue(instructions[i].Desc);
                }
                wsHelp.Column(1).Width = 10;
                wsHelp.Column(2).Width = 28;
                wsHelp.Column(3).Width = 85;

                wbVend.SaveAs(Path.Combine(rootDir, "نموذج_استيراد_الموردين.xlsx"));
            }

            Assert.True(File.Exists(Path.Combine(rootDir, "نموذج_استيراد_العملاء.xlsx")));
            Assert.True(File.Exists(Path.Combine(rootDir, "نموذج_استيراد_الموردين.xlsx")));
        }
    }
}
