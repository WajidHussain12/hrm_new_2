using System.Data;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Data
{
    public interface IDbConnectionFactory
    {
        string ConnectionString { get; }
        IDbConnection CreateConnection();
    }
}
