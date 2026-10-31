---
name: sharpvault-clean-architecture
description: SharpVault backend conventions. Use for any server-side work in this repo (domain, CQRS requests and handlers with SwartBerg.Mediator, DTOs, Mapster, pipeline behaviors, minimal API endpoints). Use this instead of dotnet-clean-architecture in this repository.
---

# SharpVault adaptations (these override the general guide below)

The general Clean Architecture guide below is how we work. In SharpVault, these differences apply:

- **Projects are prefixed** `SharpVault.` (`SharpVault.Domain`, `SharpVault.Application`, `SharpVault.Infrastructure`, `SharpVault.Api`). Namespaces follow the project name.
- **No EF Core and no `IApplicationDbContext`.** Persistence is Azure Table Storage. Follow the `sharpvault-table-storage` skill for everything in the Infrastructure Layer section below.
- **Store interfaces replace `DbSet<T>`.** Handlers depend on store interfaces declared in `SharpVault.Application/Common/Interfaces` (for example `ISecretStore`, `IAuditLog`, `ITransferJobStore`). This replaces rule 13: in SharpVault a store abstraction per aggregate is the default because Table Storage has no LINQ provider for projections.
- **Mapping:** there is no `ProjectToType<T>()`. Load entities from the store and map with `.Adapt<T>()`.
- **Current user** comes from `ICurrentUser`, populated from the Entra `oid` claim. See the `sharpvault-entra-auth` skill.
- **Secrets are never mapped into DTOs in plain text** except by the explicit reveal request, which also writes an audit entry.
- **Code style:** follow the `sharpvault-coding-standards` skill. The `// File/Path.cs` lines and explanatory comments in the examples below are there to explain the guide only. Do not write them in real code.

---

This skill guides the creation and maintenance of server-side .NET applications using Clean Architecture. It enforces specific conventions around lean domain models, CQRS with SwartBerg.Mediator, Mapster-based DTO mapping, colocated request/handler files, and minimal APIs with Scalar documentation.

**CRITICAL**: Follow every convention in this skill precisely. These are not suggestions — they are the architectural rules for this codebase.

---

## Solution Structure

```
src/
├── Domain/                           # Entities, Value Objects, Enums, Domain Events, Interfaces
│   ├── Entities/
│   ├── ValueObjects/
│   ├── Enums/
│   ├── Events/
│   ├── Interfaces/                   # Repository interfaces, domain service interfaces
│   └── Common/                       # Base classes (BaseEntity, AuditableEntity, etc.)
│
├── Application/                      # CQRS features, DTOs, Mapster config, Validators, Behaviors
│   ├── Features/                     # Organized by aggregate/domain concept
│   │   ├── Categories/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateCategory.cs         # Request + Handler + Validator in ONE file
│   │   │   │   ├── UpdateCategory.cs
│   │   │   │   └── UploadCategoryMedia.cs
│   │   │   ├── Queries/
│   │   │   │   ├── GetCategoryById.cs
│   │   │   │   ├── GetActiveCategories.cs
│   │   │   │   ├── GetCategoryBySlug.cs
│   │   │   │   └── GetCategoryLookup.cs
│   │   │   └── Dtos/
│   │   │       ├── CategoryDto.cs
│   │   │       ├── CategoryViewModelDto.cs
│   │   │       └── CategoryLookupDto.cs
│   │   └── [OtherFeatures]/
│   │       ├── Commands/
│   │       ├── Queries/
│   │       └── Dtos/
│   ├── Abstractions/                 # Shared abstractions (permissions, roles, etc.)
│   ├── Common/
│   │   ├── Behaviors/                # Pipeline behaviors (validation, logging, caching, etc.)
│   │   ├── Interfaces/               # IApplicationDbContext, ICurrentUser, etc.
│   │   ├── Exceptions/
│   │   └── Models/                   # Pagination, Result<T>, etc.
│   ├── Mapping/
│   │   └── MappingConfig.cs          # Mapster TypeAdapterConfig registration
│   └── DependencyInjection.cs        # Application layer DI registration
│
├── Infrastructure/                   # EF Core, external services, repos
│   ├── Persistence/
│   │   ├── ApplicationDbContext.cs
│   │   ├── Configurations/           # EF entity configurations
│   │   └── Migrations/
│   ├── Services/                     # Email, file storage, etc.
│   └── DependencyInjection.cs        # Infrastructure layer DI registration
│
├── Dtos/                             # Shared DTO project (referenced by Application + Api)
│   ├── Categories/
│   │   ├── CategoryDto.cs
│   │   ├── CategoryViewModelDto.cs
│   │   └── CategoryLookupDto.cs
│   └── Common/
│       ├── FileUploadDto.cs
│       └── PageRequestDto.cs
│
└── Api/                              # Minimal API host
    ├── Endpoints/                    # Minimal API endpoint groups
    ├── Middleware/
    ├── Filters/
    └── Program.cs
```

