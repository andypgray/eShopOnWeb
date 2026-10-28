using System.Threading.Tasks;
using SlideRule;

namespace EShopOnWeb.Architecture;

public sealed class ArchitectureSpec : IArchitectureSpec
{
    private const string EBookArchitecture =
        "https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures";

    public void Define(Arch arch)
    {
        Layer core = arch.Layer("ApplicationCore", arch.Project("ApplicationCore"))
            .Purpose("The Application Core holds the business model, which includes entities, services, and interfaces.");
        Layer infrastructure = arch.Layer("Infrastructure", arch.Project("Infrastructure"))
            .Purpose("Infrastructure holds the data access implementations and the services that interact with infrastructure concerns, implementing interfaces defined in the Application Core.");
        Layer web = arch.Layer("Web", arch.Project("Web"))
            .Purpose("Web is the monolithic ASP.NET Core front end: MVC controllers, Razor Pages and the host for the Blazor admin.");
        Layer publicApi = arch.Layer("PublicApi", arch.Project("PublicApi"))
            .Purpose("PublicApi is the web API the Blazor admin talks to, one endpoint class per operation.");
        Layer blazorAdmin = arch.Layer("BlazorAdmin", arch.Project("BlazorAdmin"))
            .Purpose("BlazorAdmin is the Blazor WebAssembly admin client that runs in the browser and calls PublicApi.");
        Layer blazorShared = arch.Layer("BlazorShared", arch.Project("BlazorShared"))
            .Purpose("BlazorShared holds the models and contracts shared between the Blazor admin and the API.");
        Layer serviceDefaults = arch.Layer("ServiceDefaults", arch.Project("eShopWeb.AspireServiceDefaults"))
            .Purpose("ServiceDefaults holds the Aspire service defaults shared by the hosts.");

        // The composition roots: where the eBook allows the UI to name Infrastructure types.
        Selection webRoot = arch.AnyOf(
            web.InNamespace("Microsoft.eShopWeb.Web.Configuration.*"),
            web.Named("Program", "ServiceCollectionExtensions", "WebApplicationExtensions"));
        Selection apiRoot = publicApi.Named("Program", "ServiceCollectionExtensions", "WebApplicationExtensions");

        Layering(arch, core, infrastructure, web, publicApi, blazorAdmin, blazorShared, serviceDefaults, webRoot, apiRoot);
        Externals(arch, core, web, publicApi, webRoot, apiRoot);
        Naming(arch, core, infrastructure, web, publicApi, blazorAdmin, blazorShared);
        Dragons(arch);
    }

