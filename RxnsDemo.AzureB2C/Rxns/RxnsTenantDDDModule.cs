using System;
using System.Collections.Generic;
using Autofac;
using Rxns.Hosting;

namespace RxnsDemo.AzureB2C.Rxns
{
    public class RxnsTenantDDDModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                    .CreatesOncePerApp<ExecutionContextFactory>()
                    .CreatesOncePerApp<TenantContextFactory>()
                    .CreatesOncePerApp<UserContextFactory>()
                    .CreatesOncePerApp<ExecutionContext>()
                    .CreatesOncePerApp<TenantStorageDiscardRepository>()
                    .CreatesOncePerApp<SqlTenantDatabaseFactory>()
                    .CreatesOncePerApp<LocalRouteInfo>()
                    .CreatesOncePerApp<DefaultEmptyDatabaseConfiguration>()
                    .CreatesOncePerApp<LicenceRepositoryShim>()
                    .CreatesOncePerApp<Func<string, ITenantContext>>(c =>
                    {
                        var cc = c.Resolve<IComponentContext>();

                        var matters = cc.Resolve<ICurrentUsersService>();
                        var dbFactory = cc.Resolve<ITenantDatabaseFactory>();
                        var currentTenants = cc.Resolve<ICurrentTenantsService>();
                        var eventRepo = cc.Resolve<ITenantDiscardRepository>();

                        return tenant => new TenantContext(tenant, dbFactory, matters, currentTenants, eventRepo);
                    })
                ;
        }
    }

    public class DefaultEmptyDatabaseConfiguration : ITenantDatabaseConfiguration
    {
        public string SqlServer { get; set; }
        public string SqlUsername { get; set; }
        public string SqlPassword { get; set; }
        public string DbNameFormat { get; }
    }

    public class TenantLicense
    {
        private readonly string _licenseId;

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantLicense"/> class.
        /// </summary>
        public TenantLicense() : this(Guid.NewGuid().ToString()) { }


        public TenantLicense(string licenseId)
        {
            this._licenseId = licenseId;
        }

        #endregion

        public string LicenseId
        {
            get
            {
                return this._licenseId;
            }
        }

        public string TenantId { get; set; }


        public int MaxDocuments { get; set; }

    }

    public class LicenceRepositoryShim //: ITenantLicenseRepository
    {
        public TenantLicense RetrieveByTenantId(string tenantId)
        {
            return new TenantLicense() { TenantId = tenantId };
        }

        public IEnumerable<TenantLicense> RetrieveAll()
        {
            return new TenantLicense[] { };
        }

        public void Add(TenantLicense tenantLicense)
        {
        }

        public void Update(TenantLicense tenantLicense)
        {
        }

        public void Remove(TenantLicense tenantLicense)
        {
        }

        public void RemoveByTenantId(string tenantId)
        {
        }

        public void RemoveByLicenseId(string licenseId)
        {
        }

        public void CreateOrResetTenantLicense(string tenantId)
        {
        }
    }
}
