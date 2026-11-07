/**
 * ERP 2026 - Reusable Excel Copy/Paste Grid Engine
 * File: wwwroot/js/erp-grid-paste.js
 * Supports:
 *  - Excel TSV parsing (multi-row, quoted values, Arabic numbers, dates)
 *  - Intelligent Header Detection & Column Mapping
 *  - Payment Method Recognition (Cash, Cheque, Bank Transfer, Other in Ar/En/Numeric)
 *  - Batch Lookup Resolution via ErpLookup.resolveBatch
 *  - Interactive Preview Modal with validation and error badges
 *  - Append vs. Replace modes
 *  - Reusable across Vouchers, Journal Entries, Invoices
 */
(function (window, $) {
    'use strict';

    const ErpGridPaste = {
        /**
         * Arabic to Western digit normalizer
         */
        normalizeDigits: function (str) {
            if (!str) return '';
            const arabicDigits = ['٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩'];
            return String(str).replace(/[٠-٩]/g, function (d) {
                return arabicDigits.indexOf(d);
            });
        },

        /**
         * Parses clipboard TSV into 2D array of cells
         */
        parseTSV: function (text) {
            if (!text || typeof text !== 'string') return [];
            text = this.normalizeDigits(text.trim());

            const rows = [];
            let currentRow = [];
            let currentCell = '';
            let inQuotes = false;

            for (let i = 0; i < text.length; i++) {
                const char = text[i];
                const nextChar = text[i + 1];

                if (char === '"') {
                    if (inQuotes && nextChar === '"') {
                        currentCell += '"';
                        i++; // skip escaped quote
                    } else {
                        inQuotes = !inQuotes;
                    }
                } else if (char === '\t' && !inQuotes) {
                    currentRow.push(currentCell.trim());
                    currentCell = '';
                } else if ((char === '\r' || char === '\n') && !inQuotes) {
                    if (char === '\r' && nextChar === '\n') {
                        i++; // skip \n of \r\n
                    }
                    currentRow.push(currentCell.trim());
                    if (currentRow.some(cell => cell.length > 0)) {
                        rows.push(currentRow);
                    }
                    currentRow = [];
                    currentCell = '';
                } else {
                    currentCell += char;
                }
            }

            if (currentCell.length > 0 || currentRow.length > 0) {
                currentRow.push(currentCell.trim());
                if (currentRow.some(cell => cell.length > 0)) {
                    rows.push(currentRow);
                }
            }

            return rows;
        },

        /**
         * Recognizes payment method from string (English, Arabic, or number)
         * Returns: 1 (Cash), 2 (Cheque), 3 (BankTransfer), 4 (Other)
         */
        parsePaymentMethod: function (val) {
            if (!val) return 1;
            const clean = String(val).toLowerCase().replace(/[\s\-_]/g, '');

            if (clean === '1' || clean === 'cash' || clean === 'نقدي' || clean === 'كاش' || clean === 'نقدا') {
                return 1;
            }
            if (clean === '2' || clean === 'cheque' || clean === 'check' || clean === 'شيك') {
                return 2;
            }
            if (clean === '3' || clean === 'banktransfer' || clean === 'transfer' || clean === 'تحويل' || clean === 'تحويلبنكي' || clean === 'بنكي') {
                return 3;
            }
            if (clean === '4' || clean === 'other' || clean === 'اخرى' || clean === 'أخرى' || clean === 'اخر') {
                return 4;
            }

            // Fallback substring checks
            if (clean.includes('شيك') || clean.includes('cheq') || clean.includes('chk')) return 2;
            if (clean.includes('تحويل') || clean.includes('transf')) return 3;
            if (clean.includes('نقد') || clean.includes('cash')) return 1;
            if (clean.includes('اخر') || clean.includes('other')) return 4;

            return 1; // default Cash
        },

        /**
         * Parses numeric amount safely
         */
        parseNumber: function (val) {
            if (!val) return 0;
            const clean = this.normalizeDigits(String(val)).replace(/,/g, '').trim();
            const num = parseFloat(clean);
            return isNaN(num) ? 0 : Math.round(num * 10000) / 10000;
        },

        /**
         * Normalizes date to YYYY-MM-DD
         */
        parseDate: function (val) {
            if (!val) return '';
            val = this.normalizeDigits(String(val)).trim();

            // Format YYYY-MM-DD
            if (/^\d{4}-\d{1,2}-\d{1,2}$/.test(val)) {
                const parts = val.split('-');
                return `${parts[0]}-${parts[1].padStart(2, '0')}-${parts[2].padStart(2, '0')}`;
            }

            // Format DD/MM/YYYY or DD-MM-YYYY
            const dmy = val.match(/^(\d{1,2})[\/\.-](\d{1,2})[\/\.-](\d{4})$/);
            if (dmy) {
                return `${dmy[3]}-${dmy[2].padStart(2, '0')}-${dmy[1].padStart(2, '0')}`;
            }

            // Date object parse fallback
            const parsed = new Date(val);
            if (!isNaN(parsed.getTime())) {
                return parsed.toISOString().split('T')[0];
            }

            return '';
        },

        /**
         * Matches headers to schema fields
         */
        detectColumnMapping: function (rawHeaders, schema) {
            const mapping = [];
            const usedKeys = new Set();

            rawHeaders.forEach((headerText, colIndex) => {
                const clean = String(headerText).toLowerCase().replace(/[\s\-_]/g, '');
                let matchedField = null;

                for (const field of schema) {
                    if (usedKeys.has(field.key)) continue;

                    const matchAliases = (field.aliases || []).map(a => a.toLowerCase().replace(/[\s\-_]/g, ''));
                    if (clean === field.key.toLowerCase() || matchAliases.includes(clean)) {
                        matchedField = field;
                        break;
                    }
                }

                if (matchedField) {
                    mapping.push({ colIndex: colIndex, field: matchedField });
                    usedKeys.add(matchedField.key);
                }
            });

            // If matched less than 2 columns, fallback to positional schema mapping
            if (mapping.length < 2) {
                return schema.map((field, idx) => ({ colIndex: idx, field: field }));
            }

            return mapping;
        },

        /**
         * Ensures the Excel Paste Modal exists in DOM
         */
        ensureModal: function () {
            if ($('#erpGridPasteModal').length > 0) return;

            const modalHtml = `
            <div class="modal fade" id="erpGridPasteModal" tabindex="-1" aria-labelledby="erpGridPasteModalLabel" aria-hidden="true" data-bs-backdrop="static">
                <div class="modal-dialog modal-xl modal-dialog-scrollable">
                    <div class="modal-content shadow-lg border-0">
                        <div class="modal-header bg-dark text-white py-2">
                            <h6 class="modal-title fw-bold" id="erpGridPasteModalLabel">
                                <i class="fa-solid fa-file-excel text-success me-2 fs-5"></i>
                                استيراد ولصق بيانات من Excel (Bulk Excel Paste)
                            </h6>
                            <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
                        </div>
                        <div class="modal-body p-3">
                            <!-- Step 1: Raw Paste Input Area -->
                            <div id="erpPasteInputSection">
                                <div class="alert alert-info py-2 px-3 small d-flex align-items-center mb-2">
                                    <i class="fa-solid fa-circle-info me-2 fs-5"></i>
                                    <div>
                                        <strong>تعليمات اللصق:</strong> حدد الخلايا في Excel واضغط <code>Ctrl+C</code>، ثم الصقها في الصندوق أدناه بالضغط على <code>Ctrl+V</code>. 
                                        يدعم النظام التعرف التلقائي على وسائل الدفع (نقدي، شيك، تحويل) وتعبئة أرقام وبنوك الشيكات آلياً.
                                    </div>
                                </div>
                                <div class="mb-2">
                                    <textarea id="erpPasteRawText" class="form-control font-monospace" rows="6" 
                                              placeholder="الصق بيانات Excel هنا (Ctrl+V)...&#10;مثال:&#10;Cheque&#9;12345&#9;10000&#9;2026-10-15&#9;CIB&#10;Cheque&#9;67890&#9;15000&#9;2026-11-01&#9;NBE&#10;Cash&#9;&#9;5000"></textarea>
                                </div>
                                <div class="d-flex justify-content-between align-items-center">
                                    <div class="form-check form-check-inline small">
                                        <input class="form-check-input" type="checkbox" id="erpPasteHasHeader" checked>
                                        <label class="form-check-label" for="erpPasteHasHeader">السطر الأول يحتوي على عناوين الأعمدة (Header Row)</label>
                                    </div>
                                    <button type="button" class="btn btn-primary btn-sm px-4 fw-bold" id="erpPasteProcessBtn">
                                        <i class="fa-solid fa-wand-magic-sparkles me-1"></i> معالجة ومعاينة البيانات
                                    </button>
                                </div>
                            </div>

                            <!-- Step 2: Parsed Table Preview & Validation -->
                            <div id="erpPastePreviewSection" style="display: none;" class="mt-3">
                                <div class="d-flex justify-content-between align-items-center mb-2 p-2 bg-light rounded border">
                                    <div class="d-flex align-items-center gap-2">
                                        <span class="badge bg-primary fs-6" id="erpPasteRowCountBadge">0 سطر</span>
                                        <span class="badge bg-success" id="erpPasteValidCountBadge">0 سليم</span>
                                        <span class="badge bg-danger" id="erpPasteErrorCountBadge">0 به أخطاء</span>
                                    </div>
                                    <div class="d-flex gap-3 align-items-center">
                                        <div class="form-check form-switch small mb-0">
                                            <input class="form-check-input" type="checkbox" id="erpPasteFilterErrorsOnly">
                                            <label class="form-check-label text-danger fw-bold" for="erpPasteFilterErrorsOnly">عرض الأخطاء فقط</label>
                                        </div>
                                        <div class="btn-group btn-group-sm" role="group">
                                            <input type="radio" class="btn-check" name="erpPasteInsertMode" id="erpPasteModeAppend" value="append" checked>
                                            <label class="btn btn-outline-secondary" for="erpPasteModeAppend"><i class="fa-solid fa-plus me-1"></i> إضافة للسطور الحالية</label>

                                            <input type="radio" class="btn-check" name="erpPasteInsertMode" id="erpPasteModeReplace" value="replace">
                                            <label class="btn btn-outline-danger" for="erpPasteModeReplace"><i class="fa-solid fa-arrows-rotate me-1"></i> استبدال السطور الحالية</label>
                                        </div>
                                        <button type="button" class="btn btn-outline-dark btn-sm" id="erpPasteBackBtn">
                                            <i class="fa-solid fa-arrow-right me-1"></i> إعادة اللصق
                                        </button>
                                    </div>
                                </div>

                                <div class="table-responsive border rounded" style="max-height: 420px;">
                                    <table class="table table-sm table-bordered table-hover mb-0 text-center align-middle" id="erpPastePreviewTable" style="font-size: 0.82rem;">
                                        <thead class="table-secondary sticky-top">
                                            <!-- Dynamically injected columns -->
                                        </thead>
                                        <tbody>
                                            <!-- Dynamically injected preview rows -->
                                        </tbody>
                                    </table>
                                </div>
                            </div>
                        </div>
                        <div class="modal-footer py-2 bg-light d-flex justify-content-between">
                            <div>
                                <span class="small text-muted" id="erpPasteFooterHint">يمكنك نسخ جدول بأكمله يحتوي على حتى 500 سطر دفعة واحدة.</span>
                            </div>
                            <div class="d-flex gap-2">
                                <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal">إلغاء</button>
                                <button type="button" class="btn btn-success btn-sm px-4 fw-bold" id="erpPasteCommitBtn" disabled>
                                    <i class="fa-solid fa-check-double me-1"></i> تأكيد وإدراج في السند
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>`;

            $('body').append(modalHtml);
        },

        /**
         * Opens the paste dialog with a specific configuration schema
         */
        open: function (options) {
            this.ensureModal();
            const config = $.extend({
                schema: [],
                branchId: null,
                voucherType: 1, // 1: Receipt, 2: Payment
                permissions: null,
                onCommit: function (parsedRows, mode) { }
            }, options || {});

            const $modal = $('#erpGridPasteModal');
            const $rawText = $('#erpPasteRawText');
            const $inputSec = $('#erpPasteInputSection');
            const $previewSec = $('#erpPastePreviewSection');
            const $commitBtn = $('#erpPasteCommitBtn');
            const $processBtn = $('#erpPasteProcessBtn');
            const $backBtn = $('#erpPasteBackBtn');
            const $table = $('#erpPastePreviewTable');

            // Reset UI State
            $rawText.val('');
            $inputSec.show();
            $previewSec.hide();
            $commitBtn.prop('disabled', true);
            let processedRows = [];

            // Event: Back Button
            $backBtn.off('click').on('click', function () {
                $inputSec.slideDown(200);
                $previewSec.slideUp(200);
                $commitBtn.prop('disabled', true);
            });

            // Event: Process Paste Data
            $processBtn.off('click').on('click', async () => {
                const raw = $rawText.val();
                if (!raw || !raw.trim()) {
                    if (window.Swal) Swal.fire('تنبيه', 'يرجى لصق بيانات من Excel أولاً.', 'warning');
                    else alert('يرجى لصق بيانات من Excel أولاً.');
                    return;
                }

                $processBtn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin me-1"></i> جاري المعالجة...');

                try {
                    const parsedData = await this.processRawData(raw, config);
                    processedRows = parsedData.rows;

                    this.renderPreviewTable($table, config.schema, processedRows);
                    this.updateSummaryBadges(processedRows);

                    $inputSec.slideUp(200);
                    $previewSec.slideDown(200);

                    const hasValidRows = processedRows.some(r => r.isValid);
                    $commitBtn.prop('disabled', !hasValidRows);
                } catch (err) {
                    console.error('Error processing paste data:', err);
                    if (window.Swal) Swal.fire('خطأ', 'فشل في قراءة بيانات الجدول: ' + err.message, 'error');
                } finally {
                    $processBtn.prop('disabled', false).html('<i class="fa-solid fa-wand-magic-sparkles me-1"></i> معالجة ومعاينة البيانات');
                }
            });

            // Event: Commit to Grid
            $commitBtn.off('click').on('click', function () {
                const mode = $('input[name="erpPasteInsertMode"]:checked').val() || 'append';
                const validRows = processedRows.filter(r => r.isValid);

                if (!validRows.length) {
                    if (window.Swal) Swal.fire('تنبيه', 'لا توجد أي سطور صالحة للإدراج.', 'warning');
                    return;
                }

                config.onCommit(validRows, mode);
                const bsModal = bootstrap.Modal.getInstance($modal[0]);
                if (bsModal) bsModal.hide();

                if (window.Swal) {
                    Swal.fire({
                        title: 'تم الإدراج بنجاح!',
                        text: `تم استيراد ${validRows.length} سطر من Excel بنجاح.`,
                        icon: 'success',
                        timer: 2000,
                        showConfirmButton: false
                    });
                }
            });

            // Filter Errors Only
            $('#erpPasteFilterErrorsOnly').off('change').on('change', function () {
                const showOnlyErrors = $(this).is(':checked');
                $table.find('tbody tr').each(function () {
                    const isInvalid = $(this).hasClass('table-danger');
                    if (showOnlyErrors) {
                        $(this).toggle(isInvalid);
                    } else {
                        $(this).show();
                    }
                });
            });

            // Show Modal & focus text area
            const bsModal = new bootstrap.Modal($modal[0]);
            bsModal.show();
            setTimeout(() => { $rawText.focus(); }, 350);
        },

        /**
         * Processes raw TSV into schema-bound and lookup-resolved objects
         */
        processRawData: async function (rawText, config) {
            const rawRows = this.parseTSV(rawText);
            if (!rawRows.length) return { rows: [] };

            const hasHeader = $('#erpPasteHasHeader').is(':checked');
            let dataRows = rawRows;
            let mapping = [];

            if (hasHeader && rawRows.length > 1) {
                const headerRow = rawRows[0];
                mapping = this.detectColumnMapping(headerRow, config.schema);
                dataRows = rawRows.slice(1);
            } else {
                mapping = config.schema.map((f, idx) => ({ colIndex: idx, field: f }));
            }

            // Limit to 500 rows maximum
            dataRows = dataRows.slice(0, 500);

            // Step 1: Collect lookup keys for batch resolution
            const lookupBatches = {
                bank: new Set(),
                customer: new Set(),
                vendor: new Set(),
                account: new Set()
            };

            const preParsed = dataRows.map((cells, rowIdx) => {
                const rowObj = {
                    _rawIndex: rowIdx + 1,
                    paymentMethod: 1,
                    amountForeign: 0,
                    chequeNumber: '',
                    chequeDate: '',
                    dueDate: '',
                    bankKey: '',
                    partyKey: '',
                    accountKey: '',
                    costCenterKey: '',
                    description: '',
                    notes: '',
                    errors: [],
                    isValid: true
                };

                mapping.forEach(m => {
                    const val = cells[m.colIndex] !== undefined ? cells[m.colIndex].trim() : '';
                    if (!val) return;

                    switch (m.field.key) {
                        case 'paymentMethod':
                            rowObj.paymentMethod = ErpGridPaste.parsePaymentMethod(val);
                            break;
                        case 'amount':
                        case 'amountForeign':
                            rowObj.amountForeign = ErpGridPaste.parseNumber(val);
                            break;
                        case 'chequeNumber':
                            rowObj.chequeNumber = val;
                            break;
                        case 'chequeDate':
                            rowObj.chequeDate = ErpGridPaste.parseDate(val);
                            break;
                        case 'dueDate':
                            rowObj.dueDate = ErpGridPaste.parseDate(val);
                            break;
                        case 'bank':
                            rowObj.bankKey = val;
                            lookupBatches.bank.add(val);
                            break;
                        case 'party':
                        case 'customer':
                        case 'vendor':
                            rowObj.partyKey = val;
                            if (config.voucherType === 1) lookupBatches.customer.add(val);
                            else lookupBatches.vendor.add(val);
                            break;
                        case 'account':
                            rowObj.accountKey = val;
                            lookupBatches.account.add(val);
                            break;
                        case 'description':
                            rowObj.description = val;
                            break;
                        case 'notes':
                            rowObj.notes = val;
                            break;
                        case 'drawerName':
                            rowObj.drawerName = val;
                            break;
                    }
                });

                return rowObj;
            });

            // Step 2: Batch Resolve Lookups via API
            const resolvedLookups = {
                bank: {},
                customer: {},
                vendor: {},
                account: {}
            };

            const resolvePromises = [];

            if (lookupBatches.bank.size > 0 && window.ErpLookup && window.ErpLookup.resolveBatch) {
                resolvePromises.push(
                    window.ErpLookup.resolveBatch('bank', Array.from(lookupBatches.bank), config.branchId).then(res => {
                        if (res && res.results) {
                            res.results.forEach(r => { if (r.found && r.result) resolvedLookups.bank[r.key.toLowerCase()] = r.result; });
                        }
                    })
                );
            }

            const partyType = config.voucherType === 1 ? 'customer' : 'vendor';
            const partyKeys = config.voucherType === 1 ? lookupBatches.customer : lookupBatches.vendor;
            if (partyKeys.size > 0 && window.ErpLookup && window.ErpLookup.resolveBatch) {
                resolvePromises.push(
                    window.ErpLookup.resolveBatch(partyType, Array.from(partyKeys), config.branchId).then(res => {
                        if (res && res.results) {
                            res.results.forEach(r => { if (r.found && r.result) resolvedLookups[partyType][r.key.toLowerCase()] = r.result; });
                        }
                    })
                );
            }

            if (lookupBatches.account.size > 0 && window.ErpLookup && window.ErpLookup.resolveBatch) {
                resolvePromises.push(
                    window.ErpLookup.resolveBatch('account', Array.from(lookupBatches.account), config.branchId).then(res => {
                        if (res && res.results) {
                            res.results.forEach(r => { if (r.found && r.result) resolvedLookups.account[r.key.toLowerCase()] = r.result; });
                        }
                    })
                );
            }

            await Promise.all(resolvePromises);

            // Step 3: Validate each row against business rules & permissions
            preParsed.forEach(row => {
                row.errors = [];

                // 1. Amount validation
                if (row.amountForeign <= 0) {
                    row.errors.push('المبلغ يجب أن يكون أكبر من الصفر.');
                }

                // 2. Payment Method permissions
                if (config.permissions) {
                    const methodPerm = config.permissions[row.paymentMethod];
                    if (methodPerm && !methodPerm.canCreate) {
                        row.errors.push(`ليس لديك صلاحية إنشاء لسند بوسيلة الدفع (${this.getMethodName(row.paymentMethod)}).`);
                    }
                }

                // 3. Cheque validations
                if (row.paymentMethod === 2) {
                    if (!row.chequeNumber) {
                        row.errors.push('رقم الشيك إجباري عند اختيار الدفع بشيك.');
                    }
                    if (!row.dueDate) {
                        row.dueDate = new Date().toISOString().split('T')[0];
                    }
                    if (!row.chequeDate) {
                        row.chequeDate = row.dueDate;
                    }

                    // Bank resolution
                    if (row.bankKey) {
                        const bank = resolvedLookups.bank[row.bankKey.toLowerCase()];
                        if (bank) {
                            row.bankId = bank.id;
                            row.bankCode = bank.code;
                            row.bankName = bank.nameAr || bank.nameEn;
                            row.bankDisplayName = `${bank.code} - ${row.bankName}`;
                        } else {
                            row.bankName = row.bankKey;
                            row.bankDisplayName = row.bankKey;
                        }
                    } else {
                        row.errors.push('يرجى تحديد البنك المسحوب عليه الشيك.');
                    }
                } else {
                    // Non-cheque: clean cheque fields
                    row.chequeNumber = '';
                    row.chequeDate = '';
                    row.dueDate = '';
                    row.bankId = null;
                    row.bankCode = '';
                    row.bankName = '';
                    row.bankDisplayName = '';
                    row.drawerName = '';
                }

                // 4. Party resolution
                if (row.partyKey) {
                    const party = resolvedLookups[partyType][row.partyKey.toLowerCase()];
                    if (party) {
                        row.subLedgerId = party.id;
                        row.subLedgerDisplayName = `${party.code} - ${party.nameAr || party.nameEn}`;
                        row.lineType = config.voucherType === 1 ? 1 : 2; // Customer or Vendor

                        // Auto-assign control account if available
                        const ctrlId = (party.extraData && party.extraData.controlAccountId) ? party.extraData.controlAccountId : null;
                        if (ctrlId) {
                            row.accountId = ctrlId;
                        }
                    } else {
                        row.errors.push(`لم يتم العثور على (${config.voucherType === 1 ? 'العميل' : 'المورد'}) بكود/اسم: ${row.partyKey}`);
                    }
                }

                // 5. Account resolution
                if (row.accountKey) {
                    const acc = resolvedLookups.account[row.accountKey.toLowerCase()];
                    if (acc) {
                        row.accountId = acc.id;
                        row.accountCode = acc.code;
                        row.accountName = acc.nameAr || acc.nameEn;
                        row.accountDisplayName = `${acc.code} - ${row.accountName}`;
                    } else if (!row.accountId) {
                        row.errors.push(`لم يتم العثور على الحساب بكود/اسم: ${row.accountKey}`);
                    }
                }

                row.isValid = (row.errors.length === 0);
            });

            return { rows: preParsed };
        },

        /**
         * Renders the preview table in modal
         */
        renderPreviewTable: function ($table, schema, rows) {
            const thead = `
                <tr>
                    <th style="width: 35px;">#</th>
                    <th style="width: 80px;">الحالة</th>
                    <th style="width: 130px;">طريقة الدفع</th>
                    <th style="width: 120px;">المبلغ</th>
                    <th style="width: 110px;">رقم الشيك</th>
                    <th style="width: 110px;">استحقاق الشيك</th>
                    <th style="min-width: 140px;">البنك</th>
                    <th style="min-width: 160px;">الطرف / الحساب</th>
                    <th>ملاحظات الأخطاء</th>
                </tr>`;
            $table.find('thead').html(thead);

            let tbodyHtml = '';
            rows.forEach((r, idx) => {
                const trClass = r.isValid ? '' : 'table-danger';
                const statusBadge = r.isValid 
                    ? '<span class="badge bg-success"><i class="fa-solid fa-check"></i> جاهز</span>'
                    : '<span class="badge bg-danger"><i class="fa-solid fa-triangle-exclamation"></i> خطأ</span>';

                const methodBadge = this.getMethodBadge(r.paymentMethod);
                const bankText = r.bankDisplayName || (r.bankKey ? `<span class="text-danger">${r.bankKey} ⚠️</span>` : '-');
                const partyText = r.subLedgerDisplayName || r.accountDisplayName || (r.partyKey || r.accountKey ? `<span class="text-danger">${r.partyKey || r.accountKey}</span>` : '<span class="text-muted">الافتراضي</span>');
                const errorText = r.errors.length ? `<span class="text-danger fw-bold">${r.errors.join(' | ')}</span>` : '<span class="text-muted">-</span>';

                tbodyHtml += `
                <tr class="${trClass}">
                    <td class="text-muted fw-bold">${idx + 1}</td>
                    <td>${statusBadge}</td>
                    <td>${methodBadge}</td>
                    <td class="fw-bold font-monospace text-end">${Number(r.amountForeign || 0).toLocaleString(undefined, { minimumFractionDigits: 2 })}</td>
                    <td class="font-monospace">${r.chequeNumber || '-'}</td>
                    <td>${r.dueDate || '-'}</td>
                    <td>${bankText}</td>
                    <td>${partyText}</td>
                    <td class="text-start small">${errorText}</td>
                </tr>`;
            });

            $table.find('tbody').html(tbodyHtml);
        },

        updateSummaryBadges: function (rows) {
            const total = rows.length;
            const valids = rows.filter(r => r.isValid).length;
            const errors = total - valids;

            $('#erpPasteRowCountBadge').text(`${total} سطر`);
            $('#erpPasteValidCountBadge').text(`${valids} سليم`);
            $('#erpPasteErrorCountBadge').text(`${errors} به أخطاء`);
        },

        getMethodName: function (method) {
            switch (Number(method)) {
                case 1: return 'نقداً (Cash)';
                case 2: return 'شيك (Cheque)';
                case 3: return 'تحويل بنكي (Bank Transfer)';
                case 4: return 'أخرى (Other)';
                default: return 'نقداً';
            }
        },

        getMethodBadge: function (method) {
            switch (Number(method)) {
                case 1: return '<span class="badge bg-success-subtle text-success border border-success-subtle">💵 نقداً</span>';
                case 2: return '<span class="badge bg-warning-subtle text-dark border border-warning">📝 شيك</span>';
                case 3: return '<span class="badge bg-info-subtle text-info border border-info-subtle">🏦 تحويل</span>';
                case 4: return '<span class="badge bg-secondary-subtle text-secondary border border-secondary-subtle">🔄 أخرى</span>';
                default: return '<span class="badge bg-light text-dark">نقداً</span>';
            }
        },

        /**
         * Copies lines array to clipboard as TSV format
         */
        copyToClipboard: function (lines, schema) {
            if (!lines || !lines.length) return;

            const headers = schema.map(s => s.label || s.key);
            const tsvRows = [headers.join('\t')];

            lines.forEach(line => {
                const row = schema.map(s => {
                    const val = line[s.key] !== undefined ? line[s.key] : '';
                    if (s.key === 'paymentMethod') return ErpGridPaste.getMethodName(val);
                    return String(val).replace(/[\t\r\n]/g, ' ');
                });
                tsvRows.push(row.join('\t'));
            });

            const text = tsvRows.join('\r\n');
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).then(() => {
                    if (window.Swal) Swal.fire({ title: 'تم النسخ!', text: `تم نسخ ${lines.length} سطر إلى الحافظة بصيغة Excel.`, icon: 'success', timer: 1500, showConfirmButton: false });
                });
            } else {
                // Fallback textarea
                const $ta = $('<textarea>').val(text).appendTo('body').select();
                document.execCommand('copy');
                $ta.remove();
                if (window.Swal) Swal.fire({ title: 'تم النسخ!', text: `تم نسخ ${lines.length} سطر إلى الحافظة بصيغة Excel.`, icon: 'success', timer: 1500, showConfirmButton: false });
            }
        }
    };

    window.ErpGridPaste = ErpGridPaste;

})(window, jQuery);
