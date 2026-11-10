/**
 * ERP Global Search & Lookup Standard Framework (Dual-Mode: Quick Search & Modal Dialog)
 * Version: 1.0.0
 */

(function ($) {
    'use strict';

    var entityTitles = {
        account: { title: 'دليل الحسابات المالية', icon: 'fa-book-bookmark', codeLabel: 'رقم الحساب', infoLabel: 'المجموعة / النوع' },
        customer: { title: 'دليل العملاء', icon: 'fa-users', codeLabel: 'كود العميل', infoLabel: 'الرقم الضريبي / الهاتف' },
        vendor: { title: 'دليل الموردين', icon: 'fa-truck-field', codeLabel: 'كود المورد', infoLabel: 'الرقم الضريبي / الهاتف' },
        costcenter: { title: 'مراكز التكلفة', icon: 'fa-diagram-project', codeLabel: 'كود المركز', infoLabel: 'التوجيه المالي' },
        item: { title: 'دليل الأصناف', icon: 'fa-boxes-stacked', codeLabel: 'كود الصنف', infoLabel: 'الباركود / المجموعة' },
        warehouse: { title: 'المستودعات', icon: 'fa-warehouse', codeLabel: 'كود المستودع', infoLabel: 'الفرع' },
        salesman: { title: 'مناديب المبيعات', icon: 'fa-user-tie', codeLabel: 'كود المندوب', infoLabel: 'البيانات' },
        branch: { title: 'الفروع', icon: 'fa-code-branch', codeLabel: 'كود الفرع', infoLabel: 'الاسم' },
        currency: { title: 'العملات', icon: 'fa-coins', codeLabel: 'كود العملة', infoLabel: 'معامل الصرف' },
        user: { title: 'دليل المستخدمين', icon: 'fa-user-gear', codeLabel: 'كود الدخول / الموظف', infoLabel: 'البريد / الهاتف' },
        customercategory: { title: 'تصنيفات العملاء', icon: 'fa-tags', codeLabel: 'كود التصنيف', infoLabel: 'الفرع' },
        vendorcategory: { title: 'تصنيفات الموردين', icon: 'fa-tags', codeLabel: 'كود التصنيف', infoLabel: 'الفرع' },
        itemgroup: { title: 'مجموعات وتصنيفات الأصناف', icon: 'fa-layer-group', codeLabel: 'كود المجموعة', infoLabel: 'المجموعة الرئيسية' },
        unit: { title: 'وحدات القياس', icon: 'fa-ruler-combined', codeLabel: 'كود الوحدة', infoLabel: 'الاسم' },
        bank: { title: 'دليل البنوك التجارية', icon: 'fa-building-columns', codeLabel: 'كود البنك', infoLabel: 'كود السويفت / الاسم' }
    };

    var modalHtml = `
    <div class="modal fade" id="erpGlobalLookupModal" tabindex="-1" aria-hidden="true" style="z-index: 1060;">
        <div class="modal-dialog modal-dialog-centered modal-lg modal-dialog-scrollable">
            <div class="modal-content shadow border-0">
                <div class="modal-header bg-primary text-white py-2">
                    <h6 class="modal-title fw-bold d-flex align-items-center" id="erpGlobalLookupModalTitle">
                        <i class="fa-solid fa-magnifying-glass me-2"></i> بحث متقدم
                    </h6>
                    <span class="badge bg-light text-primary ms-3 fw-normal small">
                        <i class="fa-solid fa-computer-mouse me-1"></i>دبل كليك على أي سطر للاختيار السريع
                    </span>
                    <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body p-3">
                    <!-- Search Input Box -->
                    <div class="input-group input-group-sm mb-3">
                        <span class="input-group-text bg-light text-muted"><i class="fa-solid fa-search"></i></span>
                        <input type="text" class="form-control" id="erpGlobalLookupSearchInput" 
                               placeholder="اكتب للبحث (الكود، الاسم بالعربي، الاسم بالإنجليزي، الرقم الضريبي...)" autocomplete="off">
                        <button class="btn btn-outline-secondary" type="button" id="erpGlobalLookupClearBtn" title="تفريغ">
                            <i class="fa-solid fa-xmark"></i>
                        </button>
                        <button class="btn btn-primary px-3" type="button" id="erpGlobalLookupSearchBtn">
                            بحث
                        </button>
                    </div>

                    <!-- Loading Spinner -->
                    <div id="erpGlobalLookupLoading" class="text-center py-4 d-none">
                        <div class="spinner-border text-primary" role="status">
                            <span class="visually-hidden">جاري التحميل...</span>
                        </div>
                        <div class="small text-muted mt-2">جاري البحث في قاعدة البيانات...</div>
                    </div>

                    <!-- Results Table -->
                    <div class="table-responsive border rounded" id="erpGlobalLookupTableWrapper" style="min-height: 240px; max-height: 380px;">
                        <table class="table table-hover table-striped table-sm align-middle mb-0" id="erpGlobalLookupTable">
                            <thead class="table-light sticky-top small">
                                <tr>
                                    <th style="width: 120px;" id="erpGlobalLookupThCode">الكود</th>
                                    <th>الاسم</th>
                                    <th style="min-width: 150px;" id="erpGlobalLookupThInfo">معلومات إضافية</th>
                                    <th style="width: 110px;" class="text-center d-none" id="erpGlobalLookupThBalance">رصيد المخزن</th>
                                    <th style="width: 80px;" class="text-center">اختيار</th>
                                </tr>
                            </thead>
                            <tbody id="erpGlobalLookupTbody" class="small">
                                <!-- Dynamic rows -->
                            </tbody>
                        </table>
                    </div>

                    <!-- No Results Alert -->
                    <div id="erpGlobalLookupEmpty" class="alert alert-warning text-center py-3 my-2 d-none">
                        <i class="fa-solid fa-circle-exclamation me-1"></i> لا توجد نتائج مطابقة لبحثك.
                    </div>
                </div>

                <!-- Footer Pagination -->
                <div class="modal-footer bg-light py-2 d-flex justify-content-between align-items-center">
                    <span class="small text-muted" id="erpGlobalLookupPaginationInfo">
                        عرض 0 - 0 من 0 سجل
                    </span>
                    <nav aria-label="Lookup navigation">
                        <ul class="pagination pagination-sm mb-0" id="erpGlobalLookupPaginationUl">
                            <!-- Dynamic pages -->
                        </ul>
                    </nav>
                </div>
            </div>
        </div>
    </div>`;

    var currentOptions = null;
    var currentPage = 1;
    var pageSize = 15;
    var searchTimer = null;

    function ensureModal() {
        if ($('#erpGlobalLookupModal').length === 0) {
            $('body').append(modalHtml);
            bindModalEvents();
        }
    }

    function bindModalEvents() {
        $('#erpGlobalLookupSearchInput').on('input', function () {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(function () {
                currentPage = 1;
                fetchLookupData();
            }, 350);
        });

        $('#erpGlobalLookupSearchInput').on('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                clearTimeout(searchTimer);
                currentPage = 1;
                fetchLookupData();
            }
        });

        $('#erpGlobalLookupSearchBtn').on('click', function () {
            currentPage = 1;
            fetchLookupData();
        });

        $('#erpGlobalLookupClearBtn').on('click', function () {
            $('#erpGlobalLookupSearchInput').val('').focus();
            currentPage = 1;
            fetchLookupData();
        });

        $(document).on('click', '.erp-lookup-select-row-btn', function () {
            var itemData = $(this).closest('tr').data('item');
            if (itemData && currentOptions && currentOptions.onSelect) {
                currentOptions.onSelect(itemData);
            }
            $('#erpGlobalLookupModal').modal('hide');
        });

        $(document).on('dblclick', '#erpGlobalLookupTbody tr', function () {
            var itemData = $(this).data('item');
            if (itemData && currentOptions && currentOptions.onSelect) {
                currentOptions.onSelect(itemData);
            }
            $('#erpGlobalLookupModal').modal('hide');
        });

        $('#erpGlobalLookupModal').on('show.bs.modal', function () {
            $('.erp-lookup-dropdown').hide();
            // Deactivate focus trap on any underlying open Bootstrap 5 modals so search input can receive focus
            if (window.bootstrap && window.bootstrap.Modal) {
                $('.modal.show').not(this).each(function () {
                    var instance = window.bootstrap.Modal.getInstance(this);
                    if (instance && instance._focustrap) {
                        instance._focustrap.deactivate();
                    }
                });
            }
        });

        $('#erpGlobalLookupModal').on('shown.bs.modal', function () {
            var $backdrops = $('.modal-backdrop');
            if ($backdrops.length > 1) {
                $backdrops.last().css('z-index', 1058);
            }
            $('#erpGlobalLookupSearchInput').trigger('focus');
        });

        $('#erpGlobalLookupModal').on('hidden.bs.modal', function () {
            var $openModals = $('.modal.show').not(this);
            if ($openModals.length > 0) {
                $('body').addClass('modal-open');
                if (window.bootstrap && window.bootstrap.Modal) {
                    var topModal = $openModals.last()[0];
                    var instance = window.bootstrap.Modal.getInstance(topModal);
                    if (instance && instance._focustrap) {
                        instance._focustrap.activate();
                    }
                }
            }
        });
    }

    function fetchLookupData() {
        if (!currentOptions) return;

        var q = $('#erpGlobalLookupSearchInput').val();
        var type = currentOptions.type;
        var params = $.extend({}, currentOptions.filters || {}, {
            type: type,
            q: q,
            page: currentPage,
            pageSize: pageSize
        });

        $('#erpGlobalLookupLoading').removeClass('d-none');
        $('#erpGlobalLookupEmpty').addClass('d-none');
        $('#erpGlobalLookupTableWrapper').addClass('d-none');

        $.ajax({
            url: '/Lookups/Search',
            method: 'GET',
            data: params,
            success: function (res) {
                $('#erpGlobalLookupLoading').addClass('d-none');
                renderResults(res);
            },
            error: function (xhr) {
                $('#erpGlobalLookupLoading').addClass('d-none');
                $('#erpGlobalLookupEmpty').removeClass('d-none').text('حدث خطأ أثناء تحميل البيانات من الخادم.');
            }
        });
    }

    function renderResults(res) {
        var items = res.items || [];
        var total = res.totalCount || 0;
        var page = res.page || 1;
        var size = res.pageSize || pageSize;

        var $tbody = $('#erpGlobalLookupTbody');
        $tbody.empty();

        if (items.length === 0) {
            $('#erpGlobalLookupEmpty').removeClass('d-none');
            $('#erpGlobalLookupTableWrapper').addClass('d-none');
            $('#erpGlobalLookupPaginationInfo').text('عرض 0 من 0');
            $('#erpGlobalLookupPaginationUl').empty();
            return;
        }

        $('#erpGlobalLookupEmpty').addClass('d-none');
        $('#erpGlobalLookupTableWrapper').removeClass('d-none');

        var isItem = currentOptions && currentOptions.type === 'item';
        if (isItem) {
            $('#erpGlobalLookupThBalance').removeClass('d-none');
        } else {
            $('#erpGlobalLookupThBalance').addClass('d-none');
        }

        items.forEach(function (item) {
            var badgeHtml = item.badgeText ? `<span class="badge bg-${item.badgeColor || 'secondary'} ms-1">${item.badgeText}</span>` : '';
            var subTitleHtml = item.subTitle ? `<div class="text-muted" style="font-size:0.75rem;">${item.subTitle}</div>` : '';
            var nameHtml = `<div><strong>${item.nameAr || item.nameEn}</strong> ${badgeHtml}</div>`;
            if (item.nameEn && item.nameEn !== item.nameAr) {
                nameHtml += `<div class="text-muted" style="font-size:0.75rem;">${item.nameEn}</div>`;
            }

            var $tr = $('<tr>')
                .data('item', item)
                .attr('title', 'انقر مرتين للاختيار السريع')
                .css('cursor', 'pointer')
                .css('user-select', 'none')
                .append(`<td class="font-monospace fw-bold text-primary">${item.code}</td>`)
                .append(`<td>${nameHtml}</td>`)
                .append(`<td>${subTitleHtml || '-'}</td>`);

            if (isItem) {
                var bal = (item.balance !== undefined && item.balance !== null) ? item.balance : ((item.extraData && item.extraData.balance !== undefined) ? item.extraData.balance : null);
                if (bal !== null && bal !== undefined) {
                    var bNum = Number(bal);
                    $tr.append(`<td class="text-center font-monospace"><span class="badge ${bNum > 0 ? 'bg-success' : 'bg-danger'} px-2 py-1"><i class="fa-solid fa-boxes-stacked me-1"></i>${bNum.toLocaleString()}</span></td>`);
                } else {
                    $tr.append(`<td class="text-center text-muted small">-</td>`);
                }
            }

            $tr.append(`<td class="text-center"><button type="button" class="btn btn-sm btn-primary py-0 px-2 erp-lookup-select-row-btn">اختيار</button></td>`);

            $tbody.append($tr);
        });

        // Pagination info
        var start = (page - 1) * size + 1;
        var end = Math.min(page * size, total);
        $('#erpGlobalLookupPaginationInfo').text(`عرض ${start} - ${end} من إجمالي ${total.toLocaleString()} سجل`);

        // Render pagination buttons
        renderPagination(total, page, size);
    }

    function renderPagination(total, page, size) {
        var totalPages = Math.ceil(total / size);
        var $ul = $('#erpGlobalLookupPaginationUl');
        $ul.empty();

        if (totalPages <= 1) return;

        // Previous
        var $prev = $(`<li class="page-item ${page <= 1 ? 'disabled' : ''}"><a class="page-link" href="#">&laquo;</a></li>`);
        $prev.find('a').on('click', function (e) {
            e.preventDefault();
            if (page > 1) {
                currentPage = page - 1;
                fetchLookupData();
            }
        });
        $ul.append($prev);

        // Page Numbers (up to 5 around current)
        var startPage = Math.max(1, page - 2);
        var endPage = Math.min(totalPages, page + 2);

        for (var i = startPage; i <= endPage; i++) {
            (function (p) {
                var $pItem = $(`<li class="page-item ${p === page ? 'active' : ''}"><a class="page-link" href="#">${p}</a></li>`);
                $pItem.find('a').on('click', function (e) {
                    e.preventDefault();
                    if (p !== page) {
                        currentPage = p;
                        fetchLookupData();
                    }
                });
                $ul.append($pItem);
            })(i);
        }

        // Next
        var $next = $(`<li class="page-item ${page >= totalPages ? 'disabled' : ''}"><a class="page-link" href="#">&raquo;</a></li>`);
        $next.find('a').on('click', function (e) {
            e.preventDefault();
            if (page < totalPages) {
                currentPage = page + 1;
                fetchLookupData();
            }
        });
        $ul.append($next);
    }

    // Public API
    window.ErpLookup = {
        openModal: function (options) {
            ensureModal();
            currentOptions = options || {};
            currentPage = 1;

            var entityConfig = entityTitles[currentOptions.type] || {
                title: 'دليل السجلات',
                icon: 'fa-magnifying-glass',
                codeLabel: 'الكود',
                infoLabel: 'معلومات إضافية'
            };

            var modalTitle = currentOptions.title || `بحث متقدم - ${entityConfig.title}`;
            $('#erpGlobalLookupModalTitle').html(`<i class="fa-solid ${entityConfig.icon} me-2"></i> ${modalTitle}`);
            $('#erpGlobalLookupThCode').text(entityConfig.codeLabel);
            $('#erpGlobalLookupThInfo').text(entityConfig.infoLabel);
            if (currentOptions.type === 'item') {
                $('#erpGlobalLookupThBalance').removeClass('d-none');
            } else {
                $('#erpGlobalLookupThBalance').addClass('d-none');
            }

            var initialQuery = currentOptions.initialQuery || '';
            $('#erpGlobalLookupSearchInput').val(initialQuery);

            $('#erpGlobalLookupModal').modal('show');

            setTimeout(function () {
                $('#erpGlobalLookupSearchInput').focus();
                fetchLookupData();
            }, 250);
        },

        search: function (type, query, page, pageSize, extraFilters) {
            var params = $.extend({}, extraFilters || {}, {
                type: type,
                q: query,
                page: page || 1,
                pageSize: pageSize || 15
            });

            return $.ajax({
                url: '/Lookups/Search',
                method: 'GET',
                data: params
            });
        },

        getById: function (type, id) {
            return $.ajax({
                url: '/Lookups/GetById',
                method: 'GET',
                data: { type: type, id: id }
            });
        },

        getByCode: function (type, code, branchId) {
            return $.ajax({
                url: '/Lookups/GetByCode',
                method: 'GET',
                data: { type: type, code: code, branchId: branchId }
            });
        },

        resolveBatch: function (type, keys, branchId, extraFilters) {
            var payload = {
                entityType: type,
                keys: keys || [],
                branchId: branchId || null,
                filters: extraFilters || null
            };

            return $.ajax({
                url: '/Lookups/ResolveBatch',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(payload)
            });
        },

        initPickers: function (context) {
            initLookupPickers(context);
        },

        init: function (context) {
            initLookupPickers(context);
        },

        setPickerValue: function (selector, id, displayText) {
            var $el = $(selector);
            var $picker = $el.hasClass('erp-lookup-picker') ? $el : $el.closest('.erp-lookup-picker');
            if (!$picker.length) return;
            var $idInput = $picker.find('.erp-lookup-id');
            var $textInput = $picker.find('.erp-lookup-input');
            var $clearBtn = $picker.find('.erp-lookup-clear-btn');
            var type = $picker.data('type') || 'account';

            if (id) {
                $idInput.val(id);
                if (displayText) {
                    $textInput.val(displayText);
                    $clearBtn.show();
                } else {
                    window.ErpLookup.getById(type, id).done(function (res) {
                        if (res) {
                            var display = res.displayText || (res.code + ' - ' + (res.nameAr || res.nameEn));
                            $textInput.val(display);
                            $clearBtn.show();
                        }
                    });
                }
            } else {
                $idInput.val('');
                $textInput.val('');
                $clearBtn.hide();
            }
        }
    };

    function initLookupPickers(context) {
        var $root = context ? $(context) : $(document);
        $root.find('.erp-lookup-picker').each(function () {
            var $picker = $(this);
            if ($picker.data('erp-picker-initialized')) return;
            $picker.data('erp-picker-initialized', true);

            var type = $picker.data('type') || 'customer';
            var inputName = $picker.data('input-name') || '';
            var inputId = $picker.data('input-id') || '';
            var initialValue = $picker.data('value') !== undefined && $picker.data('value') !== null ? String($picker.data('value')) : '';
            var initialText = $picker.data('text') || '';
            var placeholder = $picker.data('placeholder') || 'ابحث بالكود أو الاسم...';
            var branchSelector = $picker.data('branch-selector') || '';
            var warehouseSelector = $picker.data('warehouse-selector') || '';
            var extraFilters = $picker.data('extra-filters') || {};
            if (typeof extraFilters === 'string') {
                try { extraFilters = JSON.parse(extraFilters); } catch (e) { extraFilters = {}; }
            }

            // Build inner HTML if not already created
            if ($picker.find('.erp-lookup-id').length === 0) {
                var idAttr = inputId ? `id="${inputId}"` : '';
                var html = `
                    <div class="input-group input-group-sm position-relative">
                        <input type="hidden" name="${inputName}" ${idAttr} class="erp-lookup-id" value="${initialValue || ''}">
                        <input type="text" class="form-control form-control-sm erp-lookup-input font-monospace" 
                               placeholder="${placeholder}" autocomplete="off" value="${initialText || ''}">
                        <button type="button" class="btn btn-outline-secondary erp-lookup-clear-btn" title="تفريغ الاختيار" style="${initialValue ? '' : 'display:none;'}">
                            <i class="fa-solid fa-xmark"></i>
                        </button>
                        <button type="button" class="btn btn-outline-primary erp-lookup-search-btn" title="بحث متقدم">
                            <i class="fa-solid fa-magnifying-glass"></i>
                        </button>
                    </div>
                    <div class="erp-lookup-dropdown dropdown-menu shadow-lg p-0 w-100" style="max-height: 260px; overflow-y: auto; display: none; z-index: 1065; position: absolute;">
                        <div class="erp-lookup-dropdown-loading p-2 text-center text-muted small d-none">
                            <i class="fa-solid fa-spinner fa-spin me-1"></i> جارٍ البحث في قاعدة البيانات...
                        </div>
                        <div class="erp-lookup-dropdown-empty p-2 text-center text-muted small d-none">
                            <i class="fa-solid fa-triangle-exclamation text-warning me-1"></i> لا توجد نتائج مطابقة لبحثك
                        </div>
                        <div class="erp-lookup-dropdown-items list-group list-group-flush small"></div>
                    </div>
                `;
                $picker.addClass('position-relative').html(html);
            }

            var $idInput = $picker.find('.erp-lookup-id');
            var $textInput = $picker.find('.erp-lookup-input');
            var $clearBtn = $picker.find('.erp-lookup-clear-btn');
            var $searchBtn = $picker.find('.erp-lookup-search-btn');
            var $dropdown = $picker.find('.erp-lookup-dropdown');
            var $dropdownLoading = $picker.find('.erp-lookup-dropdown-loading');
            var $dropdownEmpty = $picker.find('.erp-lookup-dropdown-empty');
            var $dropdownItems = $picker.find('.erp-lookup-dropdown-items');

            var typeTimer = null;

            // Auto-fetch display name if ID is present but text is empty
            if ($idInput.val() && !$textInput.val()) {
                window.ErpLookup.getById(type, $idInput.val()).done(function (res) {
                    if (res) {
                        var display = res.displayText || (res.code + ' - ' + (res.nameAr || res.nameEn));
                        $textInput.val(display);
                        $clearBtn.show();
                    }
                });
            }

            function getActiveFilters() {
                var f = $.extend({}, extraFilters);
                if (branchSelector && $(branchSelector).length) {
                    var bVal = $(branchSelector).val();
                    if (bVal) f.branchId = bVal;
                }
                if (warehouseSelector && $(warehouseSelector).length) {
                    var wVal = $(warehouseSelector).val();
                    if (wVal) f.warehouseId = wVal;
                }
                return f;
            }

            function selectItem(item) {
                var itemId = item.id !== undefined ? item.id : item.Id;
                var itemCode = item.code !== undefined ? item.code : (item.Code || '');
                var itemName = item.nameAr || item.NameAr || item.nameEn || item.NameEn || '';
                $idInput.val(itemId).attr('value', itemId).trigger('change');
                var display = itemCode ? (itemCode + ' - ' + itemName) : itemName;
                $textInput.val(display);
                $clearBtn.show();
                $dropdown.hide();
                $picker.trigger('erp-lookup:selected', [item]);
            }

            function clearSelection() {
                $idInput.val('').attr('value', '').trigger('change');
                $textInput.val('');
                $clearBtn.hide();
                $dropdown.hide();
                $picker.trigger('erp-lookup:cleared');
            }

            function performQuickSearch(q) {
                $('.erp-lookup-dropdown').not($dropdown).hide();
                $dropdown.show();
                $dropdownLoading.removeClass('d-none');
                $dropdownEmpty.addClass('d-none');
                $dropdownItems.empty();

                clearTimeout(typeTimer);
                typeTimer = setTimeout(function () {
                    var filters = getActiveFilters();
                    window.ErpLookup.search(type, q, 1, 10, filters).done(function (res) {
                        $dropdownLoading.addClass('d-none');
                        var items = res.items || [];
                        if (items.length === 0) {
                            $dropdownEmpty.removeClass('d-none');
                            return;
                        }

                        $dropdownEmpty.addClass('d-none');
                        items.forEach(function (item) {
                            var badge = item.badgeText ? `<span class="badge bg-${item.badgeColor || 'secondary'} ms-1">${item.badgeText}</span>` : '';
                            var stockBadge = '';
                            if (type === 'item') {
                                var bal = (item.balance !== undefined && item.balance !== null) ? item.balance : ((item.extraData && item.extraData.balance !== undefined) ? item.extraData.balance : null);
                                if (bal !== null && bal !== undefined) {
                                    var bNum = Number(bal);
                                    stockBadge = `<span class="badge ${bNum > 0 ? 'bg-success' : 'bg-danger'} font-monospace ms-1"><i class="fa-solid fa-boxes-stacked me-1"></i>رصيد: ${bNum.toLocaleString()}</span>`;
                                }
                            }
                            var sub = item.subTitle ? `<div class="text-muted" style="font-size:0.75rem;">${item.subTitle}</div>` : '';
                            var $a = $(`
                                <a href="#" class="list-group-item list-group-item-action py-1 px-2 border-bottom">
                                    <div class="d-flex justify-content-between align-items-center">
                                        <span><strong class="text-primary font-monospace">${item.code}</strong> - ${item.nameAr || item.nameEn}</span>
                                        <div>
                                            ${stockBadge}
                                            ${badge}
                                        </div>
                                    </div>
                                    ${sub}
                                </a>
                            `);
                            $a.on('click', function (e) {
                                e.preventDefault();
                                selectItem(item);
                            });
                            $dropdownItems.append($a);
                        });
                    }).fail(function () {
                        $dropdownLoading.addClass('d-none');
                        $dropdownEmpty.removeClass('d-none').text('فشل في الاتصال بقاعدة البيانات.');
                    });
                }, 200);
            }

            // Show quick search on focus when no item is selected yet
            $textInput.on('focus', function () {
                if (!$idInput.val()) {
                    performQuickSearch($textInput.val().trim());
                }
            });

            // Live Quick Search on typing
            $textInput.on('input', function () {
                var q = $textInput.val().trim();
                if ($idInput.val()) {
                    $idInput.val('').trigger('change');
                }
                if (!q) {
                    $clearBtn.hide();
                    performQuickSearch('');
                    return;
                }
                $clearBtn.show();
                performQuickSearch(q);
            });

            // Keyboard navigation (ArrowDown, ArrowUp, Enter, Escape)
            $textInput.on('keydown', function (e) {
                var $items = $dropdownItems.find('a');
                if (e.key === 'Escape') {
                    $dropdown.hide();
                } else if (e.key === 'ArrowDown') {
                    if ($dropdown.is(':visible') && $items.length) {
                        e.preventDefault();
                        var $active = $items.filter('.active');
                        if (!$active.length) {
                            $items.first().addClass('active')[0].scrollIntoView({ block: 'nearest' });
                        } else {
                            var $next = $active.removeClass('active').next('a');
                            var $target = $next.length ? $next : $items.first();
                            $target.addClass('active')[0].scrollIntoView({ block: 'nearest' });
                        }
                    }
                } else if (e.key === 'ArrowUp') {
                    if ($dropdown.is(':visible') && $items.length) {
                        e.preventDefault();
                        var $activeUp = $items.filter('.active');
                        if (!$activeUp.length) {
                            $items.last().addClass('active')[0].scrollIntoView({ block: 'nearest' });
                        } else {
                            var $prev = $activeUp.removeClass('active').prev('a');
                            var $targetUp = $prev.length ? $prev : $items.last();
                            $targetUp.addClass('active')[0].scrollIntoView({ block: 'nearest' });
                        }
                    }
                } else if (e.key === 'Enter') {
                    if ($dropdown.is(':visible') && $items.length) {
                        e.preventDefault();
                        var $chosen = $items.filter('.active');
                        if (!$chosen.length) $chosen = $items.first();
                        $chosen.trigger('click');
                    }
                }
            });

            // Modal Search Button
            $searchBtn.on('click', function () {
                var filters = getActiveFilters();
                var queryToPass = $idInput.val() ? '' : $textInput.val().trim();
                window.ErpLookup.openModal({
                    type: type,
                    initialQuery: queryToPass,
                    filters: filters,
                    onSelect: function (item) {
                        selectItem(item);
                    }
                });
            });

            // Clear Button
            $clearBtn.on('click', function () {
                clearSelection();
                $textInput.focus();
            });

            // Close on click outside
            $(document).on('click', function (e) {
                if (!$picker.is(e.target) && $picker.has(e.target).length === 0 && $(e.target).closest('#erpGlobalLookupModal').length === 0) {
                    $dropdown.hide();
                    if (!$idInput.val() && $textInput.val().trim() !== '' && !$('#erpGlobalLookupModal').hasClass('show')) {
                        $textInput.val('');
                        $clearBtn.hide();
                    }
                }
            });
        });
    }

    // Auto-initialize pickers on document ready and when modals open
    $(document).ready(function () {
        initLookupPickers();
        $(document).on('shown.bs.modal', '.modal', function () {
            initLookupPickers(this);
        });
    });

})(jQuery);
