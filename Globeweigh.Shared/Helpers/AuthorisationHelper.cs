using System.Linq;
using Globeweigh.Model;

namespace Globeweigh.Shared.Helpers
{
    public static class AuthorisationHelper
    {
        public static bool CurrentUserHasRole(GlobeweighUser currentUser, eUserRole userRole)
        {
            var match = currentUser.Globeweigh_Role.Any(a => a.RoleId == (int) userRole);
            return match;
        }

        public static bool CurrentUserHasRole(GlobeweighUser currentUser, eUserRole userRole, eUserRole userRole1)
        {
            var match = currentUser.Globeweigh_Role.Any(a => a.RoleId == (int)userRole || a.RoleId == (int)userRole1);
            return match;
        }

        public static bool CurrentUserHasRole(GlobeweighUser currentUser, eUserRole userRole, eUserRole userRole1, eUserRole userRole2)
        {
            var match = currentUser.Globeweigh_Role.Any(a => a.RoleId == (int)userRole || a.RoleId == (int)userRole1 || a.RoleId == (int)userRole2);
            return match;
        }
    }
}
