using System.Linq;
using SlideRule;

namespace EShopOnWeb.Architecture;

public sealed class ArchitectureSpec : IArchitectureSpec
{
    private const string EBookArchitecture =
        "https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures";

    private const string GettingStarted =
        "https://nimblepros.github.io/eShopOnWeb/getting-started-for-beginners.html";

    private const string CoreDependenciesAnswer =
        "https://github.com/dotnet-architecture/eShopOnWeb/issues/581";

    private const string InterfaceNaming =
        "https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-classes-structs-and-interfaces";

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
            .Because("The Application Core is the center of the Clean Architecture this application follows. The other projects depend on it, and it has no dependencies on other application layers.")
            .Citation(EBookArchitecture);

        arch.Rule("layering/core-declares-no-blazor-shared")
            .Migrate(
                from: "ApplicationCore.csproj declares a project reference to BlazorShared that no ApplicationCore source uses. Infrastructure and PublicApi use BlazorShared types through it.",
                to: arch.Projects.Named("ApplicationCore")
                    .Must(project => !project.ProjectReferences.Contains("BlazorShared"),
                        description: "declare no project reference to BlazorShared"))
            .Because("A project reference is a dependency even when no code uses it. This one adds BlazorShared and the Blazor packages it references to the build of every project that references the Application Core. The maintainer's rule for the core is that a package bringing UI concerns, like Microsoft.AspNetCore, does not belong there.")
            .Citation(CoreDependenciesAnswer);

        arch.Rule("layering/blazor-shared-independent")
            .Enforce(blazorShared.MustOnlyReferenceItself())
            .Because("BlazorShared contains the contracts the admin client and the API share, and both the browser and the server build it, so it references no other project.");

        arch.Rule("layering/blazor-admin-client-only")
            .Enforce(blazorAdmin.MustOnlyReference(blazorShared))
            .Because("BlazorAdmin runs in the browser and reaches the server only through PublicApi over HTTP. BlazorShared is the one project it shares with the server.");

        arch.Rule("layering/service-defaults-independent")
            .Enforce(serviceDefaults.MustOnlyReferenceItself())
            .Because("The Aspire service defaults configure telemetry, health checks and service discovery for every host, and have no dependency on the application itself.");

        arch.Rule("layering/infrastructure-not-ui")
            .Enforce(infrastructure.MustNotReference(web, publicApi, blazorAdmin))
            .Because("Infrastructure implements interfaces defined in the Application Core. Both the UI and Infrastructure depend on the Application Core, and Infrastructure has no reason to know a UI exists.")
            .Citation(EBookArchitecture);

        arch.Rule("layering/infrastructure-not-blazor-shared")
            .Migrate(
                from: "The identity seed reads role names from BlazorShared.Authorization.Constants.",
                to: infrastructure.MustNotReference(blazorShared))
            .Because("Infrastructure implements the Application Core's interfaces, and BlazorShared is the admin client's contract with the API. Infrastructure reaches BlazorShared only through the Application Core's project reference to it.")
            .Citation(EBookArchitecture);

        arch.Rule("layering/web-not-public-api")
            .Enforce(web.MustNotReference(publicApi))
            .Because("Web and PublicApi are separate hosts that share the Application Core, and neither calls into the other's code.");

        arch.Rule("layering/public-api-not-web")
            .Enforce(publicApi.MustNotReference(web, blazorAdmin))
            .Because("PublicApi serves the admin client over HTTP. It shares contracts with that client through BlazorShared, and uses neither the Web host's code nor the client's.");

        arch.Rule("layering/web-infrastructure-via-composition-root")
            .Migrate(
                from: "Identity pages, controllers and the basket component name ApplicationUser (and ManageController UserNotFoundException) from Infrastructure.Identity.",
                to: web.Except(webRoot).MustNotReference(infrastructure))
            .Because("The UI project references Infrastructure so that it can wire up dependency injection, and references to Infrastructure types belong in the app's composition root. Most of today's sites name ApplicationUser, the ASP.NET Core Identity user type, which is declared in Infrastructure.Identity.")
            .Citation(EBookArchitecture);

