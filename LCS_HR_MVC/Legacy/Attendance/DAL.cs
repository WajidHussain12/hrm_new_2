using System;
using System.Data;
using MySql.Data.MySqlClient;

public static class DAL
{
    public static string? ConnectionString { get; set; }

    public static MySqlConnection GetConnection()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("DAL.ConnectionString has not been initialized.");
        }

        var connection = new MySqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public static DataTable ExecuteDataTable(
        MySqlConnection connection,
        CommandType commandType,
        string query,
        params MySqlParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandType = commandType;
        command.CommandText = query;

        if (parameters is { Length: > 0 })
        {
            command.Parameters.AddRange(parameters);
        }

        using var adapter = new MySqlDataAdapter(command);
        var dataTable = new DataTable();
        adapter.Fill(dataTable);
        return dataTable;
    }

    public static DataSet ExecuteDataset(
     MySqlConnection connection,
     CommandType commandType,
     string query,
     int commandTimeoutSeconds,
     params MySqlParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandType = commandType;
        command.CommandText = query;
        command.CommandTimeout = commandTimeoutSeconds;

        if (parameters is { Length: > 0 })
        {
            command.Parameters.AddRange(parameters);
        }

        using var adapter = new MySqlDataAdapter(command);
        var dataSet = new DataSet();
        adapter.Fill(dataSet);
        return dataSet;
    }
}