### Key Structural Rules

- **Each layer has its own `DependencyInjection.cs`** — the Api's `Program.cs` calls `AddApplication()` and `AddInfrastructure()`.
- **No controllers** — use minimal APIs exclusively.
- **Scalar for API docs** — never Swagger/Swashbuckle.
- **DTOs may live in a separate shared project** (e.g., `Dtos/`) when they are referenced by multiple layers or by a Blazor frontend. Otherwise they live in `Application/Features/{Feature}/Dtos/`.

---

## The Golden Rule: Lean Domain Models

Domain entities are **data-centric POCOs** by default. They hold properties and relationships. They do NOT contain business logic unless one of the three exceptions applies.

### Default Entity — No Methods

Most entities look like this:

```csharp
namespace Domain.Entities;

public class Client : AuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public Guid? AssignedProviderId { get; set; }

    // Navigation properties
    public ServiceProvider? AssignedProvider { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = [];
}
```

That's it. No methods. No factories. No private setters by default. The entity is a clean data structure.

### When to Add Domain Methods — THE THREE EXCEPTIONS

Domain methods are the **exception, not the rule**. Only add behavior directly to an entity when:

#### 1. Shared State Transitions
The same status/state change happens from multiple handlers and must enforce the same invariants everywhere.

```csharp
namespace Domain.Entities;

public class ServiceProvider : AuditableEntity
{
    public Guid Id { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public ProviderStatus Status { get; set; } = ProviderStatus.PendingReview;
    public string? SuspensionReason { get; set; }

    // Navigation
    public ICollection<ServiceOffering> Offerings { get; set; } = [];

    // --- DOMAIN METHODS: State transitions called from multiple handlers ---

    public void Approve()
    {
        if (Status is not ProviderStatus.PendingReview)
            throw new InvalidOperationException(
                $"Cannot approve a provider with status '{Status}'. Must be PendingReview.");

        Status = ProviderStatus.Active;
        AddDomainEvent(new ServiceProviderApprovedEvent(Id));
    }

    public void Suspend(string reason)
    {
        if (Status is ProviderStatus.Suspended)
            throw new InvalidOperationException("Provider is already suspended.");

        Status = ProviderStatus.Suspended;
        SuspensionReason = reason;
        AddDomainEvent(new ServiceProviderSuspendedEvent(Id, reason));
    }
}
```

#### 2. Invariant Protection
A property should never be set to an invalid value regardless of which handler is calling.

#### 3. Domain Events Tied to State Changes
When a state transition must always raise a specific domain event, put the `AddDomainEvent()` call inside the method so it can't be forgotten.

**If a piece of logic only happens in one handler and doesn't need invariant protection, it belongs in the handler — NOT on the entity.**

### Base Entity Classes

