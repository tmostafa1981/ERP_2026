# دليل اختبار التحمل والتزامن لنظام ERP 2026 (NBomber Concurrency Suite)

تم تصميم هذا المشروع بواسطة مهندس الجودة والأداء (Performance & QA Engineer) لاختبار بيئة C# و .NET 8 و SQL Server تحت ضغط تزامن عالٍ (Concurrency Stress Testing) عبر **42 فرعاً**.

---

## 1. أهداف الاختبار (Testing Objectives)
* **محاكاة 50 مستخدماً متزامناً** يرسلون طلبات إنشاء فواتير مبيعات في نفس الثانية.
* **توزيع الفروع (Multi-Tenant Scoping)**: تغطية 42 فرعاً بالتساوي مع توليد أصناف وأسعار وكميات مختلفة ديناميكياً.
* **اكتشاف الاختناقات (Bottlenecks)**: رصد أقفال التنافس (Deadlocks - Error 1205) ومهلات الانتظار (Lock Request Timeouts - Error 1222).
* **حماية الترقيم التلقائي**: التحقق من عدم حدوث تكرار في أرقام الفواتير أو فجوات (Sequence Gaps).
* **حماية المخزون والحسابات التراكمية**: التحقق من عدم انخفاض أرصدة الأصناف بالسالب والتأكد من مطابقة الكميات المباعة للأرصدة.

---

## 2. هيكل المشروع (Project Structure)
```
ERP.LoadTesting/
├── ERP.LoadTesting.csproj                     # تعريف الحزم (NBomber v5.6.0, Microsoft.Data.SqlClient)
├── Program.cs                                 # سيناريو NBomber ونقطة الانطلاق (Console App)
├── Models/
│   ├── LoadTestConfig.cs                      # إعدادات معدل التزامن، المدة، ونمط التنفيذ
│   └── InvoicePayload.cs                      # نماذج البيانات (Header & Lines DTOs)
├── Services/
│   ├── PayloadGenerator.cs                    # توليد بيانات عشوائية واقعية للفروع والأصناف
│   ├── InvoiceApiClient.cs                    # النمط الأول: إرسال عبر HTTP POST /api/invoices
│   └── InvoiceSqlRepository.cs                # النمط الثاني: استدعاء مباشر عبر ADO.NET لـ SP
├── Scripts/
│   ├── 01_Setup_LoadTest_Database_Objects.sql # إعداد الجداول، نوع TVP، والإجراء المخزن
│   ├── 02_Validation_InvoiceSequencesAndGaps.sql # استعلام التحقق من التسلسل والفجوات
│   ├── 03_Validation_InventoryConsistencyAndRunningTotals.sql # استعلام مطابقة المخزون والتراكمي
│   └── 04_Diagnostics_DeadlocksAndLockWaits.sql # استخراج Deadlock Graphs وإحصائيات الأقفال
```

---

## 3. خطوات التشغيل (How to Run)

### الخطوة الأولى: تجهيز قاعدة البيانات
افتح ملف `Scripts/01_Setup_LoadTest_Database_Objects.sql` في SQL Server Management Studio (SSMS) ونفذه.
سيقوم بـ:
1. إنشاء الجداول `DocumentHeaders` و `DocumentLines` و `CodeSequences` و `ItemStocks`.
2. إنشاء نوع الجدول `dbo.InvoiceLineTableType` (TVP).
3. تغذية أرصدة افتتاحية لـ 42 فرعاً و 30 صنفاً لكل فرع.
4. إنشاء الإجراء المخزن `dbo.sp_CreateInvoiceWithItems` المزود بحماية ذرية (`UPDLOCK, ROWLOCK`).

### الخطوة الثانية: تشغيل اختبار NBomber
افتح موجه الأوامر (Terminal) في مجلد المشروع ونفذ:
```bash
cd o:\Tamer\ERP_2026\ERP\ERP.LoadTesting
dotnet run
```
* سيظهر لك خيار لاختيار النمط:
  * اضغط `1` لتنفيذ الإجراء المخزن مباشرة عبر ADO.NET (أسرع وأدق لاختبار ضغط المحرك).
  * اضغط `2` لاختبار الـ Web API عبر بروتوكول HTTP.
* سيبدأ NBomber في ضخ 50 طلب/ثانية لمدة 30 ثانية (أو حسب الإعداد المحدد).
* سيتم إنشاء تقرير إحصائي كامل بصيغة HTML في المجلد `nbomber_reports`.

### الخطوة الثالثة: تنفيذ استعلامات التحقق والتدقيق (Post-Test Validations)
بعد انتهاء NBomber، نفذ الاستعلامات التالية في SSMS:
1. **فحص أرقام الفواتير والتسلسل**: نفذ `02_Validation_InvoiceSequencesAndGaps.sql`.
   - يجب أن تكون النتيجة 0 صفوف في فحوصات التكرار والفجوات.
2. **فحص المخزون والحسابات التراكمية**: نفذ `03_Validation_InventoryConsistencyAndRunningTotals.sql`.
   - يتحقق من عدم وجود رصيد سالب (`CurrentBalance < 0`).
   - يطابق `InitialBalance - TotalSold = CurrentBalance`.
   - يطابق إجمالي الفواتير برأس المستند مع مجموع سطورها.
3. **تشخيص الأقفال والـ Deadlocks**: نفذ `04_Diagnostics_DeadlocksAndLockWaits.sql`.
   - يعرض مخططات الـ Deadlock بدقة بصيغة XML في حال حدوث أي تعارض، مع إحصائيات `LCK_M_X`.
