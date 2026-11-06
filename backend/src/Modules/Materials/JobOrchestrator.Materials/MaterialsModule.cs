using System.Reflection;

namespace JobOrchestrator.Materials;

/// <summary>Porta de entrada do módulo Materials: currículo por vaga com proveniência, revisão cruzada e inspeção ATS.</summary>
public static class MaterialsModule
{
    public static Assembly Assembly => typeof(MaterialsModule).Assembly;

    public static IServiceCollection AddMaterialsModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddDbContext<MaterialsDbContext>(o => o.UseModuleDatabase(config.ConnectionFor("Materials"), "materials"));
        s.AddModuleReadDb<MaterialsDbContext>(config, "Materials");
        s.AddScoped<Compose.ResumeComposer>();
        s.AddScoped<IMaterialsApi>(sp => sp.GetRequiredService<Compose.ResumeComposer>());
        return s;
    }

    public static async Task MigrateMaterialsAsync(this IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MaterialsDbContext>().Database.MigrateAsync();
    }
}