```csharp
namespace Domain.Common;

public abstract class BaseEntity
{
    private readonly List<BaseEvent> _domainEvents = [];

    public IReadOnlyCollection<BaseEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(BaseEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void RemoveDomainEvent(BaseEvent domainEvent) => _domainEvents.Remove(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? LastModifiedBy { get; set; }
}

public abstract class BaseEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

---

## CQRS with SwartBerg.Mediator

Use `SwartBerg.Mediator` (NuGet package name) with namespace `Mediator`. The interfaces mirror MediatR:

- `IRequest<TResponse>` — marker for a request
- `IRequestHandler<TRequest, TResponse>` — handler
- `IPipelineBehavior<TRequest, TResponse>` — pipeline behaviors
- `INotification` / `INotificationHandler<T>` — for domain event dispatching

### SwartBerg.Mediator Registration

The mediator registers by scanning assemblies. It also supports notification persistence with background workers:

```csharp
services.AddMediator(assemblies, options =>
{
    options.NotificationWorkerCount = Environment.ProcessorCount;
    options.ChannelCapacity = 1000;
    options.EnablePersistence = true;
    options.ProcessingInterval = TimeSpan.FromMinutes(1);
    options.ProcessingBatchSize = 100;
    options.MaxRetryAttempts = 3;
    options.InitialRetryDelay = TimeSpan.FromMinutes(1);
    options.RetryDelayMultiplier = 2.0;
    options.CleanupRetentionPeriod = TimeSpan.FromDays(7);
    options.CleanupInterval = TimeSpan.FromHours(6);
    options.UseConfigureAwaitGlobally = true;  // default: true, optimal for API apps
});
```

The `AddMediator()` call takes an array of assemblies to scan for handlers and notification handlers. Pass all assemblies that contain `IRequestHandler<,>` or `INotificationHandler<>` implementations.

### Colocated Request + Handler + Validator Pattern

**CRITICAL**: The Request, Handler, and Validator for a single feature live in the **same file**. This is non-negotiable. It makes features discoverable — a developer opens one file and sees everything about that operation.

#### Command Example

```csharp
// Features/Categories/Commands/CreateCategory.cs

using FluentValidation;
using Mapster;
using Mediator;
using Application.Common.Interfaces;
using Dtos.Categories;
using Domain.Entities;

namespace Application.Features.Categories.Commands;

// --- Request ---
public record CreateCategoryCommand(
    string Name,
    string? Description,
    string? Slug
) : IRequest<Result<CategoryDto>>;

// --- Validator ---
public class CreateCategoryCommandValidator
    : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator(IApplicationDbContext context)
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Slug)
            .MaximumLength(200)
            .When(x => x.Slug is not null);

        RuleFor(x => x.Name)
            .MustAsync(async (name, ct) =>
                !await context.Categories
                    .AnyAsync(c => c.Name == name, ct))
            .WithMessage("A category with this name already exists.");
    }
}

// --- Handler ---
public class CreateCategoryCommandHandler
    : IRequestHandler<CreateCategoryCommand, Result<CategoryDto>>
{
    private readonly IApplicationDbContext _context;

    public CreateCategoryCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CategoryDto>> Handle(
        CreateCategoryCommand request,
        CancellationToken cancellationToken)
    {
        var entity = new Category
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Slug = request.Slug ?? request.Name.ToSlug()
        };

        _context.Categories.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<CategoryDto>.Success(entity.Adapt<CategoryDto>());
    }
}
```

#### Query Example

```csharp
// Features/Categories/Queries/GetCategoryBySlug.cs

using Mapster;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Application.Common.Interfaces;
using Dtos.Categories;

namespace Application.Features.Categories.Queries;

// --- Request ---
public record CategoryGetBySlugQuery(string Slug) : IRequest<Result<CategoryViewModelDto>>;