    private static void Layering(Arch arch, Layer core, Layer infrastructure, Layer web, Layer publicApi,
        Layer blazorAdmin, Layer blazorShared, Layer serviceDefaults, Selection webRoot, Selection apiRoot)
    {
        arch.Rule("layering/core-independent")
            .Enforce(core.MustOnlyReferenceItself())
            .Because("Candidate: the eBook says the Application Core has no dependencies on other application layers.")
            .Citation(EBookArchitecture);

        arch.Rule("layering/blazor-shared-independent")
            .Enforce(blazorShared.MustOnlyReferenceItself())
            .Because("Candidate: survey shows BlazorShared references no other solution project.");

        arch.Rule("layering/blazor-admin-client-only")
            .Enforce(blazorAdmin.MustOnlyReference(blazorShared))
            .Because("Candidate: the WebAssembly client reaches the server only through PublicApi over HTTP, sharing BlazorShared contracts.");

        arch.Rule("layering/service-defaults-independent")
            .Enforce(serviceDefaults.MustOnlyReferenceItself())
            .Because("Candidate: survey shows the Aspire service defaults reference no other solution project.");

        arch.Rule("layering/infrastructure-not-ui")
            .Enforce(infrastructure.MustNotReference(web, publicApi, blazorAdmin))
            .Because("Candidate: the eBook says infrastructure and implementation details depend on the Application Core, and UI and Infrastructure do not depend on one another.")
            .Citation(EBookArchitecture);

        arch.Rule("layering/infrastructure-not-blazor-shared")
            .Migrate(
                from: "The identity seed reads role names from BlazorShared.Authorization.Constants.",
                to: infrastructure.MustNotReference(blazorShared))
            .Because("Candidate: Infrastructure implements Application Core interfaces; BlazorShared is the admin client's contract.");

        arch.Rule("layering/web-not-public-api")
            .Enforce(web.MustNotReference(publicApi))
            .Because("Candidate: the ArchUnitNET example tests say Web and API should be independent of each other.");

        arch.Rule("layering/public-api-not-web")
            .Enforce(publicApi.MustNotReference(web, blazorAdmin))
            .Because("Candidate: survey shows PublicApi references neither the Web host nor the admin client.");

        arch.Rule("layering/web-not-blazor-admin")
            .Migrate(
                from: "UserController returns the admin client's BlazorAdmin.Authorization.UserInfo and ClaimValue types.",
                to: web.Except(webRoot).MustNotReference(blazorAdmin))
            .Because("Candidate: the ArchUnitNET example tests say BlazorAdmin and Web should be independent of each other; Web's composition root hosts the admin client and is excepted.");

        arch.Rule("layering/web-infrastructure-via-composition-root")
            .Migrate(
                from: "Identity pages, controllers and the basket component name ApplicationUser (and ManageController UserNotFoundException) from Infrastructure.Identity.",
                to: web.Except(webRoot).MustNotReference(infrastructure))
            .Because("Candidate: the eBook says developers should limit actual references to types in the Infrastructure project to the app's composition root.")
            .Citation(EBookArchitecture);

        arch.Rule("layering/public-api-infrastructure-via-composition-root")
            .Migrate(
                from: "User and role management endpoints name ApplicationUser from Infrastructure.Identity; CreateUserEndpoint calls HostEnvironmentExtensions.",
                to: publicApi.Except(apiRoot).MustNotReference(infrastructure))
            .Because("Candidate: the eBook says developers should limit actual references to types in the Infrastructure project to the app's composition root.")
            .Citation(EBookArchitecture);

        arch.Rule("construction/ui-no-infrastructure-instantiation")
            .Migrate(
                from: "Register and CreateUserEndpoint construct ApplicationUser; ManageController throws a new UserNotFoundException.",
                to: arch.AnyOf(web.Except(webRoot), publicApi.Except(apiRoot)).MustNotConstruct(infrastructure))
            .Because("Candidate: the eBook says no direct instantiation of or static calls to the Infrastructure layer types should be allowed in the UI layer.")
            .Citation(EBookArchitecture);

        arch.Rule("captive/singleton-no-scoped")
            .Enforce(arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped)))
            .Because("Candidate: a singleton holding a scoped service (DbContext, repositories) outlives its scope.");
    }

    private static void Externals(Arch arch, Layer core, Layer web, Layer publicApi, Selection webRoot, Selection apiRoot)
    {
        arch.Rule("externals/core-no-ef")
            .Enforce(core.MustNotReference(arch.Namespace("Microsoft.EntityFrameworkCore.*")))
            .Because("Candidate: the eBook places the EF DbContext and data access implementations in Infrastructure.")
            .Citation(EBookArchitecture);

        arch.Rule("externals/core-no-aspnetcore")
            .Enforce(core.MustNotReference(arch.Namespace("Microsoft.AspNetCore.*")))
            .Because("Candidate: the Application Core has no dependencies on the UI.");

        arch.Rule("externals/ui-no-ef")
            .Enforce(arch.AnyOf(web.Except(webRoot), publicApi.Except(apiRoot))
                .MustNotReference(arch.Namespace("Microsoft.EntityFrameworkCore.*")))
            .Because("Candidate: the eBook says the UI layer interacts with infrastructure strictly through interfaces defined in Application Core.")
            .Citation(EBookArchitecture);
    }

    private static void Naming(Arch arch, Layer core, Layer infrastructure, Layer web, Layer publicApi,
        Layer blazorAdmin, Layer blazorShared)
    {
        Selection product = arch.AnyOf(core, infrastructure, web, publicApi, blazorAdmin, blazorShared).Authored();

        arch.Rule("naming/interfaces-prefix-i")
            .Enforce(product.OfKind(TypeKind.Interface).MustHavePrefix("I"))
            .Because("Candidate: every interface in the inventory is named I*.");

        arch.Rule("naming/repository-implements-irepository")
            .Enforce(product.OfKind(TypeKind.Class).WithNameMatching("*Repository*")
                .MustImplement("Microsoft.eShopWeb.ApplicationCore.Interfaces.IRepository<T>"))
            .Because("Candidate: the ArchUnitNET example tests say classes named as Repository should implement IRepository.");


        arch.Rule("members/entity-fields-private")
            .Enforce(core.InNamespace("Microsoft.eShopWeb.ApplicationCore.Entities.*").Fields.MustBePrivate())
            .Because("Candidate: the ArchUnitNET example tests say fields in entities should be private.");

        arch.Rule("async/async-suffix")
            .Migrate(
                from: "A few basket service methods (AddItemToBasket, SetQuantities, CountTotalBasketItems) return Task without the Async suffix.",
                to: arch.AnyOf(core, infrastructure).Authored().Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async"))
            .Because("Candidate: most Task-returning methods in ApplicationCore and Infrastructure are named *Async.");


        arch.Rule("members/no-blocking-wait")
            .Enforce(product.MustNotUse(arch.Member(typeof(Task), nameof(Task.Wait))))
            .Because("Candidate: blocking on a Task in ASP.NET Core or WebAssembly risks thread-pool starvation or deadlock.");
    }

    private static void Dragons(Arch arch)
    {
        arch.Scope("infrastructure/ef-migrations")
            .Caution(arch.AnyOf(
                arch.Namespace("Microsoft.eShopWeb.Infrastructure.Data.Migrations.*"),
                arch.Namespace("Microsoft.eShopWeb.Infrastructure.Identity.Migrations.*")))
            .Dragons("EF Core migrations for CatalogContext and AppIdentityDbContext, generated by `dotnet ef migrations add`. " +
                     "Each migration is history already applied to existing databases: editing or deleting one desynchronises " +
                     "them from the code, and the *.Designer.cs files and *ModelSnapshot.cs are tool output that the next " +
                     "migration diffs against. Change the model, then add a new migration with the commands in the README; " +
                     "never hand-edit an existing one.")
            .Because("Candidate: migrations are generated, applied history.");
    }
}