        arch.Rule("layering/public-api-infrastructure-via-composition-root")
            .Migrate(
                from: "User and role management endpoints name ApplicationUser from Infrastructure.Identity; CreateUserEndpoint calls HostEnvironmentExtensions.",
                to: publicApi.Except(apiRoot).MustNotReference(infrastructure))
            .Because("The API references Infrastructure so that it can wire up dependency injection, and references to Infrastructure types belong in the app's composition root. Most of today's sites name ApplicationUser, the ASP.NET Core Identity user type, which is declared in Infrastructure.Identity.")
            .Citation(EBookArchitecture);

        arch.Rule("construction/ui-no-infrastructure-instantiation")
            .Migrate(
                from: "Register and CreateUserEndpoint construct ApplicationUser; ManageController throws a new UserNotFoundException.",
                to: arch.AnyOf(web.Except(webRoot), publicApi.Except(apiRoot)).MustNotConstruct(infrastructure))
            .Because("UI code works with infrastructure through interfaces defined in the Application Core, so it does not instantiate Infrastructure types outside the composition root.")
            .Citation(EBookArchitecture);
    }

    private static void Externals(Arch arch, Layer core, Layer web, Layer publicApi, Selection webRoot, Selection apiRoot)
    {
        arch.Rule("externals/core-no-ef")
            .Enforce(core.MustNotReference(arch.Namespace("Microsoft.EntityFrameworkCore.*")))
            .Because("Entities are stored with EF Core, but EF Core is an implementation detail kept out of the ApplicationCore project. The DbContext and the repositories are in Infrastructure.")
            .Citation(GettingStarted);

        arch.Rule("externals/core-no-aspnetcore")
            .Enforce(core.MustNotReference(arch.Namespace("Microsoft.AspNetCore.*")))
            .Because("ASP.NET Core is a UI concern, and the Application Core has no dependency on the UI.")
            .Citation(CoreDependenciesAnswer);

        arch.Rule("externals/ui-no-ef")
            .Enforce(arch.AnyOf(web.Except(webRoot), publicApi.Except(apiRoot))
                .MustNotReference(arch.Namespace("Microsoft.EntityFrameworkCore.*")))
            .Because("UI types read and write data through the repository and specification interfaces defined in the Application Core. EF Core types belong to Infrastructure and the composition root.")
            .Citation(EBookArchitecture);
    }

    private static void Naming(Arch arch, Layer core, Layer infrastructure, Layer web, Layer publicApi,
        Layer blazorAdmin, Layer blazorShared)
    {
        Selection product = arch.AnyOf(core, infrastructure, web, publicApi, blazorAdmin, blazorShared).Authored();

        arch.Rule("naming/interfaces-prefix-i")
            .Enforce(product.OfKind(TypeKind.Interface).MustHavePrefix("I"))
            .Because("Interface names start with I, the .NET naming convention every interface in the solution follows.")
            .Citation(InterfaceNaming);

        arch.Rule("naming/repository-implements-irepository")
            .Enforce(product.OfKind(TypeKind.Class).WithNameMatching("*Repository*")
                .MustImplement("Microsoft.eShopWeb.ApplicationCore.Interfaces.IRepository<T>"))
            .Because("Data access is abstracted as repository interfaces in the Application Core and implemented in Infrastructure, so a class named as a repository implements IRepository<T>.")
            .Citation(GettingStarted);

        arch.Rule("members/entity-fields-private")
            .Enforce(core.InNamespace("Microsoft.eShopWeb.ApplicationCore.Entities.*").Fields.MustBePrivate())
            .Because("Entities keep their state in private fields and change it through methods, so an aggregate can check its own rules on every change.");
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
            .Because("Migrations are generated history that existing databases have already applied.");
    }
}
