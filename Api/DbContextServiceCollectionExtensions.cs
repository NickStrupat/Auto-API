using Microsoft.EntityFrameworkCore;

namespace Api;

public static class DbContextServiceCollectionExtensions
{
    public static IServiceCollection AddDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        Action<ModelBuilder>? modelAction,
        Action<ModelConfigurationBuilder>? modelConfigurationAction)
    {
        services.AddDbContext<DbContext, Context>(optionsAction);
        services.AddSingleton(modelAction ?? (_ => { }));
        services.AddSingleton(modelConfigurationAction ?? (_ => { }));
        return services;
    }

    private sealed class Context(
        DbContextOptions<Context> options,
        Action<ModelBuilder> mba,
        Action<ModelConfigurationBuilder> mcba) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder mb) => mba(mb);
        protected override void ConfigureConventions(ModelConfigurationBuilder cb) => mcba(cb);
    }
    
    public static ModelBuilder AddEntitiesThatAreSubclassesOf<T>(this ModelBuilder mb) where T : class
    {
        return mb.AddEntitiesThatAreSubclassesOf(typeof(T));
    }

    public static ModelBuilder AddEntitiesThatAreSubclassesOf(this ModelBuilder mb, Type type)
    {
        var entityTypes = type.Assembly.GetTypes().Where(x => x.IsSubclassOf(type));
        foreach (var entityType in entityTypes)
            mb.Entity(entityType);
        return mb;
    }
}