// --- Handler ---
public class CategoryGetBySlugQueryHandler
    : IRequestHandler<CategoryGetBySlugQuery, Result<CategoryViewModelDto>>
{
    private readonly IApplicationDbContext _context;

    public CategoryGetBySlugQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CategoryViewModelDto>> Handle(
        CategoryGetBySlugQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == request.Slug, cancellationToken);

        if (entity is null)
            return Result<CategoryViewModelDto>.Failure("Category not found.");

        return Result<CategoryViewModelDto>.Success(entity.Adapt<CategoryViewModelDto>());
    }
}
```

#### Query with Pagination Example

```csharp
// Features/Categories/Queries/GetCategories.cs

using Mapster;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Application.Common.Interfaces;
using Application.Common.Models;
using Dtos.Categories;
using Dtos.Common;

namespace Application.Features.Categories.Queries;

// --- Request ---
public record GetCategoriesQuery(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<Result<PaginatedList<CategoryDto>>>;

// --- Handler ---
public class GetCategoriesQueryHandler
    : IRequestHandler<GetCategoriesQuery, Result<PaginatedList<CategoryDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetCategoriesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PaginatedList<CategoryDto>>> Handle(
        GetCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Categories.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            query = query.Where(x =>
                x.Name.Contains(request.Search) ||
                x.Slug.Contains(request.Search));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(x => x.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ProjectToType<CategoryDto>()  // Mapster SQL projection
            .ToListAsync(cancellationToken);

        return Result<PaginatedList<CategoryDto>>.Success(
            new PaginatedList<CategoryDto>(items, totalCount, request.Page, request.PageSize));
    }
}
```

---

## DTOs

DTOs are simple records. They can live in `Application/Features/{Feature}/Dtos/` or in a separate shared `Dtos/` project when referenced by multiple layers (e.g., Blazor frontend and API):

```csharp
// Dtos/Categories/CategoryDto.cs
namespace Dtos.Categories;

public record CategoryDto(
    Guid Id,
    string Name,
    string? Description,
    string Slug,
    bool IsActive,
    DateTime CreatedAt
);

public record CategoryViewModelDto(
    Guid Id,
    string Name,
    string? Description,
    string Slug,
    string? ImageUrl
);

public record CategoryLookupDto(
    Guid Id,
    string Name
);
```

```csharp
// Dtos/Common/PageRequestDto.cs
namespace Dtos.Common;

public record CategoryPageRequestDto(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20
);

public record FileUploadDto(
    IFormFile File,
    string? Folder = null
);
```

---

## Mapster Configuration

Configure Mapster with `IRegister` profile classes, scanned at startup — similar to AutoMapper profiles:

```csharp
// Mapping/MappingConfig.cs

using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Application.Mapping;

public static class MappingConfig
{
    public static IServiceCollection AddApplicationMappings(this IServiceCollection services)
    {
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(Assembly.GetExecutingAssembly());

        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
```

```csharp
// Mapping/CategoryMappingProfile.cs

using Mapster;
using Domain.Entities;
using Dtos.Categories;

namespace Application.Mapping;

public class CategoryMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Category, CategoryDto>()
            .Map(dest => dest.IsActive, src => src.IsActive);

        config.NewConfig<Category, CategoryViewModelDto>()
            .Map(dest => dest.ImageUrl, src => src.Media != null ? src.Media.Url : null);

        config.NewConfig<Category, CategoryLookupDto>();
    }
}
```

### Mapster Usage Patterns

```csharp
// In command handlers — map in-memory entity to DTO
var dto = entity.Adapt<CategoryDto>();

// In query handlers — SQL projection (generates efficient SELECT)
var dtos = await query.ProjectToType<CategoryLookupDto>().ToListAsync(ct);

// If injecting IMapper (for testability)
var dto = _mapper.Map<CategoryDto>(entity);
```

**Prefer `ProjectToType<T>()` for queries** — it generates efficient SQL projections.
**Prefer `.Adapt<T>()` for commands** — map the in-memory entity after save.

---

## Pipeline Behaviors

Cross-cutting concerns are handled via pipeline behaviors, registered in the Application layer's `DependencyInjection.cs`:

### Validation Behavior

```csharp
// Common/Behaviors/ValidationBehavior.cs

using FluentValidation;
using Mediator;

namespace Application.Common.Behaviors;

public class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

### Performance Logging Behavior

```csharp
// Common/Behaviors/PerformanceLoggingBehavior.cs

using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Application.Common.Behaviors;

public class PerformanceLoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceLoggingBehavior<TRequest, TResponse>> _logger;

    public PerformanceLoggingBehavior(
        ILogger<PerformanceLoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        var response = await next();

        sw.Stop();
        var elapsed = sw.ElapsedMilliseconds;

        if (elapsed > 500)
        {
            _logger.LogWarning(
                "Long running request: {RequestName} ({ElapsedMs}ms)",
                requestName, elapsed);
        }

        return response;
    }
}
```

### Cache Behavior (Pattern)

```csharp
// Common/Behaviors/CacheBehavior.cs

using Mediator;
using LazyCache;

namespace Application.Common.Behaviors;

public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan? Expiration { get; }
}

public class CacheBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAppCache _cache;

    public CacheBehavior(IAppCache cache)
    {
        _cache = cache;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next();

        return await _cache.GetOrAddAsync(
            cacheable.CacheKey,
            async () => await next(),
            cacheable.Expiration ?? TimeSpan.FromMinutes(5));
    }
}
```

### Cache Invalidation Behavior (Pattern)

```csharp
// Common/Behaviors/CacheInvalidationBehavior.cs

using Mediator;
using LazyCache;

namespace Application.Common.Behaviors;

public interface ICacheInvalidator
{
    string[] CacheKeysToInvalidate { get; }
}

public class CacheInvalidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAppCache _cache;

    public CacheInvalidationBehavior(IAppCache cache)
    {
        _cache = cache;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (request is ICacheInvalidator invalidator)
        {
            foreach (var key in invalidator.CacheKeysToInvalidate)
            {
                _cache.Remove(key);
            }
        }

        return response;
    }
}
```

---

## Application Layer DependencyInjection

Each layer registers its own services. The Application layer scans for validators, registers the mediator, Mapster mappings, and pipeline behaviors:

```csharp
// Application/DependencyInjection.cs

using System.Reflection;
using FluentValidation;
using Mediator;
using Application.Common.Behaviors;
using Application.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // FluentValidation — scan for all validators
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        // SwartBerg.Mediator — scan assemblies for handlers
        var mediatorAssemblies = new[]
        {
            Assembly.GetExecutingAssembly(),
            // Add other assemblies containing handlers/notification handlers as needed
        }.Distinct().ToArray();

        services.AddMediator(mediatorAssemblies);

        // Mapster mappings
        services.AddApplicationMappings();

        // Pipeline behaviors (order matters — executed in registration order)
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ExceptionHandlingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceLoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        // LazyCache
        services.AddLazyCache();

        return services;
    }
}
```

---

## Infrastructure Layer

### IApplicationDbContext

```csharp
// Application/Common/Interfaces/IApplicationDbContext.cs

using Microsoft.EntityFrameworkCore;
using Domain.Entities;

namespace Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<ServiceProvider> ServiceProviders { get; }
    DbSet<Client> Clients { get; }
    // ... other DbSets

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

### ApplicationDbContext

```csharp
// Infrastructure/Persistence/ApplicationDbContext.cs

using Microsoft.EntityFrameworkCore;
using Application.Common.Interfaces;
using Domain.Common;
using Domain.Entities;

namespace Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUser? _currentUser;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUser? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ServiceProvider> ServiceProviders => Set<ServiceProvider>();
    public DbSet<Client> Clients => Set<Client>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(builder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Stamp audit fields
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedBy = _currentUser?.UserId;
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.LastModifiedBy = _currentUser?.UserId;
                    entry.Entity.LastModifiedAt = DateTime.UtcNow;
                    break;
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);
        await DispatchDomainEventsAsync(cancellationToken);
        return result;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        var entities = ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var events = entities.SelectMany(e => e.DomainEvents).ToList();
        entities.ForEach(e => e.ClearDomainEvents());

        foreach (var domainEvent in events)
        {
            // Dispatch via your notification mechanism
        }
    }
}
```

### Entity Configuration Example

```csharp
// Infrastructure/Persistence/Configurations/CategoryConfiguration.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Entities;

