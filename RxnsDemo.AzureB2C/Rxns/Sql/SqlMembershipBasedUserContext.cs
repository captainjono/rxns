using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reactive.Linq;
using Dapper;
using Rxns.Collections;

namespace RxnsDemo.AzureB2C.Rxns
{


    public class LegacyUsers
    {
        public string txtEmailId;
        public string txtUserName { get; set; }
        public string txtGivenName { get; set; }
        public string txtLastName { get; set; }
        public string txtPassword { get; set; }
        public string rowguid { get; set; }
    }

    /// <summary>
    /// This context implements the userdatacontext over the sql member provider interface
    /// from the legacy aspnet imlementing. It hooks directly into the stored procs
    /// </summary>
    public class SqlMembershipBasedUserContext : ITenantUserContext
    {
        private readonly IOrmContext _context;
        private static readonly IExpiringCache<string, object> _userContextCache;
        
        static SqlMembershipBasedUserContext()
        {
            _userContextCache = ExpiringCache.CreateConcurrent<string, object>(TimeSpan.FromMinutes(20));
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context">An ormcontext which is already connected to the appropriote tenant database</param>
        public SqlMembershipBasedUserContext(IOrmContext context)
        {
            _context = context;
        }

        public string[] GetUsers()
        {
            return _context.Run(r =>
            {
                return (string[]) _userContextCache.GetOrLookup("{0}$users".FormatWith(r.Database), 
                                                                _ => r.Query<LegacyUsers>("select * from users").ToArray()).Wait();
            });
        }

        public RxnsRole[] GetRoles()
        {
            return _context.Run(r =>
            {
                return (RxnsRole[])_userContextCache.GetOrLookup("{0}roles".FormatWith(r.Database),
                    _ =>
                    {
                        var rolesWithProps = r.Query<RxnsRole>(@"SELECT	r.[Rolename], r.[RoleId], ra.[IsExternal]
                                                                FROM	dbo.[aspnet_roles] r
                                                                JOIN	dbo.[RoleAttribute] ra	ON r.[RoleId] = ra.[RoleId]");

                        return rolesWithProps.ToArray();
                    }).Wait();
            });
        }

        public Guid GetUserId(string userName)
        {
            return _context.Run(db =>
            {
                Guid userId;
                if (TryGetUserId(userName, out userId))
                    return userId;

                throw new Exception("User '{0}' not found".FormatWith(userName));
            });
        }

        public bool TryGetUserId(string userName, out Guid userId)
        {
            var id = default(Guid);
            var loaded = false;
            _context.Run(db =>
            {
                var results = db.Query<LegacyUsers>("select * rowguid Users where w.UserName == userName").FirstOrDefault();
                if (results != null)
                {
                    id = Guid.Parse(results.rowguid);
                    loaded = true;
                }
            });
            userId = id;
            return loaded;
        }

        public string[] GetRoles(Guid userId)
        {
            return _context.Run(db => GetOrLookupUserRoles(userId, db));
        }

        public string GetName(Guid userId)
        {
            return _context.Run(db =>
            {
                var results = db.Query<LegacyUsers>($"select * rowguid Users where rowguid == {userId}").FirstOrDefault();
                if (results != null) return results.txtGivenName;

                throw new Exception("UserId '{0}' not found".FormatWith(userId));
            });
        }

        public string GetEmail(Guid userId)
        {
            return _context.Run(db =>
            {
                var results = db.Query<LegacyUsers>($"select * fromUsers where rowguid == {userId}").FirstOrDefault();
                if (results != null) return results.txtEmailId;

                throw new Exception("UserId '{0}' not found".FormatWith(userId));
            });
        }

        public Guid RegisterUser(UserCreatedEvent newUser)
        {
            return _context.Run(r =>
            {
                var userId = r.ExecuteScalar("insert into users(colA, colB) values (@a, @b);SELECT last_insert_id();",
                    new[] { new { a = 1, b = 1 }, }
                );

                return Guid.Parse(userId.ToString());
            });
        }

        public string[] GetOrLookupUserRoles(Guid userId, IDbConnection r)
        {
            var roles = (List<string>)_userContextCache.GetOrLookup("{0}$ur${1}".FormatWith(r.Database, userId),
                                                        _ => r.Query<string>(@"SELECT	r.[Rolename]
                                                                                FROM	dbo.[aspnet_users] u
                                                                                JOIN	dbo.[aspnet_usersinroles] ur	ON u.[UserId] = ur.[UserId]
                                                                                JOIN	dbo.[aspnet_roles] r			ON ur.[RoleId] = r.[RoleId]
                                                                                WHERE	u.[UserId] = @userId", new { userId })).Wait();
            return roles != null && roles.Any() ? roles.ToArray() : new string[] {};
        }

        public Guid GetRole(string roleName)
        {
            var role = GetRoles().FirstOrDefault(r => r.Description == roleName);
            if (role == null) throw new RoleNotFoundException(roleName);

            return Guid.Parse(role.RoleId);
        }
    }

    public class RoleNotFoundException : Exception
    {
        public RoleNotFoundException(string roleName)
        {
        }
    }

    public class IsInternal
    {
        public bool Internal { get; set; }
    }
}
