using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Infrastructure;

public interface ISqlConnectionFactory
{
    SqlConnection Create();
}

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("ConnectionStrings:Sql no configurado.");
    }

    public SqlConnection Create() => new(_connectionString);
}