namespace Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(x => x.Slug)
            .IsUnique();

        builder.Ignore(x => x.DomainEvents);
    }
}
```

### Infrastructure DependencyInjection

```csharp
// Infrastructure/DependencyInjection.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Application.Common.Interfaces;
using Infrastructure.Persistence;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());

        // Register infrastructure services (email, storage, etc.)

        return services;
    }
}
```

---

## API Layer — Minimal APIs with Scalar

### Endpoint Pattern

Endpoints are static classes with a `Map{Feature}Endpoints` extension method. They are thin — receive the request, send through mediator, return the result:

```csharp
// Api/Endpoints/CategoryEndpoints.cs

using Application.Abstractions;
using Application.Features.Categories.Queries;
using Application.Features.Categories.Commands;
using Dtos;
using Dtos.Categories;
using Dtos.Common;
using Mediator;

namespace Api.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/Category")
            .RequireAuthorization(policy =>
                policy.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Admin)))
            .WithTags("Categories");

        group.MapGet("/GetPublicCategories", GetPublicCategories)
            .AllowAnonymous()
            .WithName("Category_GetPublicCategories")
            .WithSummary("Get public active categories.")
            .WithDescription("Get all active and non-deleted categories for public display");

        group.MapGet("/GetBySlug/{slug}", GetCategoryBySlug)
            .AllowAnonymous()
            .WithName("Category_GetBySlug")
            .WithSummary("Get category by slug.")
            .WithDescription("Retrieve a single category by its slug for page display");

        group.MapGet("/Lookup", GetLookup)
            .RequireAuthorization(policy =>
                policy.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Admin)))
            .WithName("Category_GetLookup")
            .WithSummary("Get lookup categories")
            .WithDescription("Get active categories for lookup dropdowns");

        group.MapPost("/UploadMedia", UploadCategoryMedia)
            .RequireAuthorization(policy =>
                policy.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Admin)))
            .WithName("Category_UploadMedia")
            .WithSummary("Upload category media")
            .WithDescription("Upload category images or icons and return a file URL");
    }

    private static async Task<Result<List<CategoryViewModelDto>>> GetPublicCategories(
        IMediator mediator, CancellationToken token = default)
    {
        var query = new CategoryGetActiveQuery();
        return await mediator.Send(query, token);
    }

    private static async Task<Result<CategoryViewModelDto>> GetCategoryBySlug(
        string slug, IMediator mediator, CancellationToken token = default)
    {
        var query = new CategoryGetBySlugQuery(slug);
        return await mediator.Send(query, token);
    }

    private static async Task<Result<List<CategoryLookupDto>>> GetLookup(
        IMediator mediator, CancellationToken token = default)
    {
        var query = new CategoryLookupQuery();
        return await mediator.Send(query, token);
    }

    private static async Task<Result<string>> UploadCategoryMedia(
        FileUploadDto request, IMediator mediator, CancellationToken token = default)
    {
        var command = new CategoryUploadMediaCommand(request);
        return await mediator.Send(command, token);
    }
}
```

### Endpoint Conventions

- **Group by feature** — one static class per aggregate/feature.
- **Method naming**: `Map{Feature}Endpoints` for the extension method.
- **Route naming**: `WithName("{Feature}_{Operation}")` for Scalar documentation.
- **Always include** `.WithSummary()` and `.WithDescription()` for Scalar docs.
- **WithTags** to group endpoints in Scalar UI.
- **Authorization** at group level with per-endpoint overrides (`.AllowAnonymous()`, additional `.RequireAuthorization()`).
- **Handler delegates are private static methods** at the bottom of the class.
- **Handlers receive `IMediator` and `CancellationToken`** via parameter injection.
- **Return `Result<T>`** — the mediator pipeline handles errors and wraps responses.

### CRUD Endpoint Helper (When Available)

If the project has a `MapCrudEndpoints` extension for standard CRUD operations, use it for the base group and add custom endpoints on top:

```csharp
var group = app.MapCrudEndpoints<CategoryDto, CategoryPageRequestDto>(
    "/Category",
    nameof(Permissions.Categories),
    builder => builder.MapGroup("/Category")
        .RequireAuthorization(policy =>
            policy.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Admin)))
        .WithTags("Categories"));

