using System.Data;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Data
{
    public class MySqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public MySqlConnectionFactory(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection string not found.");
        }

        public string ConnectionString => _connectionString;

        public IDbConnection CreateConnection()
        {
            return new MySqlConnection(_connectionString);
        }
    }
}
