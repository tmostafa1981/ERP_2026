using System;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Web.Authorization
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class HasPermissionAttribute : TypeFilterAttribute
    {
        public string PermissionCode => Arguments?.Length > 0 ? (string)Arguments[0] : string.Empty;

        public HasPermissionAttribute(string permissionCode) : base(typeof(PermissionAuthorizationFilter))
        {
            Arguments = new object[] { permissionCode };
        }
    }
}