// Then add custom endpoints to the group
group.MapGet("/GetPublicCategories", GetPublicCategories)
    .AllowAnonymous()
    // ...
```

---

## Program.cs — Scalar, Not Swagger

```csharp
// Api/Program.cs

using Application;
using Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Layer DI registration ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- API ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();    // Microsoft OpenAPI support for Scalar

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();   // Scalar UI at /scalar/v1
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// --- Map all endpoint groups ---
app.MapCategoryEndpoints();
// app.Map{OtherFeature}Endpoints();

app.Run();
```

**NEVER use Swagger/Swashbuckle.** Always use Scalar with `MapScalarApiReference()`.

---

## Common Supporting Types

### Result Pattern

```csharp
// Application/Common/Models/Result.cs

namespace Application.Common.Models;

public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public List<string> Errors { get; } = [];

    private Result(T value) { IsSuccess = true; Value = value; }
    private Result(string error) { IsSuccess = false; Error = error; Errors = [error]; }
    private Result(List<string> errors) { IsSuccess = false; Error = errors.FirstOrDefault(); Errors = errors; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error) => new(error);
    public static Result<T> Failure(List<string> errors) => new(errors);
}
```

### PaginatedList

```csharp
// Application/Common/Models/PaginatedList.cs

namespace Application.Common.Models;

