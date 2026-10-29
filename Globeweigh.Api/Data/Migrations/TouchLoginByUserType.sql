-- @manual - deletes role assignments; run it deliberately, not from a deploy.
-- Retires the "Factory User" role (eUserRole.Factory = 4). The Touch login picker used to
-- be role-driven: vwGlobeweighUserFactory joined Globeweigh_Role and filtered RoleId = 4.
-- It is now driven by the user's type instead — eUserType.Touch (2) or Both (3) — which is
-- what the API does directly (AuthController.TouchUsers). The view is repointed at the same
-- rule so the legacy WPF Touch app's login list matches TauriTouch and Globeweigh.Picking.
-- Run once against GbEvergreen.
ALTER VIEW dbo.vwGlobeweighUserFactory
AS
SELECT  u.id,
        u.FirstName + ' ' + u.Surname AS FullName,
        u.Passcode
FROM    dbo.GlobeweighUser u
WHERE   u.Disabled = 0
    AND u.DateDeleted IS NULL
    AND u.UserTypeId IN (2, 3);   -- eUserType.Touch, eUserType.Both
GO

-- Drop the now-meaningless role assignments. Only users who keep at least one other role
-- are touched, so nobody is left role-less (office logins require >= 1 role).
DELETE r
FROM   dbo.Globeweigh_Role r
WHERE  r.RoleId = 4
   AND EXISTS (SELECT 1 FROM dbo.Globeweigh_Role o WHERE o.UserId = r.UserId AND o.RoleId <> 4);
GO
