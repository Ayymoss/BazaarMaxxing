using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BazaarCompanionWeb.Context;

public class DataContextFactory : IDesignTimeDbContextFactory<DataContext>
{
    public DataContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DataContext>();
        
        // Use a dummy connection string for design time
        optionsBuilder.UseSqlite("Data Source=designTime.db",
            sqlOpt => sqlOpt.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

        return new DataContext(optionsBuilder.Options);
    }
}