public class PaginatedList<T>
{
    public List<T> Items { get; }
    public int TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public PaginatedList(List<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
}
```

---

## Rules & Conventions Summary

1. **Domain entities are lean POCOs** — no methods unless shared state transitions, invariant protection, or domain events require it.
2. **All business logic lives in handlers** — handlers are the workhorse, not entities.
3. **Request + Handler + Validator in ONE file** — named after the operation (e.g., `CreateCategory.cs`).
4. **Mapster for all mapping** — use `.Adapt<T>()` for commands, `ProjectToType<T>()` for queries. Configure via `IRegister` classes scanned at startup.
5. **SwartBerg.Mediator** — namespace is `Mediator`, register via `services.AddMediator(assemblies)`, interfaces are `IRequest<T>` / `IRequestHandler<TRequest, TResponse>`.
6. **Pipeline behaviors** for cross-cutting concerns — validation, performance logging, caching, cache invalidation, exception handling. Order matters.
7. **DTOs are records** — immutable, simple data carriers.
8. **IApplicationDbContext** — the application layer depends on this interface, not EF Core directly.
9. **Minimal APIs only** — no controllers. Endpoint classes are static with `Map{Feature}Endpoints` extension methods. Handler delegates are private static methods.
10. **Scalar for API docs** — never Swagger/Swashbuckle. Use `MapScalarApiReference()`.
11. **Per-layer `DependencyInjection.cs`** — each layer registers its own services. `Program.cs` calls `AddApplication()` and `AddInfrastructure()`.
12. **Handlers return `Result<T>`** — consistent result wrapping across the application.
13. **No repository pattern unless justified** — `IApplicationDbContext` with `DbSet<T>` is the default. Only introduce repositories for specific abstraction needs.
14. **LazyCache** for in-memory caching via `ICacheable` and `ICacheInvalidator` interfaces on requests.
