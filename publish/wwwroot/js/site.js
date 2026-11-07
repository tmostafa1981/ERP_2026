/**
 * ERP Enterprise UI Standard Scripts
 * 1. Searchable Dynamic Dropdowns (Select2 + Bootstrap 5 + RTL/LTR + AJAX)
 * 2. Friendly Modal Form Validation & Data Preservation Framework
 */

// ─── 1. Select2 Global Initializer ───────────────────────────────────────────
function initErpSelect2(container) {
    var $context = container ? $(container) : $(document);
    var isRtl = $('html').attr('dir') === 'rtl';

    $context.find('select.erp-select2').each(function () {
        var $select = $(this);
        if ($select.hasClass('select2-hidden-accessible')) {
            // Already initialized
            return;
        }

        var $modal = $select.closest('.modal');
        var placeholderText = $select.attr('data-placeholder') || (isRtl ? '-- اختر --' : '-- Select --');
        var allowClear = $select.attr('data-allow-clear') === 'true';
        var lookupEntity = $select.attr('data-lookup-entity');
        var minChars = $select.data('min-chars') !== undefined ? parseInt($select.data('min-chars'), 10) : 0;

        var select2Options = {
            theme: 'bootstrap-5',
            dir: isRtl ? 'rtl' : 'ltr',
            language: isRtl ? 'ar' : 'en',
            width: '100%',
            placeholder: placeholderText,
            allowClear: allowClear,
            dropdownParent: $modal.length ? $modal : $(document.body)
        };

        if (lookupEntity) {
            select2Options.minimumInputLength = minChars;
            select2Options.ajax = {
                url: '/api/lookup/' + lookupEntity,
                dataType: 'json',
                delay: 250,
                cache: false, // Rule 4: Dynamic refresh, no stale cache
                data: function (params) {
                    var query = {
                        q: params.term || '',
                        page: params.page || 1
                    };

                    // Dynamic branch parameter linking
                    var branchSelector = $select.attr('data-branch-selector');
                    if (branchSelector) {
                        var bVal = $(branchSelector).val();
                        if (bVal) query.branchId = bVal;
                    }

                    // Dynamic governorate parameter linking
                    var govSelector = $select.attr('data-gov-selector');
                    if (govSelector) {
                        var gVal = $(govSelector).val();
                        if (gVal) query.governorateId = gVal;
                    }

                    // Dynamic company parameter linking
                    var companySelector = $select.attr('data-company-selector');
                    if (companySelector) {
                        var cVal = $(companySelector).val();
                        if (cVal) query.companyId = cVal;
                    }

                    // Dynamic item group parameter linking
                    var itemGroupSelector = $select.attr('data-itemgroup-selector');
                    if (itemGroupSelector) {
                        var igVal = $(itemGroupSelector).val();
                        if (igVal) query.itemGroupId = igVal;
                    }

                    return query;
                },
                processResults: function (data, params) {
                    params.page = params.page || 1;
                    return {
                        results: data.results || [],
                        pagination: data.pagination || { more: false }
                    };
                }
            };

            // Setup cascading auto-reset when parent selector changes
            var selectUniqueId = $select.attr('id') || ('sel_' + Math.random().toString(36).substring(2, 9));
            if (!$select.attr('id')) {
                $select.attr('id', selectUniqueId);
            }

            var branchSelector = $select.attr('data-branch-selector');
            if (branchSelector) {
                $(document).off('change.erp_cascade_' + selectUniqueId, branchSelector)
                           .on('change.erp_cascade_' + selectUniqueId, branchSelector, function () {
                    $select.val(null).trigger('change');
                });
            }

            var govSelector = $select.attr('data-gov-selector');
            if (govSelector) {
                $(document).off('change.erp_cascade_' + selectUniqueId, govSelector)
                           .on('change.erp_cascade_' + selectUniqueId, govSelector, function () {
                    $select.val(null).trigger('change');
                });
            }

            var companySelector = $select.attr('data-company-selector');
            if (companySelector) {
                $(document).off('change.erp_cascade_' + selectUniqueId, companySelector)
                           .on('change.erp_cascade_' + selectUniqueId, companySelector, function () {
                    $select.val(null).trigger('change');
                });
            }
        }

        $select.select2(select2Options);

        // When Select2 changes, clear any is-invalid state
        $select.on('change.select2', function () {
            $select.removeClass('is-invalid');
            $select.next('.select2-container').find('.select2-selection').removeClass('is-invalid');
            $select.closest('.mb-2, .col-md-3, .col-md-4, .col-md-6, .col-12').find('.invalid-feedback').remove();
        });
    });
}

