using Hangfire.Dashboard;

namespace JustSending.Services
{
    /// No security for hangfire dashboard.
    /// It is protected by cloudflare rules
    public class InsecureAuthFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context) => true;
    }
}