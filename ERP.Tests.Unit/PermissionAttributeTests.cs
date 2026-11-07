using System;
using System.Linq;
using System.Reflection;
using ERP.Web.Authorization;
using ERP.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ERP.Tests.Unit
{
    public class PermissionAttributeTests
    {
        [Theory]
        [InlineData(typeof(CompanyController), nameof(CompanyController.Index), "Organization.Company.View")]
        [InlineData(typeof(CompanyController), nameof(CompanyController.Edit), "Organization.Company.Edit")]
        [InlineData(typeof(BranchController), nameof(BranchController.Index), "Organization.Branch.View")]
        [InlineData(typeof(BranchController), nameof(BranchController.Create), "Organization.Branch.Add")]
        [InlineData(typeof(BranchController), nameof(BranchController.Edit), "Organization.Branch.Edit")]
        [InlineData(typeof(BranchController), nameof(BranchController.ToggleStatus), "Organization.Branch.Edit")]
        [InlineData(typeof(FiscalYearController), nameof(FiscalYearController.Index), "Organization.FiscalYear.View")]
        [InlineData(typeof(FiscalYearController), nameof(FiscalYearController.Create), "Organization.FiscalYear.Add")]
        [InlineData(typeof(FiscalYearController), nameof(FiscalYearController.Edit), "Organization.FiscalYear.Edit")]
        [InlineData(typeof(FiscalYearController), nameof(FiscalYearController.ToggleClose), "Organization.FiscalYear.Close")]
        [InlineData(typeof(UserBranchController), nameof(UserBranchController.Index), "Administration.UserBranch.View")]
        [InlineData(typeof(UserBranchController), nameof(UserBranchController.SaveAssignment), "Administration.UserBranch.Edit")]
        [InlineData(typeof(AuditController), nameof(AuditController.Index), "Administration.AuditHistory.View")]
        [InlineData(typeof(UserController), nameof(UserController.Index), "Administration.Users.View")]
        [InlineData(typeof(UserController), nameof(UserController.Create), "Administration.Users.Add")]
        [InlineData(typeof(UserController), nameof(UserController.Edit), "Administration.Users.Edit")]
        [InlineData(typeof(RoleController), nameof(RoleController.Index), "Administration.Roles.View")]
        [InlineData(typeof(RoleController), nameof(RoleController.Create), "Administration.Roles.Add")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Index), "Cash.ReceiptVoucher.View,Cash.PaymentVoucher.View")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Create), "Cash.ReceiptVoucher.Add,Cash.PaymentVoucher.Add")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Edit), "Cash.ReceiptVoucher.Edit,Cash.PaymentVoucher.Edit")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Details), "Cash.ReceiptVoucher.View,Cash.PaymentVoucher.View")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Post), "Cash.ReceiptVoucher.Post,Cash.PaymentVoucher.Post")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Unpost), "Cash.ReceiptVoucher.Unpost,Cash.PaymentVoucher.Unpost")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Delete), "Cash.ReceiptVoucher.Delete,Cash.PaymentVoucher.Delete")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Cancel), "Cash.ReceiptVoucher.Cancel,Cash.PaymentVoucher.Cancel")]
        [InlineData(typeof(CashBankVouchersController), nameof(CashBankVouchersController.Print), "Cash.ReceiptVoucher.Print,Cash.PaymentVoucher.Print")]
        [InlineData(typeof(ReportsController), nameof(ReportsController.Index), "Reports.GeneralReports.View,Reports.AccountStatement.View,Reports.CustomerStatement.View,Reports.VendorStatement.View,Reports.TrialBalance.View,Reports.GeneralLedger.View")]
        [InlineData(typeof(ReportsController), nameof(ReportsController.AccountStatement), "Reports.AccountStatement.View,Reports.GeneralReports.View")]
        [InlineData(typeof(ReportsController), nameof(ReportsController.CustomerStatement), "Reports.CustomerStatement.View,Reports.GeneralReports.View")]
        [InlineData(typeof(ReportsController), nameof(ReportsController.VendorStatement), "Reports.VendorStatement.View,Reports.GeneralReports.View")]
        [InlineData(typeof(ReportsController), nameof(ReportsController.TrialBalance), "Reports.TrialBalance.View,Reports.GeneralReports.View")]
        [InlineData(typeof(ReportsController), nameof(ReportsController.GeneralLedger), "Reports.GeneralLedger.View,Reports.GeneralReports.View")]
        [InlineData(typeof(ReportsController), nameof(ReportsController.IncomeStatement), "Reports.IncomeStatement.View,Reports.GeneralReports.View")]
        [InlineData(typeof(ReportsController), nameof(ReportsController.BalanceSheet), "Reports.BalanceSheet.View,Reports.GeneralReports.View")]
        public void Action_MustHaveExpectedPermissionAttribute(Type controllerType, string actionName, string expectedPermission)
        {
            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == actionName);

            Assert.NotEmpty(methods);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<HasPermissionAttribute>();
                Assert.NotNull(attr);
                Assert.Equal(expectedPermission, attr.PermissionCode);
            }
        }

        [Theory]
        [InlineData(typeof(CompanyController))]
        [InlineData(typeof(BranchController))]
        [InlineData(typeof(FiscalYearController))]
        [InlineData(typeof(UserBranchController))]
        [InlineData(typeof(AuditController))]
        [InlineData(typeof(UserController))]
        [InlineData(typeof(RoleController))]
        [InlineData(typeof(PermissionController))]
        [InlineData(typeof(CashBankVouchersController))]
        [InlineData(typeof(ReportsController))]
        [InlineData(typeof(SalesReportsController))]
        [InlineData(typeof(PurchaseReportsController))]
        [InlineData(typeof(InventoryReportsController))]
        [InlineData(typeof(HomeController))]
        public void Controller_MustHaveAuthorizeAttribute(Type controllerType)
        {
            var attr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attr);
        }

        [Theory]
        [InlineData(typeof(SalesReportsController), "Reports.SalesReports.View")]
        [InlineData(typeof(PurchaseReportsController), "Reports.PurchaseReports.View")]
        [InlineData(typeof(InventoryReportsController), "Reports.InventoryReports.View")]
        public void ReportControllers_AllActionsMustHavePermissionAttributeWithFallback(Type controllerType, string expectedFallback)
        {
            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => typeof(IActionResult).IsAssignableFrom(m.ReturnType) || typeof(System.Threading.Tasks.Task<IActionResult>).IsAssignableFrom(m.ReturnType));

            Assert.NotEmpty(methods);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<HasPermissionAttribute>();
                Assert.True(attr != null, $"Action {controllerType.Name}.{method.Name} is missing [HasPermission] attribute.");
                Assert.Contains(expectedFallback, attr.PermissionCode);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task PermissionAuthorizationFilter_WhenAjaxAndUnauthorized_Returns403Json()
        {
            var userContextMock = new Moq.Mock<ERP.Application.Interfaces.ICurrentUserContext>();
            userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
            userContextMock.Setup(u => u.CompanyId).Returns(1);
            userContextMock.Setup(u => u.BranchId).Returns(1);
            userContextMock.Setup(u => u.FiscalYearId).Returns(1);
            userContextMock.Setup(u => u.HasPermission("MasterData.Items.Edit")).Returns(false);

            var filter = new PermissionAuthorizationFilter("MasterData.Items.Edit", userContextMock.Object);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";

            var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            var authContext = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(actionContext, new System.Collections.Generic.List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);

            Assert.NotNull(authContext.Result);
            var jsonResult = Assert.IsType<JsonResult>(authContext.Result);
            Assert.Equal(403, jsonResult.StatusCode);
        }

        [Fact]
        public async System.Threading.Tasks.Task PermissionAuthorizationFilter_WhenStandardAndUnauthorized_ReturnsRedirect()
        {
            var userContextMock = new Moq.Mock<ERP.Application.Interfaces.ICurrentUserContext>();
            userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
            userContextMock.Setup(u => u.CompanyId).Returns(1);
            userContextMock.Setup(u => u.BranchId).Returns(1);
            userContextMock.Setup(u => u.FiscalYearId).Returns(1);
            userContextMock.Setup(u => u.HasPermission("MasterData.Items.Edit")).Returns(false);

            var filter = new PermissionAuthorizationFilter("MasterData.Items.Edit", userContextMock.Object);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();

            var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            var authContext = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(actionContext, new System.Collections.Generic.List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);

            Assert.NotNull(authContext.Result);
            var redirectResult = Assert.IsType<RedirectToActionResult>(authContext.Result);
            Assert.Equal("AccessDenied", redirectResult.ActionName);
            Assert.Equal("Account", redirectResult.ControllerName);
        }

        [Fact]
        public async System.Threading.Tasks.Task PermissionAuthorizationFilter_WhenAnyMultiPermissionGranted_AllowsAccess()
        {
            var userContextMock = new Moq.Mock<ERP.Application.Interfaces.ICurrentUserContext>();
            userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
            userContextMock.Setup(u => u.CompanyId).Returns(1);
            userContextMock.Setup(u => u.BranchId).Returns(1);
            userContextMock.Setup(u => u.FiscalYearId).Returns(1);
            userContextMock.Setup(u => u.HasPermission("Cash.ReceiptVoucher.View")).Returns(false);
            userContextMock.Setup(u => u.HasPermission("Cash.PaymentVoucher.View")).Returns(true);

            var filter = new PermissionAuthorizationFilter("Cash.ReceiptVoucher.View,Cash.PaymentVoucher.View", userContextMock.Object);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            var authContext = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(actionContext, new System.Collections.Generic.List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);

            Assert.Null(authContext.Result); // Access granted!
        }

        [Fact]
        public async System.Threading.Tasks.Task PermissionAuthorizationFilter_WhenNoneMultiPermissionGranted_DeniesAccess()
        {
            var userContextMock = new Moq.Mock<ERP.Application.Interfaces.ICurrentUserContext>();
            userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
            userContextMock.Setup(u => u.CompanyId).Returns(1);
            userContextMock.Setup(u => u.BranchId).Returns(1);
            userContextMock.Setup(u => u.FiscalYearId).Returns(1);
            userContextMock.Setup(u => u.HasPermission("Cash.ReceiptVoucher.View")).Returns(false);
            userContextMock.Setup(u => u.HasPermission("Cash.PaymentVoucher.View")).Returns(false);

            var filter = new PermissionAuthorizationFilter("Cash.ReceiptVoucher.View,Cash.PaymentVoucher.View", userContextMock.Object);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            var authContext = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(actionContext, new System.Collections.Generic.List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);

            Assert.NotNull(authContext.Result);
            var redirectResult = Assert.IsType<RedirectToActionResult>(authContext.Result);
            Assert.Equal("AccessDenied", redirectResult.ActionName);
        }

        [Fact]
        public async System.Threading.Tasks.Task PermissionAuthorizationFilter_WhenFineGrainedReportPermissionGranted_AllowsAccess()
        {
            var userContextMock = new Moq.Mock<ERP.Application.Interfaces.ICurrentUserContext>();
            userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
            userContextMock.Setup(u => u.CompanyId).Returns(1);
            userContextMock.Setup(u => u.BranchId).Returns(1);
            userContextMock.Setup(u => u.FiscalYearId).Returns(1);
            // User only has the specific granular report permission, NOT the general module permission
            userContextMock.Setup(u => u.HasPermission("Sales.SalesSummary.View")).Returns(true);
            userContextMock.Setup(u => u.HasPermission("Reports.SalesReports.View")).Returns(false);
            userContextMock.Setup(u => u.HasPermission("Reports.GeneralReports.View")).Returns(false);

            var filter = new PermissionAuthorizationFilter("Sales.SalesSummary.View,Reports.SalesReports.View,Reports.GeneralReports.View", userContextMock.Object);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            var authContext = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(actionContext, new System.Collections.Generic.List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);

            Assert.Null(authContext.Result); // Granted!
        }

        [Fact]
        public async System.Threading.Tasks.Task PermissionAuthorizationFilter_WhenLegacyModulePermissionGranted_AllowsAccess()
        {
            var userContextMock = new Moq.Mock<ERP.Application.Interfaces.ICurrentUserContext>();
            userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
            userContextMock.Setup(u => u.CompanyId).Returns(1);
            userContextMock.Setup(u => u.BranchId).Returns(1);
            userContextMock.Setup(u => u.FiscalYearId).Returns(1);
            // User has legacy module-level report permission, but not the new specific permission
            userContextMock.Setup(u => u.HasPermission("Sales.SalesSummary.View")).Returns(false);
            userContextMock.Setup(u => u.HasPermission("Reports.SalesReports.View")).Returns(true);
            userContextMock.Setup(u => u.HasPermission("Reports.GeneralReports.View")).Returns(false);

            var filter = new PermissionAuthorizationFilter("Sales.SalesSummary.View,Reports.SalesReports.View,Reports.GeneralReports.View", userContextMock.Object);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            var authContext = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(actionContext, new System.Collections.Generic.List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);

            Assert.Null(authContext.Result); // Granted by backward compatibility!
        }

        [Theory]
        [InlineData(typeof(SerialNumbersController), nameof(SerialNumbersController.Inquiry), "Inventory.SerialNumbers.View,Inventory.Items.View")]
        [InlineData(typeof(DocumentAdjustmentsController), nameof(DocumentAdjustmentsController.Index), "Inventory.DocumentAdjustments.View,Sales.SalesInvoice.View,Purchases.PurchaseInvoice.View")]
        [InlineData(typeof(DocumentAdjustmentsController), nameof(DocumentAdjustmentsController.Create), "Inventory.DocumentAdjustments.Add,Sales.SalesInvoice.Edit,Purchases.PurchaseInvoice.Edit")]
        public void NewInventoryScreens_MustHaveExpectedPermissionAttribute(Type controllerType, string actionName, string expectedPermission)
        {
            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == actionName);

            Assert.NotEmpty(methods);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<HasPermissionAttribute>();
                Assert.NotNull(attr);
                Assert.Equal(expectedPermission, attr.PermissionCode);
            }
        }
    }
}