// ─── 2. Friendly Modal Form Validation Framework ─────────────────────────────
function initErpModalForms() {
    $(document).off('submit', 'form.erp-modal-form').on('submit', 'form.erp-modal-form', function (e) {
        e.preventDefault();
        var form = this;
        var $form = $(form);
        var $modal = $form.closest('.modal');
        var $modalBody = $modal.find('.modal-body');
        var $submitBtn = $form.find('button[type="submit"]');

        // 1. Clear previous errors
        $modalBody.find('.erp-modal-val-summary').remove();
        $form.find('.is-invalid').removeClass('is-invalid');
        $form.find('.invalid-feedback').remove();

        // 2. Set loading state on submit button
        var originalBtnHtml = $submitBtn.html();
        $submitBtn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin me-1 ms-0"></i> جاري الحفظ...');

        var formData = new FormData(form);

        $.ajax({
            url: form.action,
            method: form.method || 'POST',
            data: formData,
            processData: false,
            contentType: false,
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            },
            success: function (res) {
                $submitBtn.prop('disabled', false).html(originalBtnHtml);

                if (res && res.success === false) {
                    // Validation failure: PRESERVE DATA, DO NOT CLOSE MODAL
                    renderValidationErrors($form, $modalBody, res.message, res.errors);
                } else if (res && res.success === true) {
                    // Success: close modal, reload grid / page
                    $modal.modal('hide');
                    if (res.redirectUrl) {
                        window.location.href = res.redirectUrl;
                    } else {
                        window.location.reload();
                    }
                } else {
                    // If HTML was returned unexpectedly, reload
                    window.location.reload();
                }
            },
            error: function (xhr) {
                $submitBtn.prop('disabled', false).html(originalBtnHtml);

                var errorMsg = 'حدث خطأ أثناء معالجة الطلب. يرجى مراجعة البيانات المدخلة والمحاولة مرة أخرى.';
                var fieldErrors = null;

                if (xhr.responseJSON) {
                    if (xhr.responseJSON.message) errorMsg = xhr.responseJSON.message;
                    if (xhr.responseJSON.errors) fieldErrors = xhr.responseJSON.errors;
                }

                renderValidationErrors($form, $modalBody, errorMsg, fieldErrors);
            }
        });
    });
}

function renderValidationErrors($form, $modalBody, mainMessage, errorsObj) {
    var isRtl = $('html').attr('dir') === 'rtl';
    var errorListItems = [];

    if (errorsObj && typeof errorsObj === 'object') {
        $.each(errorsObj, function (key, messages) {
            var msgArray = Array.isArray(messages) ? messages : [messages];
            $.each(msgArray, function (i, msg) {
                errorListItems.push('<li>' + $('<div>').text(msg).html() + '</li>');

                // Highlight the matching field
                var fieldName = key;
                var $field = $form.find('[name="' + fieldName + '"], [name="' + fieldName.charAt(0).toLowerCase() + fieldName.slice(1) + '"]');

                if ($field.length) {
                    $field.addClass('is-invalid');
                    // For Select2 fields
                    if ($field.hasClass('select2-hidden-accessible')) {
                        $field.next('.select2-container').find('.select2-selection').addClass('is-invalid');
                    }

                    // Add invalid-feedback message under field
                    var $parent = $field.closest('.mb-2, .mb-3, .col-md-3, .col-md-4, .col-md-6, .col-12');
                    if (!$parent.find('.invalid-feedback').length) {
                        $parent.append('<div class="invalid-feedback d-block">' + $('<div>').text(msg).html() + '</div>');
                    }
                }
            });
        });
    }

    var summaryHeader = mainMessage || (isRtl ? 'لا يمكن حفظ البيانات. يرجى تصحيح الأخطاء التالية:' : 'Cannot save data. Please correct the following errors:');
    var summaryHtml = '<div class="alert alert-danger p-2 mb-3 erp-modal-val-summary">' +
        '<div class="fw-bold mb-1"><i class="fa-solid fa-triangle-exclamation me-1 ms-0"></i> ' + $('<div>').text(summaryHeader).html() + '</div>';

    if (errorListItems.length > 0) {
        summaryHtml += '<ul>' + errorListItems.join('') + '</ul>';
    }
    summaryHtml += '</div>';

    $modalBody.prepend(summaryHtml);

    // Scroll smoothly to top of modal body
    $modalBody.animate({ scrollTop: 0 }, 200);
}

// ─── 3. Auto Initialize on Document Ready & Modal Show ───────────────────────
$(document).ready(function () {
    initErpSelect2();
    initErpModalForms();

    // Re-initialize select2 when any modal is opened
    $(document).on('shown.bs.modal', '.modal', function () {
        initErpSelect2($(this));
    });
});
