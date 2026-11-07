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
        unit: { title: 'وحدات القياس', icon: 'fa-ruler-combined', codeLabel: 'كود الوحدة', infoLabel: 'الاسم' }
    };

    var modalHtml = `
    <div class="modal fade" id="erpGlobalLookupModal" tabindex="-1" aria-hidden="true" style="z-index: 1060;">
        <div class="modal-dialog modal-dialog-centered modal-lg modal-dialog-scrollable">
            <div class="modal-content shadow border-0">
                <div class="modal-header bg-primary text-white py-2">
                    <h6 class="modal-title fw-bold" id="erpGlobalLookupModalTitle">
                        <i class="fa-solid fa-magnifying-glass me-2"></i> بحث متقدم
                    </h6>
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

        items.forEach(function (item) {
            var badgeHtml = item.badgeText ? `<span class="badge bg-${item.badgeColor || 'secondary'} ms-1">${item.badgeText}</span>` : '';
            var subTitleHtml = item.subTitle ? `<div class="text-muted" style="font-size:0.75rem;">${item.subTitle}</div>` : '';
            var nameHtml = `<div><strong>${item.nameAr || item.nameEn}</strong> ${badgeHtml}</div>`;
            if (item.nameEn && item.nameEn !== item.nameAr) {
                nameHtml += `<div class="text-muted" style="font-size:0.75rem;">${item.nameEn}</div>`;
            }

            var $tr = $('<tr>')
                .data('item', item)
                .css('cursor', 'pointer')
                .append(`<td class="font-monospace fw-bold text-primary">${item.code}</td>`)
                .append(`<td>${nameHtml}</td>`)
                .append(`<td>${subTitleHtml || '-'}</td>`)
                .append(`<td class="text-center"><button type="button" class="btn btn-sm btn-primary py-0 px-2 erp-lookup-select-row-btn">اختيار</button></td>`);

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
        }
    };

})(jQuery);
