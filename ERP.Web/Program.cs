using System.Globalization;
using Microsoft.AspNetCore.Localization;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Identity;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Localization services to the container.
builder.Services.AddLocalization();

// Support Arabic and Unicode characters natively in Razor output
builder.Services.AddSingleton<System.Text.Encodings.Web.HtmlEncoder>(
    System.Text.Encodings.Web.HtmlEncoder.Create(
        System.Text.Unicode.UnicodeRanges.BasicLatin,
        System.Text.Unicode.UnicodeRanges.Arabic,
        System.Text.Unicode.UnicodeRanges.All));

builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(ERP.Web.Resources.SharedResource));
    });

// Support AntiForgery Token in AJAX requests via RequestVerificationToken header
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// Configure Request Localization Options (Arabic default, English supported)
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("ar-EG"),
        new CultureInfo("ar"),
        new CultureInfo("en-US"),
        new CultureInfo("en")
    };
    options.DefaultRequestCulture = new RequestCulture("ar-EG", "ar-EG");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.FallBackToParentCultures = true;
    options.FallBackToParentUICultures = true;
    options.RequestCultureProviders.Insert(0, new CookieRequestCultureProvider());
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    
builder.Services.AddDbContext<ErpDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(3),
            errorNumbersToAdd: new[] { -1, 19, 64, 233, 10053, 10054, 10060 });
        sqlOptions.CommandTimeout(30);
    }));

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options => {
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<ErpDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddSingleton<Microsoft.AspNetCore.Authentication.Cookies.ITicketStore, ERP.Web.Services.MemoryCacheTicketStore>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".ERP2026.Auth";
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.IsEssential = true;
});

builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme)
    .Configure<Microsoft.AspNetCore.Authentication.Cookies.ITicketStore>((options, ticketStore) =>
    {
        options.SessionStore = ticketStore;
    });

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromHours(24);
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ERP.Application.Interfaces.ICurrentUserContext, ERP.Infrastructure.Services.CurrentUserContext>();
builder.Services.AddScoped<ERP.Application.Interfaces.ICurrentUserService, ERP.Infrastructure.Services.CurrentUserService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IAuditService, ERP.Infrastructure.Services.AuditService>();
builder.Services.AddScoped<ERP.Application.Interfaces.ICodeSequenceService, ERP.Infrastructure.Services.CodeSequenceService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IJournalEntryValidationService, ERP.Infrastructure.Services.JournalEntryValidationService>();
builder.Services.AddScoped<ERP.Application.Interfaces.ILookupService, ERP.Infrastructure.Services.LookupService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IAccountImportService, ERP.Infrastructure.Services.AccountImportService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IAccountingReportService, ERP.Infrastructure.Services.AccountingReportService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IPaymentMethodPermissionService, ERP.Infrastructure.Services.PaymentMethodPermissionService>();
builder.Services.AddScoped<ERP.Application.Interfaces.ITransactionTypeService, ERP.Infrastructure.Services.TransactionTypeService>();
builder.Services.AddScoped<ERP.Application.Interfaces.ISerialNumberService, ERP.Infrastructure.Services.SerialNumberService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IUniversalDocumentService, ERP.Infrastructure.Services.UniversalDocumentService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IDocumentConversionService, ERP.Infrastructure.Services.DocumentConversionService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IDocumentAdjustmentService, ERP.Infrastructure.Services.DocumentAdjustmentService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IFiscalPeriodService, ERP.Infrastructure.Services.FiscalPeriodService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IInventoryAvailabilityService, ERP.Infrastructure.Services.InventoryAvailabilityService>();
builder.Services.AddScoped<ERP.Application.Interfaces.ISalesReportService, ERP.Infrastructure.Services.SalesReportService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IPurchaseReportService, ERP.Infrastructure.Services.PurchaseReportService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IInventoryReportService, ERP.Infrastructure.Services.InventoryReportService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IPartyGroupService, ERP.Infrastructure.Services.PartyGroupService>();
builder.Services.AddScoped<ERP.Application.Interfaces.ICurrencyRateService, ERP.Infrastructure.Services.CurrencyRateService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IForeignCurrencyRevaluationService, ERP.Infrastructure.Services.ForeignCurrencyRevaluationService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IFixedAssetCategoryService, ERP.Infrastructure.Services.FixedAssetCategoryService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IFixedAssetService, ERP.Infrastructure.Services.FixedAssetService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IAssetDepreciationService, ERP.Infrastructure.Services.AssetDepreciationService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IFixedAssetReportService, ERP.Infrastructure.Services.FixedAssetReportService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await ERP.Infrastructure.Data.SeedData.InitializeAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Request Localization Middleware
var localizationOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(localizationOptions);

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ERP.Web.Middleware.ErpSessionValidationMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